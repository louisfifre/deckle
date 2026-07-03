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

# The patch/minor/major picker with next-version previews, shared by the
# standalone bump and the publish flow. Returns @{ Seg; Target } or $null on Esc.
function Select-VersionBump {
    param(
        [Parameter(Mandatory)][string]$Current,
        [string]$Header = 'Pick the increment:'
    )
    $n = $Current.Split('.') | ForEach-Object { [int]$_ }
    $patch = "$($n[0]).$($n[1]).$($n[2] + 1)"
    $minor = "$($n[0]).$($n[1] + 1).0"
    $major = "$($n[0] + 1).0.0"
    $items = @(
        [pscustomobject]@{ Label = ("{0,-7}{1,-18}{2}" -f 'Patch', "$Current -> $patch", 'a fix or small step'); Value = [pscustomobject]@{ Seg = 'patch'; Target = $patch } }
        [pscustomobject]@{ Label = ("{0,-7}{1,-18}{2}" -f 'Minor', "$Current -> $minor", 'a real cycle');        Value = [pscustomobject]@{ Seg = 'minor'; Target = $minor } }
        [pscustomobject]@{ Label = ("{0,-7}{1,-18}{2}" -f 'Major', "$Current -> $major", 'an overhaul');          Value = [pscustomobject]@{ Seg = 'major'; Target = $major } }
    )
    try {
        return Select-Action -Header $Header -Items $items -Default 0 -ClearScreen
    } catch {
        return $null
    }
}

function Test-ChangelogVersionSection {
    param(
        [Parameter(Mandatory)][string]$Worktree,
        [Parameter(Mandatory)][string]$Version
    )
    $path = Join-Path $Worktree 'CHANGELOG.md'
    if (-not (Test-Path $path)) { return $false }
    $content = Get-Content -Raw -LiteralPath $path
    $escaped = [regex]::Escape($Version)
    return $content -match "(?m)^## \[$escaped\]"
}

function Get-RecordableCommitCountSinceTag {
    param(
        [Parameter(Mandatory)][string]$Worktree,
        [Parameter(Mandatory)][string]$Tag
    )
    $subjects = & git -C $Worktree log --format='%s' "$Tag..HEAD"
    if ($LASTEXITCODE -ne 0) { throw "git log $Tag..HEAD failed (code $LASTEXITCODE)" }
    $count = 0
    foreach ($subject in $subjects) {
        if ($subject -cmatch '^(feat|fix|perf|refactor|revert)(?:\([^)]+\))?!?:\s+') { $count++ }
    }
    return $count
}

function Invoke-RecordVersion {
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    $cur = Get-CsprojVersion -Worktree $wt
    if (-not $cur -or $cur -notmatch '^\d+\.\d+\.\d+$') {
        Write-Host "No MAJOR.MINOR.PATCH <Version> found in that worktree." -ForegroundColor Red
        return
    }

    $tag = "v$cur"
    $alreadyCut = [bool](& git -C $wt tag --list $tag)
    $alreadyBaked = Test-ChangelogVersionSection -Worktree $wt -Version $cur
    $recordableSinceTag = if ($alreadyCut) { Get-RecordableCommitCountSinceTag -Worktree $wt -Tag $tag } else { 0 }
    $recordArgs = @{ Target = $wt; Push = $true }
    $target = $cur

    if ($alreadyCut -and $recordableSinceTag -gt 0) {
        Write-Host "$recordableSinceTag user-facing commit(s) exist after $tag." -ForegroundColor DarkGray
        $choice = Select-VersionBump -Current $cur -Header 'Record version - changes after current tag, pick the increment:'
        if ($null -eq $choice) {
            Write-Host "Cancelled." -ForegroundColor DarkGray
            return
        }
        $recordArgs.Bump = $choice.Seg
        $target = $choice.Target
    } elseif ($alreadyCut -and -not $alreadyBaked) {
        $recordArgs.Current = $true
        Write-Host "v$cur is already cut but not baked into CHANGELOG.md." -ForegroundColor DarkGray
    } else {
        $choice = Select-VersionBump -Current $cur -Header 'Record version - pick the increment:'
        if ($null -eq $choice) {
            Write-Host "Cancelled." -ForegroundColor DarkGray
            return
        }
        $recordArgs.Bump = $choice.Seg
        $target = $choice.Target
    }

    Write-Host ""
    Write-Host "This records v$target in git and CHANGELOG.md, pushes the branch + tag, and creates NO GitHub Release." -ForegroundColor Yellow
    if (-not (Read-YesNo -Question "Record Deckle v$target now?" -Default $true)) {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return
    }

    Begin-DeckleAction
    & (Join-Path $LibDir 'record-version.ps1') @recordArgs
}

# The whole release ritual behind one consent: bump (asked here, skipped when the
# current <Version> already has its tag — a cut made via Record version, or a
# previous run that failed past the cut), record-version bake/push, then the
# public GitHub Release. Each step stays a single-purpose script; this action
# only composes them, and every git step is idempotent so a re-run resumes where
# the last one stopped.
function Invoke-PublishRelease {
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    $cur = Get-CsprojVersion -Worktree $wt
    if (-not $cur -or $cur -notmatch '^\d+\.\d+\.\d+$') {
        Write-Host "No MAJOR.MINOR.PATCH <Version> found in that worktree." -ForegroundColor Red
        return
    }

    $tag = "v$cur"
    $alreadyCut = [bool](& git -C $wt tag --list $tag)
    $recordableSinceTag = if ($alreadyCut) { Get-RecordableCommitCountSinceTag -Worktree $wt -Tag $tag } else { 0 }
    $recordArgs = @{ Target = $wt; Push = $true }
    $target = $cur
    if ($alreadyCut -and $recordableSinceTag -gt 0) {
        Write-Host "$recordableSinceTag user-facing commit(s) exist after $tag - this run records a new version before publishing." -ForegroundColor DarkGray
        $choice = Select-VersionBump -Current $cur -Header 'Publish release - changes after current tag, pick the increment:'
        if ($null -eq $choice) {
            Write-Host "Cancelled." -ForegroundColor DarkGray
            return
        }
        $recordArgs.Bump = $choice.Seg
        $target = $choice.Target
        Write-Host ""
    } elseif ($alreadyCut) {
        Write-Host "$tag is already cut and no user-facing commit exists after it - this run publishes it." -ForegroundColor DarkGray
        $recordArgs.Current = $true
    } else {
        $choice = Select-VersionBump -Current $cur -Header 'Publish release - pick the increment:'
        if ($null -eq $choice) {
            Write-Host "Cancelled." -ForegroundColor DarkGray
            return
        }
        $recordArgs.Bump = $choice.Seg
        $target = $choice.Target
        Write-Host ""
    }

    Write-Host "This records v$target, pushes the branch + tag, and publishes a PUBLIC GitHub Release (installer exe + app ZIP + sha256)." -ForegroundColor Yellow
    if (-not (Read-YesNo -Question "Publish Deckle v$target to GitHub now?" -Default $true)) {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return
    }

    Begin-DeckleAction
    & (Join-Path $LibDir 'record-version.ps1') @recordArgs
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
