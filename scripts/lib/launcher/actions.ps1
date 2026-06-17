# Concrete Deckle launcher action handlers.
function Invoke-LaunchOrBuild {
    param(
        [Parameter(Mandatory)][ValidateSet('launch', 'run', 'norun')][string]$Kind,
        [Parameter(Mandatory)][ValidateSet('Release', 'Debug')][string]$Configuration
    )
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    Begin-DeckleAction
    switch ($Kind) {
        'launch' { & (Join-Path $LibDir 'launch-app.ps1') -Target $wt -Configuration $Configuration }
        'run'    { & (Join-Path $LibDir 'build-run.ps1')  -Target $wt -Configuration $Configuration }
        'norun'  { & (Join-Path $LibDir 'build-run.ps1')  -Target $wt -Configuration $Configuration -NoRun }
    }
}

function Invoke-WorktreeScript {
    param([Parameter(Mandatory)][string]$Script)
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    Begin-DeckleAction
    & (Join-Path $LibDir $Script) -Target $wt
}

function Invoke-StopBuildServers {
    Begin-DeckleAction
    & (Join-Path $LibDir 'stop-build-servers.ps1')
}

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
        $choice = Select-Action -Header 'Update version - pick the increment:' -Items $items -Default 0 -ClearScreen
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
    Begin-DeckleAction
    & (Join-Path $LibDir 'cut-version.ps1') -Target $wt -Bump $choice.Seg
}

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
    Begin-DeckleAction
    & (Join-Path $LibDir 'publish-app.ps1') -Target $wt -Publish
}

function Invoke-PrepareArtifacts {
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    Begin-DeckleAction
    & (Join-Path $LibDir 'publish-app.ps1') -Target $wt
}

function Invoke-NativeRuntime {
    Clear-DeckleMenuScreen
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
    Begin-DeckleAction
    & (Join-Path $LibDir 'publish-native-runtime.ps1') @nativeArgs
}

function Invoke-AnytypeMcp {
    Clear-DeckleMenuScreen
    Write-Host "Publishes the Anytype MCP to %LOCALAPPDATA%\Deckle\mcp\anytype\ (versioned + 'current' junction) and points .claude.json at current\ - AI clients stop locking the build output." -ForegroundColor DarkGray
    Write-Host "Safe to re-run to cut a new version: open sessions keep theirs, new spawns get the fresh one." -ForegroundColor DarkGray
    if (-not (Read-YesNo -Question 'Install / update the Anytype MCP now?' -Default $false)) {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return
    }
    Write-Host "Supervised management tools (complete / archive / delete) are destructive verbs, served only to this consumer. Off by default." -ForegroundColor DarkGray
    $mgmtState = if (Read-YesNo -Question 'Mount the supervised management tools in .claude.json?' -Default $false) { 'on' } else { 'off' }
    Begin-DeckleAction
    & (Join-Path $LibDir 'install-anytype-mcp.ps1') -Management $mgmtState
}

function Invoke-BootstrapDev {
    Clear-DeckleMenuScreen
    $dryRun = Read-YesNo -Question 'Dry-run first (probe + plan, no install)?' -Default $true
    $full   = Read-YesNo -Question 'Include Tier 2 (native recompile toolchain + Ollama)?' -Default $false
    $bootstrapArgs = @{}
    if ($dryRun) { $bootstrapArgs.DryRun = $true }
    if ($full)   { $bootstrapArgs.Full = $true }
    Begin-DeckleAction
    & (Join-Path $LibDir 'bootstrap-dev-env.ps1') @bootstrapArgs
}

function Invoke-SetupAssets {
    Clear-DeckleMenuScreen
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
    Begin-DeckleAction
    & (Join-Path $LibDir 'setup-assets.ps1') @assetArgs
}
