# deckle.ps1 — Single interactive entry point for Deckle dev workflows.
#
# Run this with F5 in VSCodium (see .vscode/launch.json) or directly from a
# PowerShell 7+ terminal. The top level is a 2-D grid (↑↓←→ to move, Enter to
# run): the verbs you reach for most sit up top, each with its Release/Debug
# variant beside it, so one Enter picks both. The menu loops — an action runs,
# then you land back on it — until Quit or Esc at the top level.
#
# The worktree is asked AFTER you pick the action, for the actions that act on
# one (Launch, Build, Update version, Maintenance, app Release). It is the
# point of the menu, and it auto-resolves when only one worktree exists. Global
# actions (MCP, native runtime, Setup) never ask for a worktree.
#
# Every concrete action delegates to a single-purpose script in scripts/lib/;
# those scripts remain usable on their own CLI for automation.
#
# Colour semantics: default foreground = neutral, DarkGray = secondary/hint,
# Cyan = step title, Green = success, Yellow = a real warning (public publish),
# Red = error.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir    = Join-Path $ScriptDir 'lib'

Import-Module (Join-Path $LibDir '_menu.psm1') -Force

# ── Small input helpers ──────────────────────────────────────────────────────

# Pick a worktree, or return $null on Esc (Select-Worktree throws "Cancelled").
# Auto-resolves silently when there is only one worktree.
function Get-WorktreeOrReturn {
    try {
        $wt = Select-Worktree -ContextDir $ScriptDir
        Write-Host "Worktree: $wt" -ForegroundColor DarkGray
        return $wt
    } catch {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return $null
    }
}

# Short y/n prompt. Returns $true/$false; default applies on bare Enter. The
# question prints in the default foreground (neutral) — never coloured.
function Read-YesNo {
    param(
        [Parameter(Mandatory)][string]$Question,
        [bool]$Default = $false
    )
    $hint = if ($Default) { '[Y/n]' } else { '[y/N]' }
    $ans  = Read-Host "$Question $hint"
    if ([string]::IsNullOrWhiteSpace($ans)) { return $Default }
    return ($ans -match '^(y|yes|o|oui)$')
}

function Read-Optional {
    param([Parameter(Mandatory)][string]$Question)
    $answer = Read-Host $Question
    if ([string]::IsNullOrWhiteSpace($answer)) { return $null }
    return $answer.Trim()
}

# Read <Version> from a worktree's csproj. Returns the string or $null.
function Get-CsprojVersion {
    param([Parameter(Mandatory)][string]$Worktree)
    $csproj = Join-Path $Worktree 'src\Deckle.App\Deckle.App.csproj'
    $m = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($m) { return $m.Matches[0].Groups[1].Value.Trim() }
    return $null
}

# Show a submenu with the same 2-D grid renderer as the top-level menu. Returns
# the chosen Value, or $null when the user goes Back or presses Esc.
function Show-Submenu {
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Rows,
        [string]$Footer = 'same controls as the main menu; Esc also goes back'
    )

    $withBack = @($Rows) + @(
        @{ Blank = $true }
        @{ Title = 'Back' }
        @{ Cells = @( @{ Label = '< Back'; Value = '__back__' } ) }
    )

    $v = Select-Grid -Header $Header -Rows $withBack -Footer $Footer
    if ($null -eq $v -or $v -eq '__back__') { return $null }
    return $v
}

# ── Action handlers (each bails with `return`, leaving the menu loop intact) ──

# Launch / Build: verb and configuration are already chosen in the grid; only
# the worktree is asked, last.
function Invoke-LaunchOrBuild {
    param(
        [Parameter(Mandatory)][ValidateSet('launch', 'run', 'norun')][string]$Kind,
        [Parameter(Mandatory)][ValidateSet('Release', 'Debug')][string]$Configuration
    )
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    switch ($Kind) {
        'launch' { & (Join-Path $LibDir 'launch-app.ps1') -Target $wt -Configuration $Configuration }
        'run'    { & (Join-Path $LibDir 'build-run.ps1')  -Target $wt -Configuration $Configuration }
        'norun'  { & (Join-Path $LibDir 'build-run.ps1')  -Target $wt -Configuration $Configuration -NoRun }
    }
}

