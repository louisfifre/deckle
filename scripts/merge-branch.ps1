<#
.SYNOPSIS
    Merge a branch into main behind the merge gate
.DESCRIPTION
    The one way a branch enters main. Runs the merge gate on the branch's last
    commit, in the worktree that holds it, on this machine: it builds
    Deckle.Tests.sln, runs every C# test and every PowerShell test, then the
    self-check of each app the branch changes. On a green gate it merges the
    branch into main with a merge commit; on a red or unstable result it
    refuses. It ends with a table of what it checked, each check green, red or
    never seen. It never records the version, pushes or deploys: those stay
    the next steps AGENTS.md names, and the script prints them. Exit code: 0
    green (merged, or not asked to merge), 1 refused (red or unstable), 2 not
    run (the branch, the worktree or main was not ready).
.PARAMETER Branch
    The branch to merge. The branch checked out in -Target by default.
.PARAMETER Target
    The worktree to run the gate in, which must hold the branch. The checkout
    this script lives in by default; with -Branch, the worktree that holds it.
.PARAMETER Summary
    What the branch brings, in one line: the merge commit reads
    "Merge <branch>: <summary>". Required unless -NoMerge.
.PARAMETER NoMerge
    Run the gate and print its table, then stop: nothing is merged.
.PARAMETER MainCheckout
    The checkout where main is checked out. The repository's primary checkout
    (the first one git worktree list names) by default.
.NOTES
    section: Run
    order: 129
#>

# How it works:
#   1. The gate tests what lands. The branch must already contain origin's
#      main; a merge commit of such a branch into main has exactly the
#      branch's tree, which git merge-tree confirms before main moves.
#   2. The steps are those of the win-serv verdict (verify-branch-server.ps1),
#      run here because the self-check needs the maintainer's interactive
#      session. The first red step ends the run; the steps after it are
#      "never seen".
#   3. A flaky test blocks everything until it is fixed, so a red is never
#      rerun into a green: every run that reached the tests is kept in the
#      repository's git folder (deckle-merge-gate\history.jsonl, shared by the
#      worktrees, never committed). A check that failed once and passed once on
#      the same tree is unstable, and the gate refuses until the tree changes.

