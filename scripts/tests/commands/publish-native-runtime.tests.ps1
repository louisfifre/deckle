$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'
. (Join-Path $ScriptsDir 'tests\lib\pe-fixture.ps1')

$scriptPath = Join-Path $CommandDir 'release\publish-native-runtime.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $scriptPath,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count) {
    throw "publish-native-runtime.ps1 does not parse: $($parseErrors[0].Message)"
}

$commands = @($ast.FindAll({
    param($node)
    $node -is [Management.Automation.Language.CommandAst]
}, $true))

foreach ($command in $commands) {
    $name = $command.GetCommandName()
    $text = $command.Extent.Text
    if ($name -ceq 'dotnet') {
        throw "Native publication must not invoke a Deckle build: $text"
    }
    if ($name -ceq 'cmake' -and $text -match '(?i)(^|\s)--build(\s|$)') {
        throw "Native publication must consume the existing whisper.cpp build: $text"
    }
    if ($name -ceq 'ninja' -and $text -notmatch '(?i)--version') {
        throw "Native publication must not invoke Ninja: $text"
    }
    if ($name -ceq 'gh' -and $text -match '(?i)\brelease\s+(create|view|edit|download)\b' -and
        $text -notmatch '(?i)(^|\s)--repo(\s|$)') {
        throw "Every GitHub release operation must name its repository: $text"
    }
    if ($name -ceq 'gh' -and $text -match '(?i)\brelease\s+create\b' -and
        $text -notmatch '(?i)(^|\s)--draft(\s|$)') {
        throw "Native release upload must begin as an explicit draft: $text"
    }
}

$parameterNames = @($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
if ($parameterNames -notcontains 'ArtifactPath') {
    throw 'Native publication must accept an existing artifact path'
}

$root = Join-Path ([IO.Path]::GetTempPath()) "deckle-native-command-$([guid]::NewGuid())"
try {
    $sourceDir = Join-Path $root 'src\Deckle.Transcription.Whisper\Setup'
    $artifactDir = Join-Path $root 'artifacts\deckle-native-1.9.1'
    $stagingDir = Join-Path $root 'staging'
    $null = New-Item -ItemType Directory -Path $sourceDir, $artifactDir, $stagingDir

    # Real PE images: the command reads their import tables before it accepts
    # an existing bundle.
    $dllNames = @('vcruntime140.dll', 'ggml.dll', 'whisper.dll')
    New-DeckleTestPeImage -Path (Join-Path $stagingDir 'vcruntime140.dll') -Imports @('KERNEL32.dll')
    New-DeckleTestPeImage -Path (Join-Path $stagingDir 'ggml.dll') -Imports @('vcruntime140.dll', 'KERNEL32.dll') -DelayImports @('vulkan-1.dll')
    New-DeckleTestPeImage -Path (Join-Path $stagingDir 'whisper.dll') -Imports @('ggml.dll', 'vcruntime140.dll')
    Set-Content -LiteralPath (Join-Path $stagingDir 'PROVENANCE.txt') -Value 'fixture provenance' -Encoding utf8NoBOM
    $sumLines = foreach ($name in $dllNames) {
        $dllPath = Join-Path $stagingDir $name
        "$((Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash.ToLowerInvariant()) *$name"
    }
    Set-Content -LiteralPath (Join-Path $stagingDir 'SHA256SUMS') -Value $sumLines -Encoding utf8NoBOM

    $artifactPath = Join-Path $artifactDir 'deckle-native-1.9.1.zip'
    Compress-Archive -Path (Join-Path $stagingDir '*') -DestinationPath $artifactPath
    $artifactSha256 = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $artifactSize = (Get-Item -LiteralPath $artifactPath).Length
    $sourcePath = Join-Path $sourceDir 'NativeRuntime.cs'
    @"
public const string EntryDll = "whisper.dll";
public static IReadOnlyList<string> RequiredDllNames { get; } = new[]
{
    "vcruntime140.dll",
    "ggml.dll",
    EntryDll,
};
public static NativeRuntimeBundle CurrentBundle { get; } = new(
    Version:     "1.9.1",
    Url:         "https://example.test/deckle-native-1.9.1.zip",
    Sha256:      "$artifactSha256",
    SizeBytes:   ${artifactSize}L,
    DisplayName: "Whisper.cpp + Vulkan runtime");
"@ | Set-Content -LiteralPath $sourcePath -Encoding utf8NoBOM

    $commandOutput = @(& $scriptPath -Target $root *>&1)
    if (-not $?) {
        throw "Pinned artifact auto-discovery failed: $($commandOutput -join ' ')"
    }
    if (($commandOutput -join "`n") -notmatch 'validated locally and is ready to publish') {
        throw 'Pinned artifact auto-discovery did not report a validated existing bundle'
    }
    if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
        throw 'Existing artifact validation must not consume or replace the bundle'
    }

    # The same pinned bundle, with ggml.dll now importing a runtime the
    # bundle does not ship, is refused by name of the missing import.
    New-DeckleTestPeImage -Path (Join-Path $stagingDir 'ggml.dll') -Imports @('vcruntime140.dll', 'libgomp-1.dll')
    $sumLines = foreach ($name in $dllNames) {
        $dllPath = Join-Path $stagingDir $name
        "$((Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash.ToLowerInvariant()) *$name"
    }
    Set-Content -LiteralPath (Join-Path $stagingDir 'SHA256SUMS') -Value $sumLines -Encoding utf8NoBOM
    Remove-Item -LiteralPath $artifactPath
    Compress-Archive -Path (Join-Path $stagingDir '*') -DestinationPath $artifactPath
    $artifactSha256 = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $artifactSize = (Get-Item -LiteralPath $artifactPath).Length
    (Get-Content -LiteralPath $sourcePath -Raw) `
        -replace 'Sha256:\s*"[0-9a-f]+"', "Sha256:      `"$artifactSha256`"" `
        -replace 'SizeBytes:\s*\d+L', "SizeBytes:   ${artifactSize}L" |
        Set-Content -LiteralPath $sourcePath -Encoding utf8NoBOM -NoNewline
    $refusal = [System.Collections.Generic.List[string]]::new()
    $refused = $false
    try {
        & $scriptPath -Target $root *>&1 | ForEach-Object { $refusal.Add([string]$_) }
    } catch {
        $refused = $true
        $refusal.Add($_.Exception.Message)
    }
    if (-not $refused) { throw 'A bundle importing a DLL it does not ship must be refused' }
    if (($refusal -join "`n") -notmatch 'ggml\.dll imports libgomp-1\.dll, which is neither in the bundle nor part of Windows') {
        throw "The refusal must name the importer and the missing import: $($refusal -join ' ')"
    }
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}

Write-Host 'publish-native-runtime.tests.ps1 passed' -ForegroundColor Green
