# deckle.ps1 — Single interactive entry point for Deckle dev workflows.
#
# Run this with F5 in VSCodium (see .vscode/launch.json) or directly from a
# PowerShell 7+ terminal. The menu is a small router: a short top level of
# verbs, where rare groups collapse into submenus reached with `▸` and left
# with Back/Esc. It loops — an action runs, then you land back on the menu —
# until you pick Quit (or press Esc at the top level).
#
#   Launch / Build    — pick the verb, then the worktree, then Release/Debug.
#   Update version    — bump the csproj <Version> and tag it (frequent).
#   Release ▸         — publish artefacts / GitHub releases (rare).
#   MCP ▸             — publish the Anytype MCP host.
#   Maintenance ▸     — clean, stats, docs.
#   Setup ▸           — bootstrap a fresh machine, hooks, runtime assets.
#
# Per-worktree actions prompt for a worktree after the action is picked
# (auto-resolves when only the main repo exists). Every concrete action
# delegates to a single-purpose script in scripts/lib/; those scripts remain
# usable on their own CLI for automation.
#
# Colour semantics (consistent across the menu): default foreground = neutral
# prompt/info, DarkGray = secondary/hint, Cyan = step title, Green = success,
# Yellow = a real warning (public publish, destructive), Red = error.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir    = Join-Path $ScriptDir 'lib'

Import-Module (Join-Path $LibDir '_menu.psm1') -Force

# ── Small input helpers ──────────────────────────────────────────────────────

# Pick a worktree, or return $null on Esc (Select-Worktree throws "Cancelled").
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

