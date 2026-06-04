# publish-app.ps1
#
# Builds the self-contained, unpackaged distribution of the Deckle app as a
# versioned folder ZIP — the payload the installer downloads from the GitHub
# Release and extracts into the user-chosen install folder.
#
# Folder (xcopy) deployment, NOT single-file: rationale lives in
# src/Deckle.App/Deckle.App.csproj (the _IsPublishing PropertyGroup). The ZIP
# carries the whole publish folder — Deckle.exe + .NET runtime + Windows App
# SDK + Deckle.pri + assets — which runs in place from wherever it lands.
#
# What it does NOT ship: the whisper.cpp native runtime and the Whisper models.
# Those are provisioned per-user by the app's first-run wizard (the native
# bundle has its own publish-native-runtime.ps1 / native-vX.Y.Z release cycle).
#
# `publish` is the maintainer's act: this script produces the local ZIP for
# inspection; pass -Publish to ALSO create the GitHub Release via gh.
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

    # Also publish the ZIP (+ its .sha256 sidecar) as a GitHub Release tagged
    # v$Version via gh. Requires gh authenticated against the repo's remote.
    # The version is read from the csproj; gh attaches to the existing tag.
    [switch]$Publish,

    # Optional release-notes file passed to `gh release create --notes-file`.
    # Without it, gh's --generate-notes is used.
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

# ── Summary — release convention the installer / future updater consumes ─────
Step 'Done'
Write-Host @"

  Folder : $PublishDir
  Zip    : $ZipPath
  Size   : $ZipBytes bytes ($ZipSize MB)
  SHA256 : $ZipSha256

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
    $ghArgs = @('release', 'create', $tag, $ZipPath, $ShaPath, '--title', $title)
    if ($Notes) { $ghArgs += @('--notes-file', $Notes) }
    else        { $ghArgs += '--generate-notes' }
    & gh @ghArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed (code $LASTEXITCODE)" }
    Ok "Released as $tag"
}
