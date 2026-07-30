# Concrete Deckle launcher action handlers.
function Invoke-LaunchOrBuild {
    param(
        [Parameter(Mandatory)][ValidateSet('launch', 'run', 'norun')][string]$Kind,
        [Parameter(Mandatory)][ValidateSet('Release', 'Debug')][string]$Configuration,
        [Parameter(Mandatory)][object[]]$MenuRows
    )
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }

    $label = switch ($Kind) {
        'launch' { "Launch $Configuration" }
        'run'    { "Build & run $Configuration" }
        'norun'  { "Build $Configuration" }
    }
    $source = if ($Kind -eq 'launch') { 'Launch' } else { 'Build' }
    $actionParameters = @{ Target = $wt; Configuration = $Configuration }
    $scriptPath = if ($Kind -eq 'launch') {
        Join-Path $LibDir 'launch-app.ps1'
    } else {
        if ($Kind -eq 'norun') { $actionParameters.NoRun = $true }
        Join-Path $LibDir 'build-run.ps1'
    }

    return Invoke-DeckleMenuAction `
        -Header "Deckle > $label" `
        -Label $label `
        -Source $source `
        -MenuRows $MenuRows `
        -Action { & $scriptPath @actionParameters }
}

function Invoke-WorktreeScript {
    param(
        [Parameter(Mandatory)][string]$Script,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][object[]]$MenuRows
    )
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    $scriptPath = Join-Path $LibDir $Script
    return Invoke-DeckleMenuAction -Header "Deckle > $Label" -Label $Label -Source $Source -MenuRows $MenuRows -Action {
        & $scriptPath -Target $wt
    }
}

function Invoke-CleanBuildOutputs {
    param([Parameter(Mandatory)][object[]]$MenuRows)
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    $worktreeName = Split-Path -Leaf $wt
    $confirmed = Read-YesNo `
        -Question "Delete generated build outputs from $worktreeName?" `
        -Default $false `
        -ConfirmLabel 'Delete outputs' `
        -CancelLabel 'Keep files' `
        -Destructive
    if (-not $confirmed) { return }
    $scriptPath = Join-Path $LibDir 'clean.ps1'
    return Invoke-DeckleMenuAction -Header 'Deckle > Maintenance > Clean' -Label 'Clean build outputs' -Source Clean -MenuRows $MenuRows -Action {
        & $scriptPath -Target $wt
    }
}

function Invoke-StopBuildServers {
    param([Parameter(Mandatory)][object[]]$MenuRows)
    $scriptPath = Join-Path $LibDir 'stop-build-servers.ps1'
    return Invoke-DeckleMenuAction -Header 'Deckle > Maintenance > Build servers' -Label 'Stop build servers' -Source Clean -MenuRows $MenuRows -Action {
        & $scriptPath
    }
}

# The patch/minor/major picker with next-version previews, shared by the
# standalone bump and the publish flow. Returns @{ Seg; Target } or $null on Esc.
function Select-VersionBump {
    param(
        [Parameter(Mandatory)][string]$Current,
        [string]$Header = 'Deckle > Version   -   ↑↓ move   Enter select   Esc back'
    )
    $n = $Current.Split('.') | ForEach-Object { [int]$_ }
    $patch = "$($n[0]).$($n[1]).$($n[2] + 1)"
    $minor = "$($n[0]).$($n[1] + 1).0"
    $major = "$($n[0] + 1).0.0"
    $items = @(
        [pscustomobject]@{ Label = 'Increment'; IsHeader = $true }
        [pscustomobject]@{ Prefix = 'Patch'; Label = "$Current → $patch   a fix or small step"; Value = [pscustomobject]@{ Seg = 'patch'; Target = $patch } }
        [pscustomobject]@{ Prefix = 'Minor'; Label = "$Current → $minor   a real cycle";        Value = [pscustomobject]@{ Seg = 'minor'; Target = $minor } }
        [pscustomobject]@{ Prefix = 'Major'; Label = "$Current → $major   an overhaul";          Value = [pscustomobject]@{ Seg = 'major'; Target = $major } }
    )
    try {
        return Select-Action -Header $Header -Items $items -Default 0 -ClearScreen -BannerStyle (Get-DeckleMenuBannerStyle)
    } catch {
        return $null
    }
}