# Run a per-worktree maintenance script with -Target.
function Invoke-WorktreeScript {
    param([Parameter(Mandatory)][string]$Script)
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    & (Join-Path $LibDir $Script) -Target $wt
}

# Update version: worktree → pick the increment (each option shows the
# resulting number) → confirm. The tag is mechanics, kept to one grey line.
function Invoke-UpdateVersion {
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    $cur = Get-CsprojVersion -Worktree $wt
    if (-not $cur -or $cur -notmatch '^\d+\.\d+\.\d+$') {
        Write-Host "No MAJOR.MINOR.PATCH <Version> found in that worktree." -ForegroundColor Red
        return
    }
    $n = $cur.Split('.') | ForEach-Object { [int]$_ }
    $patch = "$($n[0]).$($n[1]).$($n[2] + 1)"
    $minor = "$($n[0]).$($n[1] + 1).0"
    $major = "$($n[0] + 1).0.0"
    $items = @(
        [pscustomobject]@{ Label = ("{0,-7}{1,-18}{2}" -f 'Patch', "$cur -> $patch", 'a fix or small step'); Value = [pscustomobject]@{ Seg = 'patch'; Target = $patch } }
        [pscustomobject]@{ Label = ("{0,-7}{1,-18}{2}" -f 'Minor', "$cur -> $minor", 'a real cycle');        Value = [pscustomobject]@{ Seg = 'minor'; Target = $minor } }
        [pscustomobject]@{ Label = ("{0,-7}{1,-18}{2}" -f 'Major', "$cur -> $major", 'an overhaul');          Value = [pscustomobject]@{ Seg = 'major'; Target = $major } }
    )
    try {
        $choice = Select-Action -Header 'Update version - pick the increment:' -Items $items -Default 0
    } catch {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return
    }
    Write-Host ""
    Write-Host "Recorded on this worktree and tagged locally. Nothing is pushed." -ForegroundColor DarkGray
    if (-not (Read-YesNo -Question "Set Deckle to v$($choice.Target)?" -Default $true)) {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return
    }
    & (Join-Path $LibDir 'cut-version.ps1') -Target $wt -Bump $choice.Seg
}

# Publish a PUBLIC GitHub Release — maintainer's act, behind a y/N gate. The
# warning stays Yellow: it is a genuine, outward-facing, hard-to-undo act.
function Invoke-PublishRelease {
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    $ver = Get-CsprojVersion -Worktree $wt
    if (-not $ver) {
        Write-Host "Could not read <Version> from that worktree." -ForegroundColor Red
        return
    }
    Write-Host "This publishes a PUBLIC GitHub Release v$ver (creates tag v$ver, uploads the installer exe + app ZIP + sha256)." -ForegroundColor Yellow
    if (-not (Read-YesNo -Question "Publish Deckle v$ver to GitHub now?" -Default $false)) {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return
    }
    & (Join-Path $LibDir 'publish-app.ps1') -Target $wt -Publish
}

# Prepare release artefacts locally, no GitHub publish.
function Invoke-PrepareArtifacts {
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    & (Join-Path $LibDir 'publish-app.ps1') -Target $wt
}

# Build (and optionally publish) the native runtime bundle. Global action.
function Invoke-NativeRuntime {
    $version = Read-Optional -Question 'Native bundle version (X.Y.Z)'
    if (-not $version) { Write-Host "Cancelled: version is required." -ForegroundColor DarkGray; return }
    $whisperRepo = Read-Optional -Question 'Path to whisper.cpp clone with build/bin'
    if (-not $whisperRepo) { Write-Host "Cancelled: whisper.cpp path is required." -ForegroundColor DarkGray; return }
    $outDir  = Read-Optional -Question 'Output directory (blank = temp)'
    $publish = Read-YesNo -Question 'Publish native runtime GitHub Release after building?' -Default $false
    if ($publish) {
        Write-Host "This publishes a PUBLIC GitHub Release native-v$version via gh." -ForegroundColor Yellow
        if (-not (Read-YesNo -Question "Publish native-v$version now?" -Default $false)) {
            Write-Host "Cancelled." -ForegroundColor DarkGray
            return
        }
    }
    $nativeArgs = @{ Version = $version; WhisperRepo = $whisperRepo }
    if ($outDir)  { $nativeArgs.OutDir = $outDir }
    if ($publish) { $nativeArgs.Publish = $true }
    & (Join-Path $LibDir 'publish-native-runtime.ps1') @nativeArgs
}