[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Branch,
    [string]$Target,
    [string]$Summary,
    [switch]$NoMerge,
    [string]$MainCheckout
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$started = Get-Date
Import-Module (Join-Path $PSScriptRoot 'lib\merge-gate.psm1') -Force

function Stop-NotRun([string]$Message) {
    Write-Host "NOT RUN  $Message" -ForegroundColor Yellow
    exit 2
}
function Invoke-Git([string]$Repo, [string[]]$Arguments) {
    $out = & git -C $Repo @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return $out
}

# --- Which branch, which worktree, which main ----------------------------
$here = if ($Target) { (Resolve-Path -LiteralPath $Target).Path } else { Split-Path -Parent $PSScriptRoot }
$worktrees = @{}
$primary = $null
$current = $null
foreach ($line in (& git -C $here worktree list --porcelain)) {
    if ($line -like 'worktree *') { $current = [IO.Path]::GetFullPath($line.Substring(9)); if (-not $primary) { $primary = $current } }
    elseif ($line -like 'branch refs/heads/*') { $worktrees[$line.Substring(18)] = $current }
}
if (-not $Branch) {
    $Branch = Invoke-Git $here @('symbolic-ref', '--quiet', '--short', 'HEAD')
    if (-not $Branch) { Stop-NotRun "$here is not on a branch. Name one with -Branch." }
}
if ($Branch -eq 'main') { Stop-NotRun 'main is where branches land; name the branch to merge.' }
$repo = $worktrees[$Branch]
if (-not $repo) { Stop-NotRun "No worktree holds $Branch. Check it out in its worktree first: the gate builds and runs that worktree." }
if ($Target -and $repo -ne [IO.Path]::GetFullPath($here)) { Stop-NotRun "$here does not hold $Branch; $repo does." }
$sha = "$(Invoke-Git $repo @('rev-parse', 'HEAD'))".Trim()
$tree = "$(Invoke-Git $repo @('rev-parse', 'HEAD^{tree}'))".Trim()
$short = $sha.Substring(0, 8)

$dirty = @(& git -C $repo status --porcelain --untracked-files=normal)
if ($dirty.Count -gt 0) { Stop-NotRun "$repo has $($dirty.Count) uncommitted change(s). The gate judges a commit: commit them first." }

if (-not $NoMerge) {
    if (-not $Summary) { Stop-NotRun 'Give -Summary, the line the merge commit carries after "Merge <branch>:", or run with -NoMerge.' }
    $main = if ($MainCheckout) { (Resolve-Path -LiteralPath $MainCheckout).Path } else { $primary }
    if ((Invoke-Git $main @('symbolic-ref', '--quiet', '--short', 'HEAD')) -ne 'main') { Stop-NotRun "$main is not on main." }
    if (@(& git -C $main status --porcelain --untracked-files=no).Count -gt 0) { Stop-NotRun "$main has uncommitted changes on main." }
}

# The gate tests what lands only when the branch already holds origin's main.
& git -C $repo fetch --quiet origin main 2>$null
$fetched = $LASTEXITCODE -eq 0
$originMain = "$(Invoke-Git $repo @('rev-parse', 'refs/remotes/origin/main'))".Trim()
$holdsMain = $false
if ($originMain) {
    & git -C $repo merge-base --is-ancestor $originMain $sha 2>$null
    $holdsMain = $LASTEXITCODE -eq 0
}
if (-not $NoMerge) {
    if (-not $fetched -or -not $originMain) { Stop-NotRun 'origin could not be fetched: a merge starts from origin''s main.' }
    if (-not $holdsMain) { Stop-NotRun "$Branch does not contain origin/main ($($originMain.Substring(0, 8))). Merge origin/main into it first, so the gate tests what lands." }
}

Write-Host "merge gate: $Branch at $short in $repo"
if (-not $holdsMain) { Write-Host 'merge gate: the branch does not contain origin/main yet; this run speaks of the branch alone.' -ForegroundColor Yellow }

$runDir = Join-Path ([IO.Path]::GetTempPath()) "deckle-merge-gate\$(Get-Date -Format 'yyyy-MM-dd_HH-mm-ss')_$short"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$rows = [System.Collections.Generic.List[object]]::new()
$ran = [System.Collections.Generic.List[string]]::new()
$failed = [System.Collections.Generic.List[string]]::new()
$details = [System.Collections.Generic.List[string]]::new()
$red = $null

function Add-Row([string]$Check, [string]$Proof, [string]$State) {
    $rows.Add([pscustomobject]@{ Check = $Check; Proof = $Proof; State = $State })
}
function Invoke-Logged([string]$Log, [string[]]$Arguments) {
    Push-Location $repo
    try { & pwsh -NoProfile -NonInteractive @Arguments *> $Log } finally { Pop-Location }
    return $LASTEXITCODE
}
function Get-Seconds([datetime]$From) { [int]((Get-Date) - $From).TotalSeconds }
$buildLocked = Join-Path $repo 'scripts\build-locked.ps1'

# --- Build ------------------------------------------------------------------
$stepStart = Get-Date
Write-Host 'merge gate: building Deckle.Tests.sln...'
$log = Join-Path $runDir 'build.log'
$code = Invoke-Logged $log @('-File', $buildLocked, '-Project', 'Deckle.Tests.sln')
if ($code -ne 0) {
    $red = 'build'
    Add-Row 'Build of Deckle.Tests.sln' "build-locked.ps1, $(Get-Seconds $stepStart) s, log $log" 'red'
    $errors = @(Select-String -LiteralPath $log -Pattern ': error ' | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique)
    foreach ($line in ($errors | Select-Object -First 10)) { $details.Add("  $line") }
    if ($errors.Count -eq 0) { Get-Content -LiteralPath $log -Tail 5 | ForEach-Object { $details.Add("  $_") } }
}
else {
    Add-Row 'Build of Deckle.Tests.sln' "build-locked.ps1, $(Get-Seconds $stepStart) s" 'green'
}

# --- C# tests ---------------------------------------------------------------
if (-not $red) {
    $stepStart = Get-Date
    $ran.Add('tests')
    Write-Host 'merge gate: running every C# test...'
    $log = Join-Path $runDir 'test.log'
    $trxDir = Join-Path $runDir 'trx'
    $code = Invoke-Logged $log @('-File', $buildLocked, '-Test', '-Project', 'Deckle.Tests.sln',
        '--no-build', '--logger', 'trx', '--results-directory', $trxDir)
    # The TRX files name each failed test with its message, one file per suite.
    $ns = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
    $trx = @(Get-ChildItem -LiteralPath $trxDir -Filter *.trx -ErrorAction SilentlyContinue)
    $total = 0; $passed = 0; $failedCount = 0
    foreach ($file in $trx) {
        $xml = [xml](Get-Content -Raw -LiteralPath $file.FullName)
        $counters = (Select-Xml -Xml $xml -XPath '//t:ResultSummary/t:Counters' -Namespace $ns).Node
        if ($counters) {
            $total += [int]$counters.total; $passed += [int]$counters.passed
            $failedCount += [int]$counters.failed + [int]$counters.error + [int]$counters.timeout + [int]$counters.aborted
        }
        foreach ($node in (Select-Xml -Xml $xml -XPath "//t:UnitTestResult[@outcome='Failed']" -Namespace $ns)) {
            $failed.Add("tests: $($node.Node.testName)")
            $message = (Select-Xml -Xml $node.Node -XPath './t:Output/t:ErrorInfo/t:Message' -Namespace $ns).Node.InnerText
            $details.Add("  FAIL $($node.Node.testName)")
            "$message" -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 3 | ForEach-Object { $details.Add("       $($_.Trim())") }
        }
    }
    $proof = "$passed passed, $($total - $passed - $failedCount) skipped, $failedCount failed in $($trx.Count) suites, $(Get-Seconds $stepStart) s"
    if ($code -ne 0 -or $failedCount -gt 0 -or $trx.Count -eq 0) {
        $red = 'tests'
        # A crashed test host leaves no failed result: it is named by its step.
        if (@($failed | Where-Object { $_ -like 'tests: *' }).Count -eq 0) {
            $failed.Add('tests: the test run')
            Get-Content -LiteralPath $log | Where-Object { $_ -match 'error|crash|aborted|exited' } | Select-Object -Last 5 | ForEach-Object { $details.Add("  $($_.Trim())") }
        }
        Add-Row 'Every C# test (Deckle.Tests.sln)' "$proof, log $log" 'red'
    }
    else {
        Add-Row 'Every C# test (Deckle.Tests.sln)' $proof 'green'
    }
}
else { Add-Row 'Every C# test (Deckle.Tests.sln)' "not run: $red was red" 'never seen' }

# --- PowerShell tests -------------------------------------------------------
if (-not $red) {
    $stepStart = Get-Date
    $ran.Add('scripts')
    Write-Host 'merge gate: running every PowerShell test...'
    $suites = @(Get-ChildItem -LiteralPath (Join-Path $repo 'scripts\tests') -Recurse -File -Filter '*.tests.ps1' | Sort-Object FullName)
    $broken = 0
    foreach ($suite in $suites) {
        $relative = [IO.Path]::GetRelativePath($repo, $suite.FullName) -replace '\\', '/'
        $suiteLog = Join-Path $runDir "script-tests\$($suite.BaseName).log"
        New-Item -ItemType Directory -Force -Path (Split-Path $suiteLog) | Out-Null
        if ((Invoke-Logged $suiteLog @('-File', $suite.FullName)) -ne 0) {
            $broken++
            $failed.Add("scripts: $relative")
            $details.Add("  FAIL $relative")
            Get-Content -LiteralPath $suiteLog | Where-Object { $_.Trim() } | Select-Object -Last 3 | ForEach-Object { $details.Add("       $($_.Trim())") }
        }
    }
    $proof = "$($suites.Count - $broken) of $($suites.Count) suites passed, $(Get-Seconds $stepStart) s"
    if ($suites.Count -eq 0 -or $broken -gt 0) { $red = 'scripts'; Add-Row 'Every PowerShell test (scripts/tests)' $proof 'red' }
    else { Add-Row 'Every PowerShell test (scripts/tests)' $proof 'green' }
}
else { Add-Row 'Every PowerShell test (scripts/tests)' "not run: $red was red" 'never seen' }

# --- Self-check of each app the branch changes -----------------------------
$base = if ($originMain) { "$(Invoke-Git $repo @('merge-base', $originMain, $sha))".Trim() } else { '' }
$changed = if ($base) { @(Invoke-Git $repo @('diff', '--name-only', $base, $sha)) } else { @() }
$apps = @(Get-GateTouchedApps -RepoRoot $repo -ChangedPaths $changed)
if ($apps.Count -eq 0) { Write-Host 'merge gate: the branch changes no app, so no self-check runs.' }
foreach ($app in $apps) {
    $check = "Self-check of $($app.App) (reached by $($app.Because))"
    if ($red) { Add-Row $check "not run: $red was red" 'never seen'; continue }
    $stepStart = Get-Date
    $step = "self-check $($app.App)"
    $ran.Add($step)
    Write-Host "merge gate: self-check of $($app.App)..."
    $out = Join-Path $runDir "self-check-$($app.App)"
    $log = Join-Path $runDir "self-check-$($app.App).log"
    $code = Invoke-Logged $log @('-File', (Join-Path $repo 'scripts\self-check.ps1'), '-App', $app.App, '-Target', $repo, '-OutputFolder', $out)
    $proof = "self-check.ps1 -App $($app.App), $(Get-Seconds $stepStart) s, captures in $out"
    if ($code -ne 0) {
        $red = $step
        $failed.Add("${step}: the walk")
        Add-Row $check "$proof, log $log" 'red'
        Get-Content -LiteralPath $log | Where-Object { $_ -match '^FAILED|NOT LOADED' } | Select-Object -First 10 | ForEach-Object { $details.Add("  $($_.Trim())") }
    }
    else { Add-Row $check $proof 'green' }
}

# --- Unstable checks --------------------------------------------------------
# Only a run that reached the tests is kept: a red build is not a test result.
$unstable = @()
if ($ran.Count -gt 0) {
    $gitDir = "$(Invoke-Git $repo @('rev-parse', '--path-format=absolute', '--git-common-dir'))".Trim()
    $historyPath = Join-Path $gitDir 'deckle-merge-gate\history.jsonl'
    New-Item -ItemType Directory -Force -Path (Split-Path $historyPath) | Out-Null
    $history = @()
    if (Test-Path -LiteralPath $historyPath) {
        $history = @(Get-Content -LiteralPath $historyPath | Where-Object { $_.Trim() } | ForEach-Object { try { $_ | ConvertFrom-Json } catch { } })
    }
    $run = [pscustomobject]@{ when = $started.ToString('yyyy-MM-dd HH:mm:ss'); branch = $Branch; sha = $sha; tree = $tree; ran = @($ran); failed = @($failed) }
    $unstable = @(Get-GateUnstableChecks -Tree $tree -History $history -Current $run)
    ($run | ConvertTo-Json -Compress) | Add-Content -LiteralPath $historyPath -Encoding utf8
    foreach ($name in $unstable) {
        $when = @($history | Where-Object { $_.tree -eq $tree } | ForEach-Object { $_.when }) -join ', '
        Add-Row "Stability of $name" "failed and passed on this same tree (runs of $when and now)" 'red'
    }
}

# --- Verdict ----------------------------------------------------------------
$minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
Write-Host ''
Format-GateTable -Rows $rows | ForEach-Object { Write-Host $_ }
Write-Host ''
if ($red -or $unstable.Count -gt 0) {
    $why = if ($red) { "$red is red" } else { 'a check is unstable: a flaky test blocks every merge until it is fixed' }
    Write-Host "REFUSED  $Branch at $short ($why; $minutes min)" -ForegroundColor Red
    $details | ForEach-Object { Write-Host $_ }
    if ($unstable.Count -gt 0) {
        Write-Host '         A test that counts events may race: read tests/process-wide-events.md'
    }
    Write-Host "         Logs: $runDir"
    exit 1
}
if ($NoMerge) {
    Write-Host "GREEN  $Branch at $short ($minutes min). Not merged: -NoMerge." -ForegroundColor Green
    exit 0
}

# --- Merge ------------------------------------------------------------------
# The branch, main and origin's main must be where the gate left them.
& git -C $main fetch --quiet origin main 2>$null
if ($LASTEXITCODE -ne 0) { Stop-NotRun 'GREEN, but origin could not be fetched again; nothing was merged.' }
$mainSha = "$(Invoke-Git $main @('rev-parse', 'main'))".Trim()
$originNow = "$(Invoke-Git $main @('rev-parse', 'refs/remotes/origin/main'))".Trim()
if ("$(Invoke-Git $main @('rev-parse', "refs/heads/$Branch"))".Trim() -ne $sha) { Stop-NotRun "GREEN, but $Branch moved during the run; run the gate again. Nothing was merged." }
if ($mainSha -ne $originNow) { Stop-NotRun "GREEN, but main ($($mainSha.Substring(0, 8))) is not origin's main ($($originNow.Substring(0, 8))). Bring main to origin's main, merge origin/main into $Branch if it moved, and run the gate again. Nothing was merged." }
$mergedTree = "$(Invoke-Git $main @('merge-tree', '--write-tree', 'main', $sha))".Trim()
if ($mergedTree -ne $tree) { Stop-NotRun "GREEN, but the merge would not have the tree the gate tested. Merge origin/main into $Branch and run the gate again. Nothing was merged." }

& git -C $main merge --no-ff --quiet -m "Merge ${Branch}: $Summary" $sha
if ($LASTEXITCODE -ne 0) { Stop-NotRun "GREEN, but git merge failed in $main; look at it there." }
$merge = "$(Invoke-Git $main @('rev-parse', 'HEAD'))".Trim()
Write-Host "MERGED  $Branch at $short into main as $($merge.Substring(0, 8)) ($minutes min)." -ForegroundColor Green
Write-Host '        Next, as AGENTS.md says: record the version (scripts/git/record-version.ps1 -Bump <part> -Apply), push main and its tag, then run scripts/deploy-prod.ps1.'
exit 0
