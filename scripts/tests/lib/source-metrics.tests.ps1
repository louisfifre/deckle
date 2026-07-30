$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
Import-Module (Join-Path $LibDir 'source-metrics.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

Assert-Equal 2 (Measure-CSharpEffectiveLines -Lines @(
    '// comment',
    'var uri = "https://deckle.local";',
    '/* block',
    '   comment */',
    'return 1; // tail'
)) 'comments and strings'

Assert-Equal 2 (Measure-CSharpEffectiveLines -Lines @(
    'var text = """',
    '// raw content',
    '/* raw content */',
    '""";'
)) 'raw string'

Assert-Equal 1 (Measure-ReswEntries -Lines @(
    '<root xmlns:xsd="http://www.w3.org/2001/XMLSchema">',
    '',
    '  <xsd:element name="data" />',
    '  <data name="Actual"><value>Value</value></data>',
    '</root>'
)) 'resw schema declarations are not resources'

Write-Host 'source-metrics.tests.ps1: PASS' -ForegroundColor Green