# Publish / update the Anytype MCP host. Maintainer's act, behind a y/N gate.
function Invoke-AnytypeMcp {
    Write-Host "Publishes the Anytype MCP to %LOCALAPPDATA%\Deckle\mcp\anytype\ (versioned + 'current' junction) and points .claude.json at current\ - AI clients stop locking the build output." -ForegroundColor DarkGray
    Write-Host "Safe to re-run to cut a new version: open sessions keep theirs, new spawns get the fresh one." -ForegroundColor DarkGray
    if (-not (Read-YesNo -Question 'Install / update the Anytype MCP now?' -Default $false)) {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return
    }
    & (Join-Path $LibDir 'install-anytype-mcp.ps1')
}

# Bootstrap a fresh dev machine. Global action.
function Invoke-BootstrapDev {
    $dryRun = Read-YesNo -Question 'Dry-run first (probe + plan, no install)?' -Default $true
    $full   = Read-YesNo -Question 'Include Tier 2 (native recompile toolchain + Ollama)?' -Default $false
    $bootstrapArgs = @{}
    if ($dryRun) { $bootstrapArgs.DryRun = $true }
    if ($full)   { $bootstrapArgs.Full = $true }
    & (Join-Path $LibDir 'bootstrap-dev-env.ps1') @bootstrapArgs
}

# Provision runtime assets (native runtime + Whisper models). Global action.
function Invoke-SetupAssets {
    Write-Host "This may download native runtime and Whisper model files." -ForegroundColor DarkGray
    if (-not (Read-YesNo -Question 'Continue with runtime asset setup?' -Default $false)) {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return
    }
    $assetArgs = @{}
    $fromRelease = Read-Optional -Question 'Native runtime release version X.Y.Z (blank = local/sibling source or skip)'
    if ($fromRelease) { $assetArgs.FromRelease = $fromRelease }
    if (Read-YesNo -Question 'Download ggml-large-v3.bin (~3 GB)?' -Default $false) { $assetArgs.WithLarge = $true }
    if (Read-YesNo -Question 'Force re-copy / re-download existing files?' -Default $false) { $assetArgs.Force = $true }
    & (Join-Path $LibDir 'setup-assets.ps1') @assetArgs
}

# ── Submenu routers (same grid style, reached from the top-level "More" row) ─

function Show-ReleaseMenu {
    $v = Show-Submenu -Header 'Deckle > Release   -   ↑↓←→ move   Enter run   Esc back' -Rows @(
        @{ Title = 'App release' }
        @{ Prefix = 'App'; Cells = @(
            @{ Label = 'Publish app release';           Value = 'publish'   }
            @{ Label = 'Prepare app release artifacts'; Value = 'artifacts' }
        ) }
        @{ Blank = $true }
        @{ Title = 'Native runtime' }
        @{ Cells = @(
            @{ Label = 'Prepare native runtime release'; Value = 'native' }
        ) }
    )
    switch ($v) {
        'publish'   { Invoke-PublishRelease }
        'artifacts' { Invoke-PrepareArtifacts }
        'native'    { Invoke-NativeRuntime }
    }
}

