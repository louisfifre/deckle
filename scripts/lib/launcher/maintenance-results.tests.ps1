$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'statistics-plans.ps1')
. (Join-Path $PSScriptRoot 'maintenance-results.ps1')

function Assert-Contains([string[]]$Lines, [string]$Expected, [string]$Case) {
    if (($Lines -join "`n") -notlike "*$Expected*") { throw "${Case}: missing '$Expected'" }
}

$repository = [pscustomobject]@{
    Worktree = 'D:\repo'
    Modules = @([pscustomobject]@{ Module = 'App' })
    Totals = [pscustomobject]@{ Files = 12; LocCs = 100; LocXaml = 20; LocTotal = 120; ReswKeys = 4 }
    Repository = [pscustomobject]@{
        Files = 20; Lines = 400; Bytes = 8192; Scopes = @('src', 'scripts')
        Types = @([pscustomobject]@{ Type = '.cs'; Files = 4; Lines = 300; Bytes = 4096 })
    }
    LargeFiles = @([pscustomobject]@{ Extension = '.cs'; Loc = 700; RawLines = 900; RelativeRepo = 'src/App/Large.cs' })
    ResourceFiles = @()
}
$repositoryLines = @(ConvertTo-RepositoryStatisticsLines -Statistics $repository)
Assert-Contains $repositoryLines 'Total LOC 120' 'repository total'
Assert-Contains $repositoryLines 'Repository 20 files' 'repository scope total'
Assert-Contains $repositoryLines '.cs' 'repository file types'
Assert-Contains $repositoryLines '700 LOC' 'repository threshold detail'

$context = [pscustomobject]@{
    Worktree = 'D:\repo'
    Totals = [pscustomobject]@{ Documents = 8; Sections = 30; Lines = 400; Bytes = 4096; EstimatedTokens = 2000; Added1Day = 1; Added7Days = 2; Added30Days = 3 }
    Groups = @([pscustomobject]@{ LoadingMode = 'Automatic instructions'; Files = 2; Lines = 100; Bytes = 1024; EstimatedTokens = 500 })
}
$contextLines = @(ConvertTo-ContextStatisticsLines -Statistics $context)
Assert-Contains $contextLines 'Documents 8' 'context total'
Assert-Contains $contextLines 'Automatic instructions' 'context loading mode'

$failure = Invoke-MaintenanceStatisticsScan -Kind Repository -Worktree 'D:\missing' -LibDir 'D:\missing'
if ($failure.Succeeded) { throw 'failed scan should return a failure result' }
Assert-Contains $failure.Lines 'could not complete' 'failed scan stays renderable'

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
