# publish-app.ps1
#
# Cuts a Deckle GitHub Release: builds the two artefacts a release carries and
# (with -Publish) uploads them under the version tag.
#
#   1. The installer stub — Deckle-Setup-v<X.Y.Z>-win-x64.exe — the HEADLINE
#      asset, the file an end user downloads and runs. A small NativeAOT exe
#      (src/Deckle.Installer) that resolves the latest release, pulls the app ZIP
#      below, verifies its sha256, extracts it per-user, and registers Deckle.
#   2. The self-contained app payload — Deckle-v<X.Y.Z>.zip + .sha256 — NOT a
#      user-facing download but the soute the installer fetches: the whole
#      publish folder (Deckle.exe + .NET runtime + Windows App SDK + Deckle.pri +
#      assets), run in place, xcopy deployment (folder, NOT single-file:
#      rationale in the _IsPublishing PropertyGroup of Deckle.App.csproj).
#
# GitHub auto-attaches the source-code archives — nothing to do for those.
#
# What it does NOT ship: the whisper.cpp native runtime and the Whisper models.
# Those are provisioned per-user by the app's first-run wizard (the native
# bundle has its own publish-native-runtime.ps1 / native-vX.Y.Z release cycle).
#
# `publish` is the maintainer's act: without -Publish this builds both artefacts
# locally for inspection; -Publish ALSO creates the GitHub Release via gh.
#
# Pendant to publish-native-runtime.ps1 — same Step/Ok/Warn idiom, same
# zip + sha256 + paste-ready-summary + optional -Publish shape.

