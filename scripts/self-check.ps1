<#
.SYNOPSIS
    Self-check the built app
.DESCRIPTION
    Runs the built Deckle once in its self-check mode and judges its report.
    Takes the already-built Deckle.exe of this checkout (it never builds), runs it
    with --self-check on a fresh data root of its own, waits for it, and reads
    the report.json it writes. The app opens its Settings window, walks every
    page, opens the Log window and the Playground, probes the microphone once
    and exits (see src/Deckle.App/App.SelfCheck.cs for what it composes and why).

    It runs beside the maintainer's resident Deckle: it takes no mutex of the
    resident app, composes no hook, no hotkey, no tray icon and no engine, and it
    never reads the maintainer's data root. The script prints the process ids of
    the other Deckle processes before and after, so a run that touched the
    resident app would show.

    A run fails when the app exits with anything but 0 or 1 (a crash exit code
    such as 0xC00000FD, a stack overflow, is named), when it times out, when no
    report was written, or when the report says "failed". Exit code of this
    script: 0 passed, 1 failed.
.PARAMETER Configuration
    Which build to run. Debug by default, as the repository builds.
.PARAMETER Target
    The checkout whose build is run. The checkout this script lives in by default.
.PARAMETER TimeoutSeconds
    How long the app may take. The walk is a few seconds per dozen pages; the
    default leaves room for a cold start.
.PARAMETER OutputFolder
    Where report.json and the captures go. A new folder under the temp folder by
    default; it is kept, the report and the images are the output.
.PARAMETER LinkRuntime
    Link the maintainer's native\ and models\ folders into the fresh root, so the
    Dictation pages render in their provisioned state. Off by default: the run is
    then the same on every machine, and a page that deletes a model cannot reach
    the maintainer's. A junction cannot be read-only, so this stays opt-in; the
    app never writes there in this mode, but nothing enforces it.
.PARAMETER KeepData
    Keep the fresh data root after the run (it holds the run's log.jsonl).
.NOTES
    section: Run
    order: 125
    action: Self-check |
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$Target,
    [ValidateRange(10, 1800)]
    [int]$TimeoutSeconds = 180,
    [string]$OutputFolder,
    [switch]$LinkRuntime,
    [switch]$KeepData
)

$ErrorActionPreference = 'Stop'

$RepoRoot = if ($Target) { (Resolve-Path -LiteralPath $Target).Path } else { Split-Path -Parent $PSScriptRoot }
$ExePath = Join-Path $RepoRoot "artifacts\bin\Deckle.App\$($Configuration.ToLowerInvariant())\Deckle.exe"
if (-not (Test-Path -LiteralPath $ExePath)) {
    Write-Host "self-check: no built app at $ExePath. Build it first (scripts/build-locked.ps1 -Project src/Deckle.App/Deckle.App.csproj)." -ForegroundColor Red
    exit 1
}

# A run folder of its own, so two runs never share a log or a report.
$RunName = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$RunRoot = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-self-check\$RunName"
$DataRoot = Join-Path $RunRoot 'data'
if (-not $OutputFolder) { $OutputFolder = Join-Path $RunRoot 'out' }
New-Item -ItemType Directory -Force -Path $DataRoot, $OutputFolder | Out-Null

# What the fresh root needs: nothing. The mode never opens the first-run wizard,
# and with no module choice on disk every module counts as present, so every
# settings page is registered, which is what the walk must see. The app writes
# the one setting it needs (the Playground) in its own root. Nothing is copied
# from the maintainer's root: Anytype data, telemetry, the personal dictionary
# and secrets.dat stay where they are.
if ($LinkRuntime) {
    $defaultRoot = Join-Path $env:LOCALAPPDATA 'Deckle'
    foreach ($folder in 'native', 'models') {
        $source = Join-Path $defaultRoot $folder
        if (Test-Path -LiteralPath $source) {
            New-Item -ItemType Junction -Path (Join-Path $DataRoot $folder) -Target $source | Out-Null
        }
    }
}

function Get-OtherDeckleIds([int]$Except) {
    @(Get-Process -Name Deckle -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $Except } | ForEach-Object { $_.Id } | Sort-Object)
}

