$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$RepoRoot = Split-Path -Parent $ScriptsDir
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'
$TestDir = Join-Path $ScriptsDir 'tests'

$commands = @(Get-ChildItem -LiteralPath $CommandDir -File -Filter '*.ps1')
if ($commands.Count -eq 0) { throw 'scripts/commands must contain the directly executable workflows.' }

$testsOutsideTestDir = @(Get-ChildItem -LiteralPath $ScriptsDir -Recurse -File -Filter '*.tests.ps1' |
    Where-Object { -not $_.FullName.StartsWith($TestDir + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) })
if ($testsOutsideTestDir.Count -gt 0) {
    throw "PowerShell tests must live under scripts/tests: $($testsOutsideTestDir.FullName -join ', ')"
}

# The vendored Deckle Scripts files follow their own repository's rules; the
# manifest names them.
$manifest = Get-Content -Raw -LiteralPath (Join-Path $ScriptsDir '.deckle-scripts\manifest.json') | ConvertFrom-Json
$vendored = @($manifest.files.PSObject.Properties.Name | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $RepoRoot $_)) })

$libraryEntryPoints = @()
foreach ($file in Get-ChildItem -LiteralPath $LibDir -Recurse -File -Include '*.ps1', '*.psm1') {
    if ($vendored -contains $file.FullName) { continue }
    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw "Could not parse library file $($file.FullName)." }
    if ($null -ne $ast.ParamBlock) { $libraryEntryPoints += $file.FullName }
}
if ($libraryEntryPoints.Count -gt 0) {
    throw "Library files must be imported, not invoked as commands: $($libraryEntryPoints -join ', ')"
}

# A Header fails silently when voided, so every script either has a valid one
# or none at all; tests are exempt because their fixtures break Headers on purpose.
Import-Module (Join-Path $LibDir 'script-discovery.psm1') -Force
$headerFaults = @(Get-ChildItem -LiteralPath $ScriptsDir -Recurse -File -Include '*.ps1' |
    Where-Object { -not $_.FullName.StartsWith($TestDir + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { Read-ScriptHeader -Path $_.FullName } |
    Where-Object { $_.Status -notin @('Described', 'NoHeader') })
if ($headerFaults.Count -gt 0) {
    $faultLines = $headerFaults | ForEach-Object {
        "$([IO.Path]::GetRelativePath($ScriptsDir, $_.Path)) [$($_.Status)] $($_.Reason)"
    }
    throw "Script Headers must be valid or absent:`n$($faultLines -join "`n")"
}

$workspace = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'deckle.code-workspace') | ConvertFrom-Json
$repositoryFolders = @($workspace.folders | Where-Object { $_.path -eq '.' })
if ($repositoryFolders.Count -ne 1) {
    throw 'The shared workspace must open the cloned repository exactly once.'
}

# The workspace is opened from the primary checkout, so its relative folders and the
# expected container both anchor there, not to the linked worktree running this test.
$gitCommonDir = git -C $RepoRoot rev-parse --path-format=absolute --git-common-dir
if (-not $gitCommonDir) { throw 'Could not resolve the primary checkout from the git common directory.' }
$PrimaryRoot = Split-Path -Parent ([System.IO.Path]::GetFullPath($gitCommonDir))
$projectContainer = Split-Path -Parent $PrimaryRoot
$worktreeContainer = Join-Path (Split-Path -Parent $projectContainer) "worktrees\$(Split-Path -Leaf $PrimaryRoot)"
$resolvedWorkspaceFolders = @($workspace.folders | ForEach-Object {
    [System.IO.Path]::GetFullPath((Join-Path $PrimaryRoot $_.path))
})
if ($resolvedWorkspaceFolders -notcontains $worktreeContainer) {
    throw "The shared workspace must expose the project worktree container: $worktreeContainer"
}

Write-Host 'script-layout.tests.ps1: PASS' -ForegroundColor Green