[CmdletBinding()]
param(
    # Build a specific repo or worktree instead of the one containing this
    # script. Accepts any path — main repo or any git worktree root.
    [string]$Target,

    # Interactive picker: lists the main repo + all linked worktrees and
    # prompts for a choice. Overrides -Target.
    [switch]$Pick,

    # Output directory for the publish folder + produced ZIP. Defaults to a
    # versioned subfolder under <RepoRoot>\artifacts\ (git-ignored). Never the
    # system temp dir — build artefacts stay on the repo's volume, off C:.
    [string]$OutDir,

    # Also publish the installer exe + the app ZIP (+ its .sha256 sidecar) as a
    # GitHub Release tagged v$Version via gh. Requires gh authenticated against
    # the repo's remote. The version is read from the csproj; gh attaches to the
    # existing tag (or creates it).
    [switch]$Publish,

    # Optional release-notes file passed to `gh release create --notes-file`.
    # Without it, the notes are generated from the commit history by
    # changelog.ps1 (home-grown, plain git log, no GitHub API).
    [string]$Notes
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot                                  # scripts/lib/

function Step($msg) { Write-Host "`n[publish] $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "           $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "           $msg" -ForegroundColor Yellow }

# ── RepoRoot resolution (mirrors build-run.ps1) ──────────────────────────────
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

# ── Read <Version> — single source of truth is the csproj ────────────────────
$Version  = $null
$verMatch = Select-String -Path $Csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
if ($verMatch) { $Version = $verMatch.Matches[0].Groups[1].Value.Trim() }
if (-not $Version) { throw "<Version> not found in $Csproj" }
Step "Deckle v$Version"

# ── Resolve owner/repo from the git remote, for the release-URL hint ─────────
# (the in-code native URL still points at the pre-rename owner; resolving from
# the live remote keeps this script honest if the owner changes again.)
$OwnerRepo = '<owner>/deckle'
$remoteUrl = & git -C $RepoRoot remote get-url origin 2>$null
if ($LASTEXITCODE -eq 0 -and $remoteUrl) {
    if ($remoteUrl -match 'github\.com[:/]([^/]+/[^/.]+)') { $OwnerRepo = $Matches[1] }
}

# ── Output layout ────────────────────────────────────────────────────────────
if (-not $OutDir) {
    # Folder name = the ZIP stem (Deckle-v<X.Y.Z>) so the artefact layout is
    # coherent end to end: artifacts\Deckle-v0.4.0\Deckle-v0.4.0.zip, matching
    # the release tag v<X.Y.Z> and the asset download URL.
    $OutDir = Join-Path $RepoRoot "artifacts\Deckle-v$Version"
}
if (Test-Path $OutDir) {
    Warn "OutDir exists, cleaning: $OutDir"
    Remove-Item $OutDir -Recurse -Force
}
$null = New-Item -ItemType Directory -Path $OutDir

$PublishDir = Join-Path $OutDir 'publish'
$ZipName    = "Deckle-v$Version.zip"
$ZipPath    = Join-Path $OutDir $ZipName
$ShaName    = "$ZipName.sha256"
$ShaPath    = Join-Path $OutDir $ShaName

# Headline asset: the installer stub, named the OSS-classic way (version + arch)
# so it reads as "the thing to download" among the release files.
$SetupName       = "Deckle-Setup-v$Version-win-x64.exe"
$SetupPath       = Join-Path $OutDir $SetupName
$InstallerCsproj = Join-Path $RepoRoot 'src\Deckle.Installer\Deckle.Installer.csproj'
$InstallerPubDir = Join-Path $OutDir 'installer'

# ── Publish: self-contained, unpackaged, folder (no PublishSingleFile) ───────
# win-x64 via RuntimeIdentifierOverride, NOT `-r win-x64`. A plain RID
# propagates to the ProjectReferences, landing each generated .pri under a
# per-RID bin subfolder (bin\...\win-x64\). The head project's PRI merge then
# looks for some of them at the non-RID path and fails with PRI252 — this is
# WindowsAppSDK issue #3337. RuntimeIdentifierOverride is Microsoft's
# documented workaround: it sets the RID without the path-splitting
# propagation. SelfContained is forced (a RID no longer implies it since
# .NET 6); the app is x64-only (<Platforms>x64</Platforms>). Restore implicit.
Step 'dotnet publish (Release, win-x64, self-contained folder)'
& dotnet publish $Csproj `
    '-c:Release' `
    '-p:RuntimeIdentifierOverride=win-x64' `
    '-p:SelfContained=true' `
    '-p:Platform=x64' `
    '-o' $PublishDir `
    '-v:m' '-nologo'
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (code $LASTEXITCODE)" }

# ── Sanity: the two files a misconfigured publish silently drops ─────────────
$exe = Join-Path $PublishDir 'Deckle.exe'
$pri = Join-Path $PublishDir 'Deckle.pri'
if (-not (Test-Path $exe)) { throw "Deckle.exe missing from publish output — $exe" }
# Deckle.pri carries every compiled XAML resource (.xbf) since WinAppSDK 1.8.
# Without it the app launches windowless. EnableMsixTooling=true in the csproj
# is what makes Publish emit it; treat its absence as a hard failure.
if (-not (Test-Path $pri)) { throw "Deckle.pri missing — windowless app. Check EnableMsixTooling in the csproj." }
$fileCount = (Get-ChildItem $PublishDir -Recurse -File).Count
Ok "publish folder OK — $fileCount files (Deckle.exe + Deckle.pri present)"

# ── Zip the folder + zip-level SHA256 ────────────────────────────────────────
# CreateFromDirectory (streaming) rather than Compress-Archive: the publish
# folder is ~300 MB across 100s of files, where Compress-Archive is slow and
# memory-hungry. includeBaseDirectory:$false puts Deckle.exe at the zip root so
# the installer extracts straight into the target folder.
Step "Compress to $ZipName"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $PublishDir, $ZipPath,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)

$ZipSha256 = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLower()
$ZipBytes  = (Get-Item $ZipPath).Length
$ZipSize   = [math]::Round($ZipBytes / 1MB, 2)
Ok "$ZipName ($ZipSize MB) sha256=$ZipSha256"

# sha256sum -c compatible sidecar, so the installer (or a human) can verify the
# download independently of the release body.
Set-Content -Path $ShaPath -Value ('{0} *{1}' -f $ZipSha256, $ZipName) -Encoding ascii
Ok "$ShaName written"

