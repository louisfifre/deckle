$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Repo = Split-Path -Parent $ScriptsDir
Import-Module (Join-Path $ScriptsDir 'lib\download-integrity.psm1') -Force

$root = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-download-integrity-$([guid]::NewGuid())"
try {
    $null = New-Item -ItemType Directory -Path $root

    # The hashes come from the C# sources the app itself reads.
    $models = Get-DeckleSpeechModelHashes -SourcePath (Join-Path $Repo 'src\Deckle.Transcription.Whisper\Setup\SpeechModels.cs')
    foreach ($file in 'ggml-base.bin', 'ggml-large-v3.bin') {
        if ($models[$file] -notmatch '^[0-9a-f]{64}$') { throw "No pinned hash read for $file" }
    }
    $silero = Get-DeckleSileroVadHash -SourcePath (Join-Path $Repo 'src\Deckle.Vad\SileroVadModel.cs')
    if ($silero -notmatch '^[0-9a-f]{64}$') { throw 'No pinned hash read for the Silero VAD model' }

    # The Parakeet files come from the catalog the wizard downloads from: the
    # default FP32 set, each with a size, a hash and a URL pinned to a revision.
    $parakeet = @(Get-DeckleParakeetModelFiles -SourcePath (Join-Path $Repo 'src\Deckle.Transcription.Parakeet\Setup\ParakeetModelCatalog.cs'))
    if ($parakeet.Count -ne 6) { throw "Expected 6 Parakeet files, read $($parakeet.Count)" }
    foreach ($file in $parakeet) {
        if ($file.Sha256 -notmatch '^[0-9a-f]{64}$') { throw "No pinned hash read for $($file.Path)" }
        if ($file.SizeBytes -le 0) { throw "No size read for $($file.Path)" }
        if ($file.Url -notmatch '^https://huggingface\.co/[^/]+/[^/]+/resolve/[0-9a-f]{40}/') { throw "Url of $($file.Path) is not pinned to a revision: $($file.Url)" }
        if ($file.Path -match '^(fp16|int8|w4a8)/|nemo128|\.zst$') { throw "$($file.Path) is not part of the default download" }
    }

    # The optional FP16 encoder is not read: only the Files list counts.
    $catalogFixture = Join-Path $root 'ParakeetModelCatalog.cs'
    @'
public const string Repository = "owner/model";
public const string Revision = "0123456789abcdef0123456789abcdef01234567";
public static IReadOnlyList<ParakeetModelFile> Files { get; } =
[
    new("vocab.txt", 1_000, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
];
public static ParakeetModelFile Fp16Encoder { get; } =
    new("fp16/encoder.onnx", 5, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
'@ | Set-Content -LiteralPath $catalogFixture
    $parsedParakeet = @(Get-DeckleParakeetModelFiles -SourcePath $catalogFixture)
    if ($parsedParakeet.Count -ne 1) { throw 'The FP16 encoder was read as part of the default files' }
    if ($parsedParakeet[0].SizeBytes -ne 1000) { throw 'The size was not read without its digit separators' }
    if ($parsedParakeet[0].Sha256 -cne ('a' * 64)) { throw 'The Parakeet hash was not lower-cased' }
    if ($parsedParakeet[0].Url -cne 'https://huggingface.co/owner/model/resolve/0123456789abcdef0123456789abcdef01234567/vocab.txt') { throw "Unexpected Parakeet Url: $($parsedParakeet[0].Url)" }

    # A source in the shape SpeechModels.cs has, with the default model named
    # through a constant: the file name must come from the Url.
    $fixture = Join-Path $root 'SpeechModels.cs'
    @'
public const string DefaultModelFileName = "ggml-base.bin";
new ModelEntry(
    Id:          "whisper-base",
    FileName:    DefaultModelFileName,
    DisplayName: "Whisper base (~150 MB)",
    Url:         "https://example.test/main/ggml-base.bin",
    SizeBytes:   3L,
    Sha256:      "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
'@ | Set-Content -LiteralPath $fixture
    $parsed = Get-DeckleSpeechModelHashes -SourcePath $fixture
    if ($parsed['ggml-base.bin'] -cne ('a' * 64)) { throw 'The hash was not read by file name, lower-cased' }

    # A sound file passes, an altered or truncated one fails, a missing one fails.
    $good = Join-Path $root 'good.bin'
    [IO.File]::WriteAllBytes($good, [byte[]](1..200))
    $goodHash = (Get-FileHash -LiteralPath $good -Algorithm SHA256).Hash
    if (-not (Test-DeckleFileSha256 -Path $good -ExpectedSha256 $goodHash)) { throw 'A sound file was refused' }

    $altered = Join-Path $root 'altered.bin'
    $bytes = [IO.File]::ReadAllBytes($good)
    $bytes[100] = $bytes[100] -bxor 0xFF
    [IO.File]::WriteAllBytes($altered, $bytes)
    if (Test-DeckleFileSha256 -Path $altered -ExpectedSha256 $goodHash) { throw 'An altered file of the same size was accepted' }

    $truncated = Join-Path $root 'truncated.bin'
    [IO.File]::WriteAllBytes($truncated, [byte[]](1..199))
    if (Test-DeckleFileSha256 -Path $truncated -ExpectedSha256 $goodHash) { throw 'A truncated file was accepted' }

    if (Test-DeckleFileSha256 -Path (Join-Path $root 'absent.bin') -ExpectedSha256 $goodHash) { throw 'A missing file was accepted' }
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
Write-Host 'download-integrity tests passed'
