[CmdletBinding()]
param(
    [switch]$NoRun,
    [switch]$Wait,
    [ValidateSet('Release','Debug')]
    [string]$Configuration = 'Release',
    # Build a specific repo or worktree instead of the one containing this
    # script. Accepts any path — main repo or any git worktree root.
    [string]$Target,
    # Interactive picker: lists the main repo + all linked worktrees via
    # `git worktree list` and prompts for a choice. Overrides -Target.
    [switch]$Pick,
    # Skip the --post-build flag passed to Deckle.exe on launch. The flag
    # triggers a one-shot self-restart at the app side, mitigating the
    # post-build HUD topmost glitch. Disable here for debug scenarios
    # where you want a stable PID (attached debugger, log capture, ...).
    [switch]$NoAutoRestart
)

$ErrorActionPreference = 'Stop'
$ScriptDir  = $PSScriptRoot                                  # scripts/lib/

# =============================================================================
# RepoRoot resolution
# -----------------------------------------------------------------------------
# Default: build the repo containing this script copy — the VS Code "Run"
# flow (PowerShell extension on the open file) naturally picks the
# worktree currently being edited. Two levels up from this script:
# scripts/lib/build-run.ps1 → scripts/ → <repo root>.
#
# Override: -Target "<path>" picks any path. -Pick lists the worktrees
# via the shared interactive picker (scripts/lib/_menu.psm1) and prompts.
# Both are for terminal use; VS Code Run should stay no-arg.
# =============================================================================
if ($Pick) {
    Import-Module (Join-Path $ScriptDir '_menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}

Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray

$ProjectDir = Join-Path $RepoRoot 'src\Deckle.App'
$Csproj     = Join-Path $ProjectDir 'Deckle.App.csproj'

if (-not (Test-Path $Csproj)) { throw "csproj not found at $Csproj — is '$RepoRoot' a Deckle repo?" }

# Resolve the exe path lazily after the build (the TargetPlatformVersion
# may change over time — historically 19041, now 26100 — and we don't
# want a hardcoded TFM segment to silently launch a stale binary from a
# previous TPV). We glob for the freshest net10.0-windows10.0.*\Deckle.exe
# under the configured bin directory after the build returns.
$BinConfigDir = Join-Path $ProjectDir "bin\x64\$Configuration"

# 1. Kill running instance (otherwise the .exe is locked)
Get-Process -Name Deckle -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Killing Deckle PID $($_.Id)" -ForegroundColor Yellow
    $_ | Stop-Process -Force
}

# 2. Build via `dotnet build`. Restore is implicit (separate evaluation
# phase before Build), so the WindowsAppSDK targets (CompileXaml etc.)
# get imported from the freshly-regenerated .nuget.g.targets — no
# CS5001 / CS0103 InitializeComponent surprise in a fresh worktree.
Write-Host "Build ($Configuration x64)..." -ForegroundColor Cyan
& dotnet build $Csproj "-c:$Configuration" '-p:Platform=x64' '-v:m' '-nologo'
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (code $LASTEXITCODE)" }

# 3. Run
if ($NoRun) { return }

# Resolve the freshest Deckle.exe under bin\x64\<Config>\net10.0-windows*\.
# Globbing rather than hardcoding the TFM segment so TargetPlatformVersion
# bumps (e.g. 19041 → 26100 when we needed access to MinUpdateInterval +
# IDXGIOutput6) don't silently leave us launching the stale exe from the
# old TPV folder. LastWriteTime sort picks the freshest if both old and
# new TFM dirs coexist.
$ExeCandidates = Get-ChildItem -Path $BinConfigDir -Recurse -Filter 'Deckle.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -like 'net10.0-windows10.0.*' } |
    Sort-Object LastWriteTime -Descending
if (-not $ExeCandidates) { throw "Exe not found under $BinConfigDir (expected bin\x64\$Configuration\net10.0-windows10.0.*\Deckle.exe)" }
$ExePath = $ExeCandidates[0].FullName
Write-Host "Run $ExePath" -ForegroundColor Green

# Launch via `cmd /c start` instead of `Start-Process -FilePath` so the
# new Deckle process is created via ShellExecute (detached) rather than
# CreateProcess as a child of PowerShell. Direct CreateProcess from a
# non-foreground PowerShell host makes Windows treat the new process as
# also non-foreground, and the foreground lock policy then silently
# downgrades / defers the WS_EX_TOPMOST flag that HudWindow's
# OverlappedPresenter posts at construction (IsAlwaysOnTop = true). The
# net effect: HUD launches behind every other window for the entire
# session, and only an app restart from Explorer (which routes through
# ShellExecute and gets proper foreground promotion) cures it.
# `cmd /c start "" "<path>"` is the canonical Windows idiom for "launch
# this exe as if the user had double-clicked it from the shell". The
# empty "" is the START title slot — required when the path is quoted,
# otherwise `start` interprets the quoted path as the title and silently
# does nothing.
#
# Post-build mitigation: even with cmd /c start, the first launch right
# after the build has finished still occasionally exhibits the HUD-behind
# glitch (the foreground lock heuristic appears to be sensitive to the
# timing of the PowerShell host that just spawned the build). To work
# around this, we pass --post-build to Deckle.exe so it re-launches
# itself once via cmd /c start after a short delay, then exits. The
# second instance never inherits the post-build foreground state. Pass
# -NoAutoRestart to suppress (debug-attach scenarios).
if ($NoAutoRestart) {
    & cmd /c start "" "$ExePath"
} else {
    & cmd /c start "" "$ExePath" --post-build
}

if ($Wait) {
    # cmd /c start spawned the process detached; we don't have a direct
    # handle. Poll briefly until the new Deckle process appears, then
    # wait on it. 5s ceiling so the script can't hang indefinitely if
    # the exe failed to start for an unrelated reason.
    $deadline = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $proc = Get-Process -Name Deckle -ErrorAction SilentlyContinue | Select-Object -First 1
    } while (-not $proc -and (Get-Date) -lt $deadline)
    if ($proc) { $proc.WaitForExit() }
}