# Pick Release or Debug (Release default). Returns the string, or $null on Esc.
function Get-Configuration {
    try {
        return Select-Action -Header 'Configuration:' -Items @(
            [pscustomobject]@{ Label = 'Release'; Value = 'Release' }
            [pscustomobject]@{ Label = 'Debug';   Value = 'Debug'   }
        ) -Default 0
    } catch {
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

# Show a submenu: the given items plus a Back entry. Returns the chosen Value,
# or $null when the user goes Back or presses Esc.
function Show-Submenu {
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Items
    )
    $withBack = @($Items) + @(
        [pscustomobject]@{ Label = ''; Value = $null; IsHeader = $true }
        [pscustomobject]@{ Label = '< Back'; Value = '__back__' }
    )
    try {
        $v = Select-Action -Header $Header -Items $withBack
    } catch {
        return $null   # Esc = back
    }
    if ($v -eq '__back__') { return $null }
    return $v
}

# ── Action handlers (each bails with `return`, leaving the menu loop intact) ──

# Launch / Build: verb → worktree → Release/Debug, then delegate.
function Invoke-LaunchOrBuild {
    param([Parameter(Mandatory)][ValidateSet('launch', 'run', 'norun')][string]$Kind)
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    Write-Host ""
    $cfg = Get-Configuration
    if ($null -eq $cfg) { Write-Host "Cancelled." -ForegroundColor DarkGray; return }
    switch ($Kind) {
        'launch' { & (Join-Path $LibDir 'launch-app.ps1') -Target $wt -Configuration $cfg }
        'run'    { & (Join-Path $LibDir 'build-run.ps1')  -Target $wt -Configuration $cfg }
        'norun'  { & (Join-Path $LibDir 'build-run.ps1')  -Target $wt -Configuration $cfg -NoRun }
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
# warning stays Yellow here: it is a genuine, outward-facing, hard-to-undo act.
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

# ── Submenu routers ──────────────────────────────────────────────────────────

function Show-ReleaseMenu {
    $v = Show-Submenu -Header 'Release:' -Items @(
        [pscustomobject]@{ Label = 'Publish app release';            Value = 'publish'   }
        [pscustomobject]@{ Label = 'Prepare app release artifacts';  Value = 'artifacts' }
        [pscustomobject]@{ Label = 'Prepare native runtime release'; Value = 'native'    }
    )
    switch ($v) {
        'publish'   { Invoke-PublishRelease }
        'artifacts' { Invoke-PrepareArtifacts }
        'native'    { Invoke-NativeRuntime }
    }
}

function Show-McpMenu {
    $v = Show-Submenu -Header 'MCP:' -Items @(
        [pscustomobject]@{ Label = 'Install / update Anytype MCP'; Value = 'anytype' }
    )
    switch ($v) {
        'anytype' { Invoke-AnytypeMcp }
    }
}

function Show-MaintenanceMenu {
    $v = Show-Submenu -Header 'Maintenance:' -Items @(
        [pscustomobject]@{ Label = 'Clean build outputs'; Value = 'clean'        }
        [pscustomobject]@{ Label = 'Show module stats';   Value = 'stats'        }
        [pscustomobject]@{ Label = 'Update README pulse'; Value = 'readme-stats' }
        [pscustomobject]@{ Label = 'Update changelog';    Value = 'changelog'    }
    )
    switch ($v) {
        'clean'        { Invoke-WorktreeScript -Script 'clean.ps1' }
        'stats'        { Invoke-WorktreeScript -Script 'stats.ps1' }
        'readme-stats' { Invoke-WorktreeScript -Script 'update-readme-stats.ps1' }
        'changelog'    { Invoke-WorktreeScript -Script 'changelog.ps1' }
    }
}

function Show-SetupMenu {
    $v = Show-Submenu -Header 'Setup:' -Items @(
        [pscustomobject]@{ Label = 'Bootstrap dev environment'; Value = 'bootstrap' }
        [pscustomobject]@{ Label = 'Set up runtime assets';     Value = 'assets'    }
        [pscustomobject]@{ Label = 'Install git hooks';         Value = 'hooks'     }
    )
    switch ($v) {
        'bootstrap' { Invoke-BootstrapDev }
        'assets'    { Invoke-SetupAssets }
        'hooks'     { & (Join-Path $LibDir 'install-hooks.ps1') }
    }
}

# ── Top-level menu loop ──────────────────────────────────────────────────────

$topActions = @(
    [pscustomobject]@{ Label = 'Launch';         Value = 'launch'         }
    [pscustomobject]@{ Label = 'Build & run';    Value = 'build-run'      }
    [pscustomobject]@{ Label = 'Build (no run)'; Value = 'build-norun'    }
    [pscustomobject]@{ Label = '──────────';     Value = $null; IsHeader = $true }
    [pscustomobject]@{ Label = 'Update version'; Value = 'update-version' }
    [pscustomobject]@{ Label = 'Release  ▸';     Value = 'release-menu'   }
    [pscustomobject]@{ Label = '──────────';     Value = $null; IsHeader = $true }
    [pscustomobject]@{ Label = 'MCP  ▸';         Value = 'mcp-menu'       }
    [pscustomobject]@{ Label = 'Maintenance  ▸'; Value = 'maintenance-menu' }
    [pscustomobject]@{ Label = 'Setup  ▸';       Value = 'setup-menu'     }
    [pscustomobject]@{ Label = '──────────';     Value = $null; IsHeader = $true }
    [pscustomobject]@{ Label = 'Quit';           Value = 'quit'           }
)

while ($true) {
    try {
        $action = Select-Action -Header 'Deckle - pick an action (Up/Down, Enter, Esc = quit):' -Items $topActions
    } catch {
        break   # Esc at the top level = quit
    }
    if ($action -eq 'quit') { break }
    switch ($action) {
        'launch'           { Invoke-LaunchOrBuild -Kind 'launch' }
        'build-run'        { Invoke-LaunchOrBuild -Kind 'run' }
        'build-norun'      { Invoke-LaunchOrBuild -Kind 'norun' }
        'update-version'   { Invoke-UpdateVersion }
        'release-menu'     { Show-ReleaseMenu }
        'mcp-menu'         { Show-McpMenu }
        'maintenance-menu' { Show-MaintenanceMenu }
        'setup-menu'       { Show-SetupMenu }
    }
}

Write-Host "Bye." -ForegroundColor DarkGray