function Get-RecordableCommitCountSinceVersion {
    param(
        [Parameter(Mandatory)][string]$Worktree,
        [Parameter(Mandatory)][string]$Version
    )
    $records = & git -C $Worktree log --format='%H%x1f%s'
    if ($LASTEXITCODE -ne 0) { throw "git log failed (code $LASTEXITCODE)" }
    $versionSubject = "^(chore\(version\)|chore\(release\)): v$([regex]::Escape($Version))$"
    $versionCommit = $null
    foreach ($record in $records) {
        $parts = $record -split ([char]0x1f), 2
        if ($parts.Count -eq 2 -and $parts[1] -match $versionSubject) {
            $versionCommit = $parts[0]
            break
        }
    }
    if (-not $versionCommit) { return 1 }
    $subjects = & git -C $Worktree log --format='%s' "$versionCommit..HEAD"
    if ($LASTEXITCODE -ne 0) { throw "git log since version record failed (code $LASTEXITCODE)" }
    $count = 0
    foreach ($subject in $subjects) {
        if ($subject -cmatch '^(feat|fix|perf|refactor|revert)(?:\([^)]+\))?!?:\s+') { $count++ }
    }
    return $count
}

function Invoke-RecordVersion {
    param([Parameter(Mandatory)][object[]]$MenuRows)
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    $cur = Get-CsprojVersion -Worktree $wt
    if (-not $cur -or $cur -notmatch '^\d+\.\d+\.\d+$') {
        Write-Host "No MAJOR.MINOR.PATCH <Version> found in that worktree." -ForegroundColor Red
        return
    }

    $choice = Select-VersionBump -Current $cur -Header 'Deckle > Project > Version   -   ↑↓ move   Enter select   Esc back'
    if ($null -eq $choice) {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return
    }

    Write-Host ""
    Write-Host "This records v$($choice.Target), refreshes Unreleased, and pushes the branch. It creates no tag or GitHub Release." -ForegroundColor Yellow
    if (-not (Read-YesNo -Question "Record Deckle v$($choice.Target) now?" -Default $true)) {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return
    }

    $scriptPath = Join-Path $LibDir 'record-version.ps1'
    return Invoke-DeckleMenuAction -Header 'Deckle > Project > Record version' -Label "Record v$($choice.Target)" -Source Release -MenuRows $MenuRows -Action {
        & $scriptPath -Target $wt -Bump $choice.Seg -Push
    }
}

