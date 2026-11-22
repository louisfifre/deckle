<#
.SYNOPSIS
    Set up runtime assets
.DESCRIPTION
    Populates the models folder under the user data root with the Parakeet
    speech model (FP32, into parakeet-tdt-0.6b-v3-ultra, about 2.4 GB) and the
    Silero VAD model the app needs at runtime. Idempotent: skips a file that
    already carries the SHA-256 pinned in the C# code (a wrong or truncated one
    is deleted and fetched again), unless -Force. Every download is checked
    against its pinned SHA-256 before it takes its place.
.NOTES
    order: 130
    action: Runtime assets / set up |
#>

[CmdletBinding()]
param(
    # Override of the target UserDataRoot. Highest priority, ahead of
    # DECKLE_DATA_ROOT and %LOCALAPPDATA%\Deckle\.
    [string]$DataRoot,

    # Re-download even if the destination already carries the pinned
    # SHA-256.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'download-integrity.psm1') -Force

$Repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# Same order as AppPaths.ResolveUserDataRoot, after the -DataRoot override.
if ($DataRoot)                 { $TargetRoot = $DataRoot }
elseif ($env:DECKLE_DATA_ROOT) { $TargetRoot = $env:DECKLE_DATA_ROOT }
else                           { $TargetRoot = Join-Path $env:LOCALAPPDATA 'Deckle' }

$TargetModels = Join-Path $TargetRoot 'models'
# ParakeetModelFiles.DefaultFolderName.
$TargetParakeet = Join-Path $TargetModels 'parakeet-tdt-0.6b-v3-ultra'

$WorkflowOutput = New-WorkflowOutput -Category 'setup'

$Workflow = 'Set up runtime assets'
$ModelStatus = 'Pending'

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Runtime asset setup failed before completion." `
        -Details ([ordered]@{
            'Data root' = $TargetRoot
            Models      = $ModelStatus
            Error       = $_.Exception.Message
        })
    throw
}

# The file list and the SHA-256 of every download are read where the app's setup
# wizard reads them, so the two cannot drift apart.
$ParakeetCatalogSource = Join-Path $Repo 'src\Deckle.Transcription.Parakeet\Setup\ParakeetModelCatalog.cs'
$SileroVadSource = Join-Path $Repo 'src\Deckle.Vad\SileroVadModel.cs'
$ParakeetFiles = Get-DeckleParakeetModelFiles -SourcePath $ParakeetCatalogSource
$SileroSha256 = Get-DeckleSileroVadHash -SourcePath $SileroVadSource

# curl.exe ships with Windows and beats Invoke-WebRequest on large files.
# The file lands under a .part name and takes its place only once its SHA-256
# equals $expectedSha256: a wrong download is deleted and never replaces a good
# file. A file already at the destination counts as present only by that same
# hash, never by its size.
function Download($url, $dst, $expectedSha256) {
    $name = Split-Path $dst -Leaf
    if (-not $Force -and (Test-Path $dst)) {
        if (Test-DeckleFileSha256 -Path $dst -ExpectedSha256 $expectedSha256) {
            Write-WorkflowMessage -Output $WorkflowOutput -Message "already present $name ($([math]::Round((Get-Item $dst).Length/1MB,1)) MB, SHA-256 verified)"
            return
        }
        Write-WorkflowMessage -Output $WorkflowOutput -Message "$name does not match its pinned SHA-256, downloading it again" -Role Warning
    }
    New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null
    $part = "$dst.part"
    if (Test-Path $part) { Remove-Item $part -Force }
    Write-Host "         downloading $name ..."
    & curl.exe -L --fail --retry 3 --progress-bar -o $part $url
    $curlExitCode = $LASTEXITCODE
    Write-Host ''
    if ($curlExitCode -ne 0) {
        if (Test-Path $part) { Remove-Item $part -Force }
        throw "curl failed for $url"
    }
    if (-not (Test-DeckleFileSha256 -Path $part -ExpectedSha256 $expectedSha256)) {
        $actual = (Get-FileHash -LiteralPath $part -Algorithm SHA256).Hash.ToLowerInvariant()
        Remove-Item $part -Force
        throw "SHA-256 mismatch for $name from $url : expected $expectedSha256, got $actual. The file was deleted."
    }
    Move-Item -LiteralPath $part -Destination $dst -Force
    Write-WorkflowMessage -Output $WorkflowOutput -Message "downloaded $name ($([math]::Round((Get-Item $dst).Length/1MB,1)) MB, SHA-256 verified)"
}

Write-WorkflowStep -Output $WorkflowOutput -Message "target root: $TargetRoot"

Write-WorkflowStep -Output $WorkflowOutput -Message 'create folders'
foreach ($d in @($TargetModels, $TargetParakeet)) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
        Write-WorkflowMessage -Output $WorkflowOutput -Message "created $d"
    } else {
        Write-WorkflowMessage -Output $WorkflowOutput -Message "exists  $d"
    }
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'download the Parakeet speech model'
foreach ($file in $ParakeetFiles) {
    Download $file.Url (Join-Path $TargetParakeet ($file.Path -replace '/', '\')) $file.Sha256
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'download Silero VAD'
$vadDst = Join-Path $TargetModels 'silero_vad.onnx'
Download `
    'https://raw.githubusercontent.com/snakers4/silero-vad/v6.2/src/silero_vad/data/silero_vad.onnx' `
    $vadDst `
    $SileroSha256
$ModelStatus = 'Parakeet and Silero VAD present'

$parakeetCount = @($ParakeetFiles | Where-Object { Test-Path (Join-Path $TargetParakeet ($_.Path -replace '/', '\')) }).Count
Write-WorkflowStep -Output $WorkflowOutput -Message 'done'
Write-Host "         $TargetParakeet : $parakeetCount / $($ParakeetFiles.Count) file(s)"
Write-OutputLine -Segments @()
Write-OutputLine -Segments @(
    New-OutputSegment -Text 'Next: ' -Role Category
    New-OutputSegment -Text 'scripts\menu.ps1 (Build & run)' -Role Body
)

Write-ActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence "Runtime assets were set up under $TargetRoot." `
    -Details ([ordered]@{
        'Data root'     = $TargetRoot
        Models          = $ModelStatus
        'Parakeet files' = "$parakeetCount / $($ParakeetFiles.Count)"
        Force           = $(if ($Force) { 'Yes' } else { 'No' })
    }) `
    -Next @("Run scripts\menu.ps1 and choose Build & run.")
