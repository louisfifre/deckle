$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'native-runtime-release.psm1') -Force

function Assert-Throws([scriptblock]$Action, [string]$MessageFragment) {
    try {
        & $Action
        throw "Expected failure containing '$MessageFragment'"
    } catch {
        if ($_.Exception.Message -notlike "*$MessageFragment*") { throw }
    }
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-native-release-$([guid]::NewGuid())"
try {
    $null = New-Item -ItemType Directory -Path $root
    $artifact = Join-Path $root 'deckle-native-1.9.1.zip'
    [IO.File]::WriteAllBytes($artifact, [byte[]](1, 2, 3, 4))
    $sha256 = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    $source = Join-Path $root 'NativeRuntime.cs'
    @"
public static NativeRuntimeBundle CurrentBundle { get; } = new(
    Version:     "1.9.1",
    Url:         "https://example.test/deckle-native-1.9.1.zip",
    Sha256:      "$sha256",
    SizeBytes:   4L,
    DisplayName: "Whisper.cpp + Vulkan runtime");
"@ | Set-Content -LiteralPath $source

    $bundle = Get-DeckleNativeRuntimeBundle -SourcePath $source
    if ($bundle.Version -cne '1.9.1' -or $bundle.SizeBytes -ne 4) {
        throw 'Native runtime metadata was not parsed correctly'
    }
    $verified = Assert-DeckleNativeRuntimeArtifact -Bundle $bundle -ArtifactPath $artifact
    if ($verified.Sha256 -cne $sha256) { throw 'Native runtime hash was not returned' }

    $wrongSize = $bundle.PSObject.Copy()
    $wrongSize.SizeBytes = 5
    Assert-Throws { Assert-DeckleNativeRuntimeArtifact -Bundle $wrongSize -ArtifactPath $artifact } 'size mismatch'

    $wrongHash = $bundle.PSObject.Copy()
    $wrongHash.Sha256 = '00' * 32
    Assert-Throws { Assert-DeckleNativeRuntimeArtifact -Bundle $wrongHash -ArtifactPath $artifact } 'SHA-256 mismatch'

    Write-Host 'native-runtime-release.tests.ps1 passed' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