# The whole release ritual behind one consent: record a new internal version if
# user-facing changes exist since the current version record, push the branch,
# build artifacts, then create the public GitHub Release and its tag.
function Invoke-PublishRelease {
    param([Parameter(Mandatory)][object[]]$MenuRows)
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    $cur = Get-CsprojVersion -Worktree $wt
    if (-not $cur -or $cur -notmatch '^\d+\.\d+\.\d+$') {
        Write-Host "No MAJOR.MINOR.PATCH <Version> found in that worktree." -ForegroundColor Red
        return
    }

    Import-Module (Join-Path $LibDir 'release-history.psm1') -Force
    $tag = "v$cur"
    $alreadyPublished = @(Get-PublishedReleaseTags -RepoRoot $wt) -contains $tag
    $recordableSinceVersion = Get-RecordableCommitCountSinceVersion -Worktree $wt -Version $cur
    $recordArgs = @{ Target = $wt; Push = $true }
    $recordVersion = $true
    $target = $cur
    if ($recordableSinceVersion -gt 0) {
        Write-Host "$recordableSinceVersion user-facing commit(s) exist after the v$cur version record." -ForegroundColor DarkGray
        $choice = Select-VersionBump -Current $cur -Header 'Deckle > Release > Publish   -   ↑↓ move   Enter select   Esc back'
        if ($null -eq $choice) {
            Write-Host "Cancelled." -ForegroundColor DarkGray
            return
        }
        $recordArgs.Bump = $choice.Seg
        $target = $choice.Target
        Write-Host ""
    } elseif ($alreadyPublished) {
        $recordVersion = $false
        Write-Host "$tag is already recorded; the release workflow will verify and reconcile its remote state." -ForegroundColor DarkGray
    } else {
        $recordArgs.Current = $true
        Write-Host "$tag is recorded internally and ready to become a public release." -ForegroundColor DarkGray
    }

    Write-Host "This pushes v$target, builds the artifacts, then publishes a PUBLIC GitHub Release. Its tag is created only after the builds succeed." -ForegroundColor Yellow
    if (-not (Read-YesNo -Question "Publish Deckle v$target to GitHub now?" -Default $false -ConfirmLabel "Publish v$target" -CancelLabel 'Keep private' -Destructive)) {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return
    }

    $recordScript = Join-Path $LibDir 'record-version.ps1'
    $publishScript = Join-Path $LibDir 'publish-app.ps1'
    return Invoke-DeckleMenuAction -Header 'Deckle > Release > Publish app' -Label "Publish app v$target" -Source Release -MenuRows $MenuRows -Action {
        if ($recordVersion) {
            & $recordScript @recordArgs
            if (-not $?) { return }
        }
        & $publishScript -Target $wt -Publish
    }
}

function Invoke-PrepareArtifacts {
    param([Parameter(Mandatory)][object[]]$MenuRows)
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    $scriptPath = Join-Path $LibDir 'publish-app.ps1'
    return Invoke-DeckleMenuAction -Header 'Deckle > Release > Prepare app' -Label 'Prepare app artifacts' -Source Release -MenuRows $MenuRows -Action {
        & $scriptPath -Target $wt
    }
}

