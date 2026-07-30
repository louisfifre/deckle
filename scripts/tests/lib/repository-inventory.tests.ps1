$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
Import-Module (Join-Path $LibDir 'repository-inventory.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected '$Expected', got '$Actual'" }
}

function Assert-True($Actual, [string]$Case) {
    if (-not $Actual) { throw "${Case}: expected true" }
}

Assert-True (Test-RepositoryPathInScope -RelativePath 'src/App/File.cs' -Scope 'src') 'path belongs to scope'
Assert-Equal $false (Test-RepositoryPathInScope -RelativePath 'scripts/tool.ps1' -Scope 'src') 'path outside scope'

$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ('deckle-repository-inventory-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    & git -C $fixture init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize repository inventory fixture.' }
    New-Item -ItemType Directory -Path (Join-Path $fixture 'app') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixture 'notes') | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $fixture 'app/main.cs'), "// note`nvar value = 1;`nreturn value;", [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $fixture 'app/View.g.cs'), "generated();", [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $fixture 'notes/readme.md'), "# Notes`ntext", [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $fixture 'ignored.txt'), 'ignored', [System.Text.UTF8Encoding]::new($false))
    & git -C $fixture add -- app/main.cs app/View.g.cs notes/readme.md
    if ($LASTEXITCODE -ne 0) { throw 'Could not stage repository inventory fixture.' }

    $all = Invoke-RepositoryInventory -RepoRoot $fixture -FileSet Text -MeasureContent -GroupBy Extension
    Assert-Equal 3 $all.Totals.Files 'only tracked files are inventoried'
    Assert-Equal 6 $all.Totals.Lines 'text lines are measured'
    Assert-Equal 2 $all.Groups.Count 'extension grouping'

    $source = Invoke-RepositoryInventory -RepoRoot $fixture -RelativePath app -FileSet Source -MeasureContent -MeasureSource `
        -LinesWarning 2 -LinesCritical 3 -SourceLinesWarning 1 -SourceLinesCritical 3
    Assert-Equal 1 $source.Totals.Files 'relative path filters candidates before measurement'
    Assert-Equal 2 $source.Totals.SourceLines 'source lines ignore the pure comment line'
    Assert-Equal 2 $source.Findings.Count 'line and source thresholds share the finding contract'
    Assert-Equal 'Critical' @($source.Findings | Where-Object Measure -eq Lines)[0].Level 'critical line threshold'

    $empty = Invoke-RepositoryInventory -RepoRoot $fixture -RelativePath missing -FileSet Text
    Assert-Equal 0 $empty.Totals.Files 'an unmatched safe scope is a successful empty result'
} finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}

Write-Host 'repository-inventory.tests.ps1: PASS' -ForegroundColor Green
