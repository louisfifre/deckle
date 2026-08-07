$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$RepoRoot = Split-Path -Parent $ScriptsDir
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'
$TestDir = Join-Path $ScriptsDir 'tests'

$commands = @(Get-ChildItem -LiteralPath $CommandDir -File -Filter '*.ps1')
if ($commands.Count -eq 0) { throw 'scripts/commands must contain the directly executable workflows.' }
if (Get-ChildItem -LiteralPath $CommandDir -Directory) { throw 'scripts/commands must stay flat so command repo-root resolution remains stable.' }

$testsOutsideTestDir = @(Get-ChildItem -LiteralPath $ScriptsDir -Recurse -File -Filter '*.tests.ps1' |
    Where-Object { -not $_.FullName.StartsWith($TestDir + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) })
if ($testsOutsideTestDir.Count -gt 0) {
    throw "PowerShell tests must live under scripts/tests: $($testsOutsideTestDir.FullName -join ', ')"
}

$libraryEntryPoints = @()
foreach ($file in Get-ChildItem -LiteralPath $LibDir -Recurse -File -Include '*.ps1', '*.psm1') {
    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw "Could not parse library file $($file.FullName)." }
    if ($null -ne $ast.ParamBlock) { $libraryEntryPoints += $file.FullName }
}
if ($libraryEntryPoints.Count -gt 0) {
    throw "Library files must be imported, not invoked as commands: $($libraryEntryPoints -join ', ')"
}

$workspace = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'deckle.code-workspace') | ConvertFrom-Json
$repositoryFolders = @($workspace.folders | Where-Object { $_.path -eq '.' })
if ($repositoryFolders.Count -ne 1) {
    throw 'The shared workspace must open the cloned repository exactly once.'
}

Write-Host 'script-layout.tests.ps1: PASS' -ForegroundColor Green
