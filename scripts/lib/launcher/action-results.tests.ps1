$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\menu\chrome.ps1')
. (Join-Path $PSScriptRoot '..\menu\grid-layout.ps1')
. (Join-Path $PSScriptRoot '..\menu\grid-picker.ps1')
. (Join-Path $PSScriptRoot 'action-results.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

Assert-Equal 'Error' (Get-DeckleActionLogLevel -Message 'Build FAILED.') 'failed build is classified as an error'
Assert-Equal 'Warning' (Get-DeckleActionLogLevel -Message 'App.cs(1): warning CS0000') 'compiler warning is classified'
Assert-Equal 'Step' (Get-DeckleActionLogLevel -Message '[build] dotnet build') 'workflow milestone is classified'
foreach ($prefix in @('readme', 'changelog', 'record-version', 'hooks')) {
    Assert-Equal 'Step' (Get-DeckleActionLogLevel -Message "[$prefix] Start") "$prefix milestone is classified"
}
Assert-Equal 'Summary' (Get-DeckleActionLogLevel -Message '[summary] Done') 'summary keeps its dedicated level'
Assert-Equal 'Info' (Get-DeckleActionLogLevel -Message 'Restore completed') 'ordinary output stays informational'
Assert-Equal 'Info' (Get-DeckleActionLogLevel -Message '0 Warning(s)  0 Error(s)') 'zero-count build summary stays informational'
Assert-Equal 'Warning' (Get-DeckleActionLogLevel -Message '2 Warning(s)  0 Error(s)') 'nonzero warning summary stays actionable'

$formatted = @(ConvertTo-DeckleActionLogLines -InputObject "first`nsecond" -Source Build -Timestamp ([datetime]'2026-07-29T14:32:08'))
Assert-Equal 2 $formatted.Count 'multiline output becomes separate log entries'
Assert-Equal $true $formatted[0].StartsWith('14:32:08  Info     Build') 'log columns stay aligned'

$medium = @(ConvertTo-DeckleActionLogLines -InputObject 'compile' -Source Build -Timestamp ([datetime]'2026-07-29T14:32:08') -ContentWidth 52)
Assert-Equal $true $medium[0].StartsWith('Info     Build') 'medium logs omit time before truncating the message'
$narrow = @(ConvertTo-DeckleActionLogLines -InputObject 'compile' -Source Build -Timestamp ([datetime]'2026-07-29T14:32:08') -ContentWidth 38)
Assert-Equal 'Info     compile' $narrow[0] 'narrow logs preserve level and message before optional metadata'

$hostWarning = @(& { Write-Host 'tool needs attention' -ForegroundColor Yellow } 6>&1)[0]
$structured = @(ConvertTo-DeckleActionLogRecords -InputObject $hostWarning -Source Build)
Assert-Equal 'Warning' $structured[0].Level 'host color provides a structured internal log level'

$escape = [char]27
$bell = [char]7
$terminalOutput = "${escape}[31mfirst${escape}[0m`rsecond${escape}]9;4;1;50${bell}`b"
$sanitized = @(ConvertTo-DeckleActionLogLines -InputObject $terminalOutput -Source Clean -Timestamp ([datetime]'2026-07-29T14:32:08'))
Assert-Equal 2 $sanitized.Count 'terminal repaint output becomes stable log lines'
Assert-Equal $true $sanitized[0].EndsWith('first') 'CSI color controls are removed'
Assert-Equal $true $sanitized[1].EndsWith('second') 'OSC progress controls and carriage returns are removed'

$title = Get-DeckleActionResultTitle -Label 'Build Debug' -State Succeeded -Elapsed ([timespan]::FromSeconds(18.4))
Assert-Equal 'Build Debug succeeded · 18.4 s' $title 'summary states action outcome and duration'

function New-GridStatusView { return [pscustomobject]@{ Name = 'status-view' } }
function Update-GridStatusView {
    param($View, [string]$Title, [string[]]$Lines, [switch]$Follow)
    $script:ActionViewUpdateCount++
    return $View
}
$script:ActionViewUpdateCount = 0
$menuRows = @(@{ Cells = @( @{ Label = 'Build' } ) })

$success = Invoke-DeckleMenuAction -Header Deckle -Label Build -Source Build -MenuRows $menuRows -Action {
    Write-Output 'restore completed'
    [Threading.Thread]::Sleep(120)
    Write-Host 'build completed'
}
Assert-Equal $true $success.Succeeded 'captured action reports success'
Assert-Equal 2 $success.Lines.Count 'output and host streams are both retained'
Assert-Equal $true ($script:ActionViewUpdateCount -gt 0) 'captured output updates the existing action view'

$failure = Invoke-DeckleMenuAction -Header Deckle -Label Build -Source Build -MenuRows $menuRows -Action {
    Write-Output 'restore completed'
    throw 'compiler stopped'
}
Assert-Equal $false $failure.Succeeded 'terminating action error reports failure'
Assert-Equal $true (($failure.Lines -join "`n") -like '*compiler stopped*') 'failure reason stays in the log'

$partial = Invoke-DeckleMenuAction -Header Deckle -Label Setup -Source Setup -MenuRows $menuRows -Action {
    Write-Host '  Result        : Partial'
}
Assert-Equal 'Partial' $partial.Result 'script summary result is preserved'
Assert-Equal $false $partial.Succeeded 'partial is not presented as full success'
Assert-Equal $true $partial.Title.StartsWith('Setup partial · ') 'partial state stays visible in the title'

Write-Host 'action-results.tests.ps1: PASS' -ForegroundColor Green
