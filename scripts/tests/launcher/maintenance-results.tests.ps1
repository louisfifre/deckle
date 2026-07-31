$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'
$LauncherDir = Join-Path $LibDir 'launcher'
$MenuDir = Join-Path $LibDir 'menu'
. (Join-Path $LauncherDir 'statistics-plans.ps1')
. (Join-Path $LauncherDir 'maintenance-results.ps1')

function Assert-Contains([string[]]$Lines, [string]$Expected, [string]$Case) {
    if (($Lines -join "`n") -notlike "*$Expected*") { throw "${Case}: missing '$Expected'" }
}

$targetedSpecification = New-MaintenanceScanSpecification -Kind Repository -Goal files-to-review
$targeted = [pscustomobject]@{
    Kind = 'Repository'
    Worktree = 'D:\repo'
    Specification = $targetedSpecification
    Totals = [pscustomobject]@{
        Files = 3; Bytes = 4096; Lines = 1200; SourceLines = 700; ReswKeys = 0
        MeasuredFiles = 3; LinkedFiles = 1
    }
    Groups = @()
    Findings = @([pscustomobject]@{
        Category = 'Threshold'; Path = 'src/App/Large.cs'; Measure = 'SourceLines'
        Value = 700; Threshold = 600; Level = 'Critical'
    })
    Items = @()
    Diagnostics = @('Link counted without traversal: docs/reference.md')
}
$targetedLines = @(ConvertTo-TargetedStatisticsLines -Result $targeted)
Assert-Contains $targetedLines 'Goal      Files to review' 'targeted result remembers its purpose'
Assert-Contains $targetedLines '700 source lines' 'finding exposes the measured value'
Assert-Contains $targetedLines 'counted without traversal' 'safe scan diagnostics remain visible'

Write-Host 'maintenance-results.tests.ps1: PASS' -ForegroundColor Green