# ── Build the installer stub (NativeAOT, win-x64) — the headline asset ───────
# The file the end user downloads and runs. A standalone native exe (PublishAot
# in src/Deckle.Installer): it resolves the latest GitHub Release, downloads the
# app ZIP above, verifies it against the .sha256 sidecar, extracts it per-user,
# and registers Deckle. NativeAOT links here at publish — it needs the VC++
# linker on the maintainer's machine; a plain `dotnet build` only validates the
# IL. `-r win-x64` is correct here: the installer has no PRI merge, so none of
# the WinAppSDK RID quirk the app build dodges with RuntimeIdentifierOverride.
# x64-only matches the app: the payload it fetches is x64-only too.
Step 'dotnet publish installer (Release, win-x64, NativeAOT)'
if (-not (Test-Path $InstallerCsproj)) { throw "Installer csproj not found at $InstallerCsproj" }
& dotnet publish $InstallerCsproj `
    '-c:Release' `
    '-r' 'win-x64' `
    '-o' $InstallerPubDir `
    '-v:m' '-nologo'
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (installer) failed (code $LASTEXITCODE)" }

# AssemblyName is Deckle-Installer → the linker emits Deckle-Installer.exe. Copy
# it up to the version- and arch-tagged release name. The exe reads its own path
# at runtime (it self-copies as the uninstaller), so the download name is free
# to be descriptive.
$InstallerBuilt = Join-Path $InstallerPubDir 'Deckle-Installer.exe'
if (-not (Test-Path $InstallerBuilt)) { throw "Installer exe missing from publish output — $InstallerBuilt" }
Copy-Item $InstallerBuilt $SetupPath -Force
$SetupBytes = (Get-Item $SetupPath).Length
$SetupSize  = [math]::Round($SetupBytes / 1MB, 2)
Ok "$SetupName ($SetupSize MB)"

# ── Summary — release convention the installer / future updater consumes ─────
Step 'Done'
Write-Host @"

  Installer : $SetupPath ($SetupSize MB)   <- the user-facing download
  Payload   : $ZipPath ($ZipSize MB)
  SHA256    : $ZipSha256

  Release convention (asset URLs the installer resolves):
    https://github.com/$OwnerRepo/releases/download/v$Version/$ZipName
    https://github.com/$OwnerRepo/releases/download/v$Version/$ShaName

  To publish (maintainer act):
    pwsh scripts/lib/publish-app.ps1 -Target "$RepoRoot" -Publish

"@ -ForegroundColor Green

# ── Optional: gh release create (maintainer act, never run by tooling) ───────
if ($Publish) {
    Step "Publish via gh release create v$Version"
    $tag    = "v$Version"
    $title  = "Deckle $tag"
    # Asset order = upload order = display order on the release page: the
    # installer exe first (the headline download), then the payload + its sha.
    $ghArgs = @('release', 'create', $tag, $SetupPath, $ZipPath, $ShaPath, '--title', $title)
    # Every 0.x cut is a pre-release (versioning convention): the phase is
    # pre-stable, so the release must not claim the repo's "Latest" badge.
    if ($Version -like '0.*') { $ghArgs += '--prerelease' }
    # Release notes come from changelog.ps1 (plain git log, no API). -Notes
    # overrides with a hand-written file when a release needs special wording.
    if (-not $Notes) {
        $Notes = Join-Path $OutDir 'release-notes.md'
        & (Join-Path $ScriptDir 'changelog.ps1') -Target $RepoRoot -NotesFor $Version -OutFile $Notes
        if ($LASTEXITCODE -ne 0) { throw "changelog.ps1 notes generation failed (code $LASTEXITCODE)" }
        Ok "Release notes generated from history: $Notes"
    }
    $ghArgs += @('--notes-file', $Notes)
    & gh @ghArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed (code $LASTEXITCODE)" }
    Ok "Released as $tag"
}