$Before = Get-OtherDeckleIds -Except 0

# DECKLE_DATA_ROOT goes to the child's environment only: this process, and the
# shell that called it, keep theirs.
$psi = [System.Diagnostics.ProcessStartInfo]::new($ExePath)
$psi.UseShellExecute = $false
$psi.WorkingDirectory = Split-Path -Parent $ExePath
$psi.ArgumentList.Add('--self-check')
$psi.ArgumentList.Add($OutputFolder)
$psi.Environment['DECKLE_DATA_ROOT'] = $DataRoot

Write-Host "self-check: $ExePath"
Write-Host "self-check: data root $DataRoot"
# The guard: the run must never be seen nor take the focus. While the app runs,
# this script samples every 20 ms which window has the foreground and where the
# app's visible windows stand. A foreground window owned by the app, or a window
# rectangle that touches a monitor, is a failure whatever the report says.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class DeckleSelfCheckGuard
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    public static long Foreground() { return GetForegroundWindow().ToInt64(); }

    public static uint ForegroundPid()
    {
        uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return pid;
    }

    // Visible top-level windows of a process: "hwnd:left,top,right,bottom".
    public static List<string> VisibleWindows(uint ownerPid)
    {
        var found = new List<string>();
        EnumWindows((hwnd, l) =>
        {
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            RECT r;
            if (pid == ownerPid && IsWindowVisible(hwnd) && GetWindowRect(hwnd, out r)
                && r.right > r.left && r.bottom > r.top)
                found.Add(hwnd.ToInt64() + ":" + r.left + "," + r.top + "," + r.right + "," + r.bottom);
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

$monitors = @([System.Windows.Forms.Screen]::AllScreens | ForEach-Object { $_.Bounds })
$guardViolations = [System.Collections.Generic.List[string]]::new()
$foregroundBefore = [DeckleSelfCheckGuard]::Foreground()
$foregroundSeen = [System.Collections.Generic.HashSet[long]]::new()
[void]$foregroundSeen.Add($foregroundBefore)
$windowsSeen = 0

$process = [System.Diagnostics.Process]::Start($psi)
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
    [void]$foregroundSeen.Add([DeckleSelfCheckGuard]::Foreground())
    if ([DeckleSelfCheckGuard]::ForegroundPid() -eq [uint32]$process.Id) {
        $guardViolations.Add('a window of the self-check had the foreground')
    }
    foreach ($entry in [DeckleSelfCheckGuard]::VisibleWindows([uint32]$process.Id)) {
        $windowsSeen++
        $rect = ($entry -split ':')[1] -split ','
        $l = [int]$rect[0]; $t = [int]$rect[1]; $r = [int]$rect[2]; $b = [int]$rect[3]
        foreach ($m in $monitors) {
            if ($l -lt $m.Right -and $r -gt $m.Left -and $t -lt $m.Bottom -and $b -gt $m.Top) {
                $guardViolations.Add("a self-check window ($l,$t,$r,$b) touched the monitor at $($m.Left),$($m.Top),$($m.Right),$($m.Bottom)")
            }
        }
    }
    Start-Sleep -Milliseconds 20
}
$foregroundAfter = [DeckleSelfCheckGuard]::Foreground()
[void]$foregroundSeen.Add($foregroundAfter)
$timedOut = -not $process.HasExited
if ($timedOut) {
    # The tree: a hung app may have started helpers.
    try { $process.Kill($true) } catch { }
    $process.WaitForExit(10000) | Out-Null
}
$exitCode = if ($timedOut) { $null } else { $process.ExitCode }
$childId = $process.Id

$After = Get-OtherDeckleIds -Except $childId

# Known process-death codes, named so the summary says what happened.
$crashNames = @{
    'C00000FD' = 'stack overflow'
    'C0000005' = 'access violation'
    'C0000409' = 'fail fast / stack buffer overrun'
    'E0434352' = 'unhandled .NET exception'
    'C0000374' = 'heap corruption'
}

$failures = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

foreach ($violation in @($guardViolations | Select-Object -Unique)) {
    $failures.Add("seen or focused: $violation.")
}

$ReportPath = Join-Path $OutputFolder 'report.json'
$report = $null
if (Test-Path -LiteralPath $ReportPath) {
    try { $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json } catch { }
}

if ($timedOut) {
    $failures.Add("timeout: the app did not finish in $TimeoutSeconds s and was killed.")
}
elseif ($exitCode -eq 2) {
    $reason = if ($report -and $report.reason) { $report.reason } else { 'no reason in the report' }
    $failures.Add("refused: the app refused to run ($reason)")
}
elseif ($exitCode -ne 0 -and $exitCode -ne 1) {
    $hex = '{0:X8}' -f [uint32]($exitCode -band 0xFFFFFFFFL)
    $code = "0x$hex"
    $name = $crashNames[$hex]
    $failures.Add("crash: the app ended with exit code $code" + $(if ($name) { " ($name)" } else { '' }) + '.')
}

if (-not $report) {
    $failures.Add("no report: $ReportPath is missing or unreadable.")
}
else {
    if (-not $report.completed) {
        $failures.Add("the run did not complete; the last step it started: $($report.current)")
    }
    foreach ($f in @($report.failures)) { if ($f -and -not $failures.Contains($f)) { $failures.Add($f) } }
    foreach ($w in @($report.warnings)) { if ($w) { $warnings.Add($w) } }
    if ($exitCode -eq 0 -and $report.outcome -ne 'passed') {
        $failures.Add("the app exited 0 but its report says '$($report.outcome)'.")
    }
}

# With no resident Deckle both lists are empty, and Compare-Object refuses a
# null reference set: compare only when there is something to compare.
$Before = @($Before)
$After = @($After)
if ($Before.Count -ne $After.Count -or ($Before.Count -gt 0 -and (Compare-Object $Before $After))) {
    $failures.Add("the other Deckle processes changed during the run: before [$($Before -join ', ')], after [$($After -join ', ')].")
}

# Summary.
Write-Host ''
if ($report -and $report.surfaces) {
    $loaded = @($report.surfaces | Where-Object { $_.loaded }).Count
    Write-Host ("pages and windows: {0} walked, {1} loaded" -f @($report.surfaces).Count, $loaded)
    foreach ($s in @($report.surfaces | Where-Object { -not $_.loaded })) {
        $name = if ($s.page) { "$($s.window)/$($s.page)" } else { $s.window }
        Write-Host "  NOT LOADED  $name" -ForegroundColor Red
    }
}
if ($report -and $report.microphone) {
    Write-Host ("microphone: {0} (stage {1}, {2})" -f $report.microphone.outcome, $report.microphone.stage, $report.microphone.error_name)
}
if ($report -and $report.version) { Write-Host "version: $($report.version) ($($report.build))" }
Write-Host ("foreground window: before 0x{0:X}, after 0x{1:X}, {2} distinct during the run; the app's visible windows were sampled {3} time(s)" -f $foregroundBefore, $foregroundAfter, $foregroundSeen.Count, $windowsSeen)
Write-Host "resident Deckle processes before: [$($Before -join ', ')]  after: [$($After -join ', ')]"
foreach ($w in $warnings) { Write-Host "warning: $w" -ForegroundColor Yellow }
foreach ($f in $failures) { Write-Host "FAILED: $f" -ForegroundColor Red }
Write-Host "report: $ReportPath"

if (-not $KeepData) {
    # A junction goes first so the removal never follows it into the maintainer's folders.
    foreach ($folder in 'native', 'models') {
        $link = Join-Path $DataRoot $folder
        if (Test-Path -LiteralPath $link) { (Get-Item -LiteralPath $link).Delete() }
    }
    # Held open for a moment by the app's last writes; a leftover temp folder is harmless.
    try { Remove-Item -LiteralPath $DataRoot -Recurse -Force -ErrorAction Stop } catch { }
}

if ($failures.Count -gt 0) {
    Write-Host 'self-check: FAILED' -ForegroundColor Red
    exit 1
}
Write-Host 'self-check: passed' -ForegroundColor Green
exit 0
