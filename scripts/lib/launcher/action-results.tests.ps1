$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\menu\chrome.ps1')
. (Join-Path $PSScriptRoot '..\menu\grid-picker.ps1')
. (Join-Path $PSScriptRoot 'action-results.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

Assert-Equal 'Error' (Get-DeckleActionLogLevel -Message 'Build FAILED.') 'failed build is classified as an error'
Assert-Equal 'Warning' (Get-DeckleActionLogLevel -Message 'App.cs(1): warning CS0000') 'compiler warning is classified'
Assert-Equal 'Step' (Get-DeckleActionLogLevel -Message '[build] dotnet build') 'workflow milestone is classified'
Assert-Equal 'Info' (Get-DeckleActionLogLevel -Message 'Restore completed') 'ordinary output stays informational'
Assert-Equal 'Info' (Get-DeckleActionLogLevel -Message '0 Warning(s)  0 Error(s)') 'zero-count build summary stays informational'
Assert-Equal 'Warning' (Get-DeckleActionLogLevel -Message '2 Warning(s)  0 Error(s)') 'nonzero warning summary stays actionable'

$formatted = @(ConvertTo-DeckleActionLogLines -InputObject "first`nsecond" -Source Build -Timestamp ([datetime]'2026-07-29T14:32:08'))
Assert-Equal 2 $formatted.Count 'multiline output becomes separate log entries'
Assert-Equal $true $formatted[0].StartsWith('14:32:08  Info     Build') 'log columns stay aligned'

$title = Get-DeckleActionResultTitle -Label 'Build Debug' -State Succeeded -Elapsed ([timespan]::FromSeconds(18.4))
Assert-Equal 'Build Debug succeeded · 18.4 s' $title 'summary states action outcome and duration'

function Show-GridStatus { }
$menuRows = @(@{ Cells = @( @{ Label = 'Build' } ) })

$success = Invoke-DeckleMenuAction -Header Deckle -Label Build -Source Build -MenuRows $menuRows -Action {
    Write-Output 'restore completed'
    Write-Host 'build completed'
}
Assert-Equal $true $success.Succeeded 'captured action reports success'
Assert-Equal 2 $success.Lines.Count 'output and host streams are both retained'

$failure = Invoke-DeckleMenuAction -Header Deckle -Label Build -Source Build -MenuRows $menuRows -Action {
    Write-Output 'restore completed'
    throw 'compiler stopped'
}
Assert-Equal $false $failure.Succeeded 'terminating action error reports failure'
Assert-Equal $true (($failure.Lines -join "`n") -like '*compiler stopped*') 'failure reason stays in the log'

Write-Host 'action-results.tests.ps1: PASS' -ForegroundColor Green