function Invoke-NativeRuntime {
    param(
        [Parameter(Mandatory)][object[]]$MenuRows,
        [switch]$Publish
    )
    $wt = Get-WorktreeOrReturn
    if ($null -eq $wt) { return }
    $whisperRepo = Read-Optional -Question 'Path to whisper.cpp with an existing build/bin' -Header 'Deckle > Release > Native runtime' -Label 'Path' -Lines @('This action packages existing DLLs.', 'It does not invoke CMake or build Deckle.')
    if (-not $whisperRepo) { Write-Host "Cancelled: whisper.cpp path is required." -ForegroundColor DarkGray; return }

    $versionHolder = [pscustomobject]@{ Plan = $null }
    $versionCheck = Invoke-DeckleMenuAction `
        -Header 'Deckle > Release > Native runtime' `
        -Label 'Resolve native version' `
        -Source Release `
        -MenuRows $MenuRows `
        -Action {
            if ($Publish) {
                Write-Host '[release] Sync published native versions'
                $fetchOutput = @(& git -C $wt fetch origin main --tags --prune 2>&1)
                if ($LASTEXITCODE -ne 0) {
                    throw "git fetch origin main --tags failed: $($fetchOutput -join ' ')"
                }
            }
            Import-Module (Join-Path $LibDir 'native-runtime-release.psm1') -Force
            $publishedTags = @(& git -C $wt tag --list 'native-v*')
            if ($LASTEXITCODE -ne 0) { throw "git tag --list native-v* failed (code $LASTEXITCODE)" }
            $sourcePath = Join-Path $wt 'src\Deckle.Transcription.Whisper\Setup\NativeRuntime.cs'
            $versionHolder.Plan = Get-DeckleNativeRuntimeVersionPlan `
                -SourcePath $sourcePath `
                -WhisperRepo $whisperRepo `
                -PublishedTags $publishedTags
            Write-Host ("[release] native-v{0} follows native-v{1} for whisper.cpp {2}" -f `
                $versionHolder.Plan.Version, $versionHolder.Plan.PreviousVersion, $versionHolder.Plan.WhisperVersion)
        }
    if (-not $versionCheck.Succeeded) { return $versionCheck }

    $version = $versionHolder.Plan.Version
    $outDir = Read-Optional -Question 'Output directory (blank = temporary folder)' -Header 'Deckle > Release > Native runtime' -Label 'Folder'
    if ($Publish) {
        Write-Host "native-v$version is next for whisper.cpp $($versionHolder.Plan.WhisperVersion). Existing DLLs will be packaged and published publicly; nothing is built." -ForegroundColor Yellow
        if (-not (Read-YesNo -Question "Publish native-v$version now?" -Default $false -ConfirmLabel "Publish native-v$version" -CancelLabel 'Keep private' -Destructive)) {
            Write-Host "Cancelled." -ForegroundColor DarkGray
            return
        }
    }
    $nativeArgs = @{ Version = $version; WhisperRepo = $whisperRepo; Target = $wt }
    if ($outDir)  { $nativeArgs.OutDir = $outDir }
    if ($Publish) { $nativeArgs.Publish = $true }
    $scriptPath = Join-Path $LibDir 'publish-native-runtime.ps1'
    $label = if ($Publish) { "Publish native-v$version" } else { "Prepare native-v$version" }
    return Invoke-DeckleMenuAction -Header "Deckle > Release > $label" -Label $label -Source Release -MenuRows $MenuRows -Action {
        & $scriptPath @nativeArgs
    }
}

function Invoke-BootstrapDev {
    param([Parameter(Mandatory)][object[]]$MenuRows)
    $dryRun = Read-YesNo -Question 'Dry-run first (probe + plan, no install)?' -Default $true
    $full   = Read-YesNo -Question 'Include Tier 2 (native recompile toolchain + Ollama)?' -Default $false
    $bootstrapArgs = @{}
    if ($dryRun) { $bootstrapArgs.DryRun = $true }
    if ($full)   { $bootstrapArgs.Full = $true }
    if (-not $dryRun -and -not (Read-YesNo -Question 'Apply the environment bootstrap now?' -Default $false -ConfirmLabel 'Apply setup' -CancelLabel 'Keep machine unchanged' -Destructive)) { return }
    if (-not $dryRun) { $bootstrapArgs.Yes = $true }
    $scriptPath = Join-Path $LibDir 'bootstrap-dev-env.ps1'
    return Invoke-DeckleMenuAction -Header 'Deckle > Setup > Bootstrap' -Label 'Bootstrap dev environment' -Source Setup -MenuRows $MenuRows -Action {
        & $scriptPath @bootstrapArgs
    }
}

function Invoke-SetupAssets {
    param([Parameter(Mandatory)][object[]]$MenuRows)
    Write-Host "This may download native runtime and Whisper model files." -ForegroundColor DarkGray
    if (-not (Read-YesNo -Question 'Continue with runtime asset setup?' -Default $false)) {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return
    }
    $assetArgs = @{}
    $fromRelease = Read-Optional -Question 'Native runtime release version X.Y.Z (blank = local/sibling source or skip)'
    if ($fromRelease) { $assetArgs.FromRelease = $fromRelease }
    if (Read-YesNo -Question 'Download ggml-large-v3.bin (~3 GB)?' -Default $false) { $assetArgs.WithLarge = $true }
    if (Read-YesNo -Question 'Force re-copy / re-download existing files?' -Default $false -ConfirmLabel 'Replace files' -CancelLabel 'Keep existing' -Destructive) { $assetArgs.Force = $true }
    $scriptPath = Join-Path $LibDir 'setup-assets.ps1'
    return Invoke-DeckleMenuAction -Header 'Deckle > Setup > Runtime assets' -Label 'Set up runtime assets' -Source Setup -MenuRows $MenuRows -Action {
        & $scriptPath @assetArgs
    }
}
