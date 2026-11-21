# verify-branch-server.ps1 - The half of verify-branch.ps1 that runs on the verification machine.
#
# verify-branch.ps1 pushes a commit into a repository of its own on the
# verification machine (win-serv), then extracts this file from that very
# commit and runs it there, so the machine never keeps a copy to update by
# hand. This script checks the commit out, builds Deckle.Tests.sln, runs
# every automatic test (the C# suites through dotnet test, the PowerShell
# suites under scripts/tests) and prints a short verdict that names the commit.
#
# The verdict is the machine's: green only when the build, every C# test and
# every PowerShell suite pass. The UI self-check is not part of it, because
# it needs an interactive Windows session that the verification machine does
# not have; the verdict says so on every run.
#
# Keep this file ASCII: the PC extracts it with `git show` through a shell
# whose console code page is not UTF-8.
#
# No Header on purpose: it runs only on the verification machine, called by
# verify-branch.ps1 with a commit to verify.
#
# Exit code: 0 green, 1 red, 2 no verdict (setup error), 3 busy.

[CmdletBinding(PositionalBinding = $false)]
param(
    # The full commit hash to verify. It must already be in the repository.
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$Sha,
    # The branch name, for the messages only: the commit is what is verified.
    [string]$Branch = '(unnamed)',
    # Where the repository, the run logs and the state file live.
    [string]$Root = (Join-Path $env:LOCALAPPDATA 'deckle-verify'),
    # How many run folders to keep; older ones are deleted at each run.
    [int]$KeepRuns = 10
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$short = $Sha.Substring(0, 8)
$repo = Join-Path $Root 'deckle'
$stateFile = Join-Path $Root 'current.json'

function Write-Verdict([string]$Color, [string]$Text) {
    Write-Host $Text -ForegroundColor $Color
}

# --- One verification at a time --------------------------------------------
# A full run peaks at about 6.2 GB on a 7.9 GB machine (measured 2026-11-17),
# so two at once would not fit: a second request is refused, not queued, so
# its caller learns at once and nothing waits on a dropped SSH connection.
# A mutex rather than a lock file: a run killed with its SSH session abandons
# the mutex, and the next request takes it over instead of finding a stale file.
$lock = [System.Threading.Mutex]::new($false, 'Global\Deckle.Verify')
$held = $false
try {
    try { $held = $lock.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $held = $true }
    if (-not $held) {
        $current = $null
        try { $current = Get-Content -Raw -LiteralPath $stateFile | ConvertFrom-Json } catch { }
        $what = if ($current) { "$($current.branch) at $($current.sha.Substring(0, 8)), started $($current.started)" } else { 'another commit' }
        Write-Verdict Yellow "BUSY  win-serv is already verifying $what."
        Write-Host "      Nothing was run for $Branch at $short. Run again once it ends (a run takes 4 to 6 minutes)."
        exit 3
    }

    $started = Get-Date
    $runDir = Join-Path $Root ("runs\{0}-{1}" -f $started.ToString('yyyyMMdd-HHmmss'), $short)
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    [pscustomobject]@{ branch = $Branch; sha = $Sha; started = $started.ToString('yyyy-MM-dd HH:mm:ss'); pid = $PID } |
        ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8

    # Old run folders go, so the disk does not fill up run after run.
    Get-ChildItem -LiteralPath (Join-Path $Root 'runs') -Directory | Sort-Object Name -Descending |
        Select-Object -Skip $KeepRuns | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    # --- The run's own token ----------------------------------------------
    # An administrator's SSH token has SeBackupPrivilege and its kin enabled;
    # .NET opens folders with backup semantics, so Deckle Folders tests that deny
    # a folder listing read it anyway and fail (measured 2026-11-17). They are
    # disabled in this process before any child starts: a child inherits a
    # copy of this token, so dotnet and the test hosts see files as a standard
    # desktop user does.
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class DeckleVerifyPrivileges {
  [StructLayout(LayoutKind.Sequential, Pack = 4)] struct TP { public int Count; public long Luid; public int Attr; }
  [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr h, int acc, out IntPtr tok);
  [DllImport("advapi32.dll", SetLastError = true)] static extern bool LookupPrivilegeValue(string s, string n, out long luid);
  [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr tok, bool all, ref TP nw, int len, IntPtr prev, IntPtr rl);
  [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
  public static bool Disable(string name) {
    IntPtr tok; if (!OpenProcessToken(GetCurrentProcess(), 0x28, out tok)) return false;
    try {
      TP tp = new TP(); tp.Count = 1; tp.Attr = 0;
      if (!LookupPrivilegeValue(null, name, out tp.Luid)) return false;
      return AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
    } finally { CloseHandle(tok); }
  }
}
'@
    foreach ($name in 'SeBackupPrivilege', 'SeRestorePrivilege', 'SeTakeOwnershipPrivilege', 'SeSecurityPrivilege', 'SeDebugPrivilege') {
        if (-not [DeckleVerifyPrivileges]::Disable($name)) { throw "Could not disable $name in the run's token." }
    }

    # English tool output, so the logs read the same on every machine.
    $env:DOTNET_CLI_UI_LANGUAGE = 'en'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

    $steps = [System.Collections.Generic.List[string]]::new()
    $red = $null          # the failing step, once one fails
    $details = [System.Collections.Generic.List[string]]::new()
    $logPath = $null

    function Invoke-Logged([string]$Log, [string[]]$Arguments) {
        & pwsh -NoProfile -NonInteractive @Arguments *> $Log
        return $LASTEXITCODE
    }
    function Get-Seconds { [int]((Get-Date) - $script:stepStart).TotalSeconds }

    # --- Checkout ---------------------------------------------------------
    # The working tree is forced to the commit and every untracked or ignored
    # file is removed except artifacts/, the build output: keeping it makes
    # the next build incremental (about 3 min 50 instead of 5 min 40).
    $script:stepStart = Get-Date
    Push-Location $repo
    try {
        & git checkout --quiet --detach --force $Sha 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "git checkout $Sha failed." }
        & git clean -ffdxq -e /artifacts/ 2>&1 | Out-Null
        $head = (& git rev-parse HEAD).Trim()
        if ($head -ne $Sha) { throw "The checkout is at $head, not $Sha." }
    } finally { Pop-Location }
    $steps.Add("checkout $(Get-Seconds) s")

    # --- Build ------------------------------------------------------------
    $script:stepStart = Get-Date
    $logPath = Join-Path $runDir 'build.log'
    Push-Location $repo
    try { $code = Invoke-Logged $logPath @('-File', 'scripts\build-locked.ps1', '-Project', 'Deckle.Tests.sln') } finally { Pop-Location }
    $steps.Add("build $(Get-Seconds) s")
    if ($code -ne 0) {
        $red = 'build'
        $errors = @(Select-String -LiteralPath $logPath -Pattern ': error ' | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique)
        foreach ($line in ($errors | Select-Object -First 10)) { $details.Add("  $line") }
        if ($errors.Count -gt 10) { $details.Add("  ... and $($errors.Count - 10) more errors") }
        if ($errors.Count -eq 0) { Get-Content -LiteralPath $logPath -Tail 5 | ForEach-Object { $details.Add("  $_") } }
    }

    # --- C# tests ---------------------------------------------------------
    $testSummary = ''
    if (-not $red) {
        $script:stepStart = Get-Date
        $logPath = Join-Path $runDir 'test.log'
        $trxDir = Join-Path $runDir 'trx'
        Push-Location $repo
        try {
            $code = Invoke-Logged $logPath @('-File', 'scripts\build-locked.ps1', '-Test', '-Project', 'Deckle.Tests.sln',
                '--no-build', '--logger', 'trx', '--results-directory', $trxDir)
        } finally { Pop-Location }
        $steps.Add("tests $(Get-Seconds) s")

        # The TRX files are the structured result, one per suite: they name
        # each failed test with its message, whatever the console printed.
        $ns = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
        $trx = @(Get-ChildItem -LiteralPath $trxDir -Filter *.trx -ErrorAction SilentlyContinue)
        $total = 0; $passed = 0; $failedCount = 0
        $failures = [System.Collections.Generic.List[object]]::new()
        foreach ($file in $trx) {
            $xml = [xml](Get-Content -Raw -LiteralPath $file.FullName)
            $counters = (Select-Xml -Xml $xml -XPath '//t:ResultSummary/t:Counters' -Namespace $ns).Node
            if ($counters) {
                $total += [int]$counters.total; $passed += [int]$counters.passed
                # An error, a timeout or an abort is no pass either.
                $failedCount += [int]$counters.failed + [int]$counters.error + [int]$counters.timeout + [int]$counters.aborted
            }
            foreach ($node in (Select-Xml -Xml $xml -XPath "//t:UnitTestResult[@outcome='Failed']" -Namespace $ns)) {
                $message = (Select-Xml -Xml $node.Node -XPath './t:Output/t:ErrorInfo/t:Message' -Namespace $ns).Node.InnerText
                $failures.Add([pscustomobject]@{ name = $node.Node.testName; message = "$message" })
            }
        }
        # Skipped tests are skipped by their own code (a Skip reason or a
        # condition the test checks); they are counted, not judged.
        $testSummary = "$passed tests passed, $($total - $passed - $failedCount) skipped, $failedCount failed in $($trx.Count) suites"
        if ($code -ne 0 -or $failures.Count -gt 0 -or $trx.Count -eq 0 -or $failedCount -gt 0) {
            $red = 'tests'
            $details.Add("  $testSummary")
            foreach ($failure in ($failures | Select-Object -First 10)) {
                $details.Add("  FAIL $($failure.name)")
                $failure.message -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 3 |
                    ForEach-Object { $details.Add("       $($_.Trim())") }
            }
            if ($failures.Count -gt 10) { $details.Add("  ... and $($failures.Count - 10) more failed tests") }
            if ($failures.Count -eq 0) {
                # A crashed test host or a suite that never started leaves no
                # failed result: the end of the log is then the only clue.
                if ($trx.Count -eq 0) { $details.Add('  No test result was written.') }
                Get-Content -LiteralPath $logPath | Where-Object { $_ -match 'error|crash|aborted|exited' } |
                    Select-Object -Last 5 | ForEach-Object { $details.Add("  $($_.Trim())") }
            }
        }
    }

    # --- PowerShell tests -------------------------------------------------
    # The suites under scripts/tests are plain scripts that throw on a broken
    # assertion; each one runs in its own pwsh and fails on a non-zero exit.
    $scriptSummary = ''
    if (-not $red) {
        $script:stepStart = Get-Date
        $logPath = Join-Path $runDir 'script-tests.log'
        $suites = @(Get-ChildItem -LiteralPath (Join-Path $repo 'scripts\tests') -Recurse -File -Filter '*.tests.ps1' | Sort-Object FullName)
        $broken = [System.Collections.Generic.List[object]]::new()
        foreach ($suite in $suites) {
            $relative = [IO.Path]::GetRelativePath($repo, $suite.FullName)
            $suiteLog = Join-Path $runDir ("script-tests\{0}.log" -f $suite.BaseName)
            New-Item -ItemType Directory -Force -Path (Split-Path $suiteLog) | Out-Null
            Push-Location $repo
            try { $code = Invoke-Logged $suiteLog @('-File', $suite.FullName) } finally { Pop-Location }
            "$relative exit $code" | Add-Content -LiteralPath $logPath
            if ($code -ne 0) { $broken.Add([pscustomobject]@{ name = $relative; log = $suiteLog }) }
        }
        $steps.Add("script tests $(Get-Seconds) s")
        $scriptSummary = "$($suites.Count - $broken.Count) of $($suites.Count) PowerShell suites passed"
        if ($suites.Count -eq 0 -or $broken.Count -gt 0) {
            $red = 'script tests'
            $details.Add("  $scriptSummary")
            foreach ($suite in $broken) {
                $details.Add("  FAIL $($suite.name)")
                Get-Content -LiteralPath $suite.log | Where-Object { $_.Trim() } | Select-Object -Last 3 |
                    ForEach-Object { $details.Add("       $($_.Trim())") }
            }
        }
    }

    # --- Verdict ----------------------------------------------------------
    $minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
    $verdict = [pscustomobject]@{
        verdict   = if ($red) { 'red' } else { 'green' }
        sha       = $Sha
        branch    = $Branch
        step      = $red
        started   = $started.ToString('yyyy-MM-dd HH:mm:ss')
        minutes   = $minutes
        steps     = @($steps)
        tests     = $testSummary
        scripts   = $scriptSummary
        selfCheck = 'not run'
        log       = $logPath
    }
    $verdict | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDir 'verdict.json') -Encoding utf8

    if ($red) {
        Write-Verdict Red "RED   $Branch at $Sha"
        Write-Host "      Failed at: $red ($minutes min; $($steps -join ', '))"
        $details | ForEach-Object { Write-Host $_ }
        Write-Host "      Full log: win-serv:$logPath"
    } else {
        Write-Verdict Green "GREEN $Branch at $Sha"
        Write-Host "      Build, $testSummary, $scriptSummary ($minutes min)."
    }
    Write-Host '      Self-check: not run (it needs an interactive Windows session on win-serv).'
    exit $(if ($red) { 1 } else { 0 })
} catch {
    Write-Verdict Yellow "NO VERDICT  $Branch at $short"
    Write-Host "      The verification could not run: $($_.Exception.Message)"
    exit 2
} finally {
    if ($held) {
        Remove-Item -LiteralPath $stateFile -ErrorAction SilentlyContinue
        $lock.ReleaseMutex()
    }
    $lock.Dispose()
}
