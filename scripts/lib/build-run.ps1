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
    [switch]$NoAutoRestart,
    # Diagnostic branch only: keep this script's build+launch path, but ask
    # Deckle to relaunch into a bounded HUD z-order self-test instead of
    # staying resident.
    [switch]$HudZOrderSelfTest
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

# Artifacts output layout (see root Directory.Build.props): every bin/ is
# consolidated under <RepoRoot>\artifacts\bin\<Project>\<pivot>\. For the App
# the pivot is just the lowercased configuration (single-TFM, so no TFM
# segment; x64 is not a pivot). We still resolve the exe lazily AFTER the
# build and glob by pivot prefix rather than hardcoding the full path, so a
# RID-suffixed pivot (debug_win-x64) or a future TFM segment can't leave us
# launching a stale binary. LastWriteTime sort picks the freshest.
$AppArtifactsBin = Join-Path $RepoRoot 'artifacts\bin\Deckle.App'
$PivotPrefix     = $Configuration.ToLowerInvariant()

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

# Resolve the freshest Deckle.exe under artifacts\bin\Deckle.App\<pivot>\.
# Pivot prefix match (debug / debug_win-x64) rather than an exact folder so a
# RID-suffixed pivot can't strand us on a stale exe. LastWriteTime sort picks
# the freshest if several pivots coexist.
$ExeCandidates = Get-ChildItem -Path $AppArtifactsBin -Recurse -Filter 'Deckle.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -like "$PivotPrefix*" } |
    Sort-Object LastWriteTime -Descending
if (-not $ExeCandidates) { throw "Exe not found under $AppArtifactsBin (expected artifacts\bin\Deckle.App\$PivotPrefix\Deckle.exe)" }
$ExePath = $ExeCandidates[0].FullName
Write-Host "Run $ExePath" -ForegroundColor Green

function Start-DeckleViaShell {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [string[]]$DeckleArgs = @()
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FilePath
    $psi.UseShellExecute = $true
    $psi.WorkingDirectory = Split-Path -Parent $FilePath
    $psi.Arguments = ($DeckleArgs -join ' ')
    [System.Diagnostics.Process]::Start($psi) | Out-Null
}

# Launch via real ShellExecute so the new Deckle process is detached from
# PowerShell and enters the same shell path as a user-opened executable.
# The earlier `cmd /c start` route went through a console shell first; that
# looked close but still left Deckle under the timing/foreground state of
# the build script in the HUD topmost repro.
#
# Post-build mitigation: the first launch right after the build can still
# inherit a degraded foreground state, so we pass --post-build to Deckle.exe
# and let the app relaunch itself once via the same ShellExecute primitive.
# Pass -NoAutoRestart to suppress (debug-attach scenarios).
if ($HudZOrderSelfTest) {
    Start-DeckleViaShell -FilePath $ExePath -DeckleArgs @('--post-build-hud-zorder-selftest')
} elseif ($NoAutoRestart) {
    Start-DeckleViaShell -FilePath $ExePath
} else {
    Start-DeckleViaShell -FilePath $ExePath -DeckleArgs @('--post-build')
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