function Show-MaintenanceMenu {
    $v = Show-Submenu -Header 'Deckle > Maintenance   -   ↑↓←→ move   Enter run   Esc back' -Rows @(
        @{ Title = 'Worktree' }
        @{ Cells = @(
            @{ Label = 'Clean build outputs'; Value = 'clean' }
            @{ Label = 'Show module stats';   Value = 'stats' }
        ) }
        @{ Blank = $true }
        @{ Title = 'Generated docs' }
        @{ Cells = @(
            @{ Label = 'Update README pulse'; Value = 'readme-stats' }
            @{ Label = 'Update changelog';    Value = 'changelog'    }
        ) }
    )
    switch ($v) {
        'clean'        { Invoke-WorktreeScript -Script 'clean.ps1' }
        'stats'        { Invoke-WorktreeScript -Script 'stats.ps1' }
        'readme-stats' { Invoke-WorktreeScript -Script 'update-readme-stats.ps1' }
        'changelog'    { Invoke-WorktreeScript -Script 'changelog.ps1' }
    }
}

function Show-SetupMenu {
    $v = Show-Submenu -Header 'Deckle > Setup   -   ↑↓←→ move   Enter run   Esc back' -Rows @(
        @{ Title = 'Machine' }
        @{ Cells = @(
            @{ Label = 'Bootstrap dev environment'; Value = 'bootstrap' }
            @{ Label = 'Set up runtime assets';     Value = 'assets'    }
        ) }
        @{ Blank = $true }
        @{ Title = 'Repository' }
        @{ Cells = @(
            @{ Label = 'Install git hooks'; Value = 'hooks' }
        ) }
    )
    switch ($v) {
        'bootstrap' { Invoke-BootstrapDev }
        'assets'    { Invoke-SetupAssets }
        'hooks'     { & (Join-Path $LibDir 'install-hooks.ps1') }
    }
}

# ── Top-level grid loop ──────────────────────────────────────────────────────
# Launch/Build rows carry their config in the Value (e.g. 'run:Release'); the
# rest carry a plain action token. The cursor starts on Build & run / Release.

$mainRows = @(
    @{ Title  = 'Run' }
    @{ Prefix = 'Launch';         Cells = @( @{ Label = 'Release'; Value = 'launch:Release' }, @{ Label = 'Debug'; Value = 'launch:Debug' } ) }
    @{ Prefix = 'Build & run';    Cells = @( @{ Label = 'Release'; Value = 'run:Release' },    @{ Label = 'Debug'; Value = 'run:Debug' } ) }
    @{ Prefix = 'Build (no run)'; Cells = @( @{ Label = 'Release'; Value = 'norun:Release' },  @{ Label = 'Debug'; Value = 'norun:Debug' } ) }
    @{ Blank  = $true }
    @{ Title  = 'Project' }
    @{ Cells  = @( @{ Label = 'Update version'; Value = 'update-version' }, @{ Label = 'Anytype MCP'; Value = 'mcp' } ) }
    @{ Blank  = $true }
    @{ Title  = 'More' }
    @{ Cells  = @( @{ Label = 'Release…'; Value = 'release-menu' }, @{ Label = 'Maintenance…'; Value = 'maintenance-menu' }, @{ Label = 'Setup…'; Value = 'setup-menu' }, @{ Label = 'Quit'; Value = 'quit' } ) }
)

while ($true) {
    $v = Select-Grid `
        -Header 'Deckle   -   ↑↓←→ move   Enter run   Esc quit' `
        -Footer 'the worktree is asked after you pick (skipped when there is only one)' `
        -Rows $mainRows -StartSel 1 -StartCol 0
    if ($null -eq $v -or $v -eq 'quit') { break }

    if ($v -match '^(launch|run|norun):(Release|Debug)$') {
        Invoke-LaunchOrBuild -Kind $Matches[1] -Configuration $Matches[2]
    } else {
        switch ($v) {
            'update-version'   { Invoke-UpdateVersion }
            'mcp'              { Invoke-AnytypeMcp }
            'release-menu'     { Show-ReleaseMenu }
            'maintenance-menu' { Show-MaintenanceMenu }
            'setup-menu'       { Show-SetupMenu }
        }
    }
}

Write-Host "Bye." -ForegroundColor DarkGray
