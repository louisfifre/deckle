$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
$ScriptPath = Join-Path $ScriptsDir 'fetch-autocorrect-data.ps1'
Import-Module (Join-Path $LibDir 'download-integrity.psm1') -Force

function Assert-Throws([scriptblock]$Action, [string]$MessageFragment) {
    try {
        & $Action
    } catch {
        if ($_.Exception.Message -notlike "*$MessageFragment*") { throw }
        return
    }
    throw "Expected failure containing '$MessageFragment'"
}

# The script's own Download function, lifted from its source so the test runs
# the code that ships rather than a copy.
$ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref]$null, [ref]$null)
$download = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Download' }, $true)
if (-not $download) { throw 'Download function not found in fetch-autocorrect-data.ps1' }
function Write-WorkflowMessage { param($Output, $Message, $Role) }
function Get-SizeMB($path) { [math]::Round((Get-Item $path).Length / 1MB, 2) }
$WorkflowOutput = $null
$Force = $false
. ([scriptblock]::Create($download.Extent.Text))

$root = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-fetch-autocorrect-$([guid]::NewGuid())"
try {
    $null = New-Item -ItemType Directory -Path $root
    $good = Join-Path $root 'good.src'
    $bad = Join-Path $root 'bad.src'
    [IO.File]::WriteAllBytes($good, [byte[]](1..200))
    $altered = [IO.File]::ReadAllBytes($good)
    $altered[100] = $altered[100] -bxor 0xFF
    [IO.File]::WriteAllBytes($bad, $altered)
    $goodHash = (Get-FileHash -LiteralPath $good -Algorithm SHA256).Hash.ToLowerInvariant()
    $goodUrl = ([uri]$good).AbsoluteUri
    $badUrl = ([uri]$bad).AbsoluteUri

    # An altered download is refused and leaves nothing behind.
    $dst = Join-Path $root 'one.bin'
    Assert-Throws { Download @($badUrl) $dst $goodHash | Out-Null } 'SHA-256 mismatch'
    if ((Test-Path $dst) -or (Test-Path "$dst.part")) { throw 'An altered download left a file behind' }

    # A sound download takes its place; run again it is only recognised by hash.
    if (-not (Download @($goodUrl) $dst $goodHash)) { throw 'A sound download was not fetched' }
    if (Download @($goodUrl) $dst $goodHash) { throw 'A file with the right hash was fetched again' }

    # A wrong file already in place is replaced, not trusted for its size.
    [IO.File]::WriteAllBytes($dst, $altered)
    if (-not (Download @($goodUrl) $dst $goodHash)) { throw 'A same-size altered file was kept' }
    if (-not (Test-DeckleFileSha256 -Path $dst -ExpectedSha256 $goodHash)) { throw 'The altered file was not replaced' }

    # A failing first source falls back to the next one.
    $dst2 = Join-Path $root 'two.bin'
    if (-not (Download @($badUrl, $goodUrl) $dst2 $goodHash)) { throw 'The fallback source was not used' }
    if (-not (Test-DeckleFileSha256 -Path $dst2 -ExpectedSha256 $goodHash)) { throw 'The fallback file is wrong' }
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}

# The script keeps to https and carries no personal address.
$text = Get-Content -LiteralPath $ScriptPath -Raw
if ($text -match 'http://') { throw 'fetch-autocorrect-data.ps1 still names an http:// URL' }
if ($text -match '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}') { throw 'fetch-autocorrect-data.ps1 still holds an e-mail address' }

Write-Host 'fetch-autocorrect-data tests passed'
