$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$RepoRoot = Split-Path -Parent $ScriptsDir
$LibDir = Join-Path $ScriptsDir 'lib'
Import-Module (Join-Path $LibDir 'standalone-app-release.psm1') -Force

function Assert-Throws([scriptblock]$Action, [string]$Expected) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$Expected*") {
            throw "Expected error containing '$Expected', got '$($_.Exception.Message)'"
        }
        return
    }
    throw "Expected action to throw: $Expected"
}

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -cne $Actual) { throw "${Case}: expected '$Expected', got '$Actual'" }
}

# Each app names the project that exists in the repository and the exe its
# csproj builds.
foreach ($app in Get-StandaloneAppNames) {
    $release = Get-StandaloneAppRelease -App $app -RepoRoot $RepoRoot -Version '0.149.1'
    if (-not (Test-Path -LiteralPath $release.Csproj)) { throw "$app names a missing project: $($release.Csproj)" }
    $assembly = ([xml](Get-Content -Raw -LiteralPath $release.Csproj)).Project.PropertyGroup.AssemblyName | Where-Object { $_ } | Select-Object -First 1
    Assert-Equal "$assembly.exe" $release.ExeName "$app exe is the csproj's assembly"
    Assert-Equal "$assembly.pri" $release.PriName "$app .pri is the csproj's assembly"
    Assert-Equal "$app-v0.149.1" $release.Tag "$app tag"
    Assert-Equal "$assembly-v0.149.1.zip" $release.ZipName "$app archive"
    Assert-Equal "$assembly-v0.149.1.zip.sha256" $release.ShaName "$app checksum"
    # Deckle's installer and updater read only exact vX.Y.Z releases: an app's
    # tag must never take that form.
    if ($release.Tag -cmatch '^v\d+\.\d+\.\d+$') { throw "$app tag $($release.Tag) would be read as a Deckle release" }
}
Assert-Equal 'folders mail' ((Get-StandaloneAppNames) -join ' ') 'the standalone apps'

Assert-Throws { Get-StandaloneAppRelease -App 'projects' -RepoRoot $RepoRoot -Version '0.149.1' } 'not ''projects'''
Assert-Throws { Get-StandaloneAppRelease -App 'deckle' -RepoRoot $RepoRoot -Version '0.149.1' } 'not ''deckle'''
Assert-Throws { Get-StandaloneAppRelease -App 'folders' -RepoRoot $RepoRoot -Version 'v0.149.1' } 'MAJOR.MINOR.PATCH'

# The publish is self-contained and lands in the asked folder.
$publishArgs = Get-StandaloneAppPublishArguments -PublishDir 'C:\Code\out\publish'
foreach ($expected in '/t:Publish', '/p:_IsPublishing=true', '/p:SelfContained=true', '/p:RuntimeIdentifierOverride=win-x64', '/p:PublishDir=C:\Code\out\publish\') {
    if ($publishArgs -cnotcontains $expected) { throw "publish arguments lack $expected" }
}

# The upload is a draft, and nothing in it makes a release public or Latest.
$release = Get-StandaloneAppRelease -App 'mail' -RepoRoot $RepoRoot -Version '0.149.1'
$ghArgs = Get-StandaloneAppDraftArguments -Release $release -OwnerRepo 'owner/deckle' -HeadSha ('a' * 40) -ZipPath 'z.zip' -ShaPath 'z.zip.sha256' -NotesPath 'n.md'
Assert-Equal 'release create mail-v0.149.1 z.zip z.zip.sha256' ($ghArgs[0..4] -join ' ') 'the upload creates the app release with its two assets'
if ($ghArgs -cnotcontains '--draft') { throw 'the upload is not a draft' }
foreach ($forbidden in '--latest', '--draft=false', '--prerelease=false') {
    if ($ghArgs -ccontains $forbidden) { throw "the upload carries $forbidden" }
}
Assert-Equal ('a' * 40) $ghArgs[[array]::IndexOf($ghArgs, '--target') + 1] 'the draft targets the built commit'
Assert-Equal 'Deckle Mail v0.149.1' $ghArgs[[array]::IndexOf($ghArgs, '--title') + 1] 'the draft title'

$notes = Get-StandaloneAppDraftNotes -Release $release -HeadSha ('b' * 40)
foreach ($expected in 'DeckleMail.exe', 'DeckleMail-v0.149.1.zip', 'v0.149.1', ('b' * 12)) {
    if (-not $notes.Contains($expected)) { throw "draft notes lack $expected" }
}

Write-Host 'standalone-app-release.tests.ps1: PASS' -ForegroundColor Green
