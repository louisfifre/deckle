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
Assert-Equal 'first' $formatted[0] 'logs display only the emitted message'
Assert-Equal 'second' $formatted[1] 'message-only logs need no responsive metadata columns'

$hostWarning = @(& { Write-Host 'tool needs attention' -ForegroundColor Yellow } 6>&1)[0]
$structured = @(ConvertTo-DeckleActionLogRecords -InputObject $hostWarning -Source Build)
Assert-Equal 'Warning' $structured[0].Level 'host color provides a structured internal log level'
Assert-Equal ([ConsoleColor]::Yellow) $structured[0].ForegroundColor 'host color is retained for menu rendering'

$escape = [char]27
$bell = [char]7
$terminalOutput = "${escape}[31mfirst${escape}[0m`rsecond${escape}]9;4;1;50${bell}`b"
$sanitized = @(ConvertTo-DeckleActionLogLines -InputObject $terminalOutput -Source Clean -Timestamp ([datetime]'2026-07-29T14:32:08'))
Assert-Equal 2 $sanitized.Count 'terminal repaint output becomes stable log lines'
Assert-Equal $true $sanitized[0].EndsWith('first') 'CSI color controls are removed'
Assert-Equal $true $sanitized[1].EndsWith('second') 'OSC progress controls and carriage returns are removed'

$title = Get-DeckleActionResultTitle -Label 'Build Debug' -State Succeeded -Elapsed ([timespan]::FromSeconds(18.4))
Assert-Equal 'Build Debug succeeded · 18.4 s' $title 'summary states action outcome and duration'

function Start-MenuActionConsole {
    param([string]$Header)
    $script:StartedActionHeader = $Header
    return [pscustomobject]@{ Name = 'action-console' }
}
function Write-MenuActionOutput {
    param($InputObject)
    $script:ForwardedActionOutput.Add([string]$InputObject)
}
function Stop-MenuActionConsole {
    param($Console)
    $script:StoppedActionConsole = $Console.Name
}
$script:StartedActionHeader = $null
$script:StoppedActionConsole = $null
$script:ForwardedActionOutput = [System.Collections.Generic.List[string]]::new()
$menuRows = @(@{ Cells = @( @{ Label = 'Build' } ) })

$success = Invoke-DeckleMenuAction -Header Deckle -Label Build -Source Build -MenuRows $menuRows -Action {
    Write-Output 'restore completed'
    [Threading.Thread]::Sleep(120)
    Write-Host 'build completed' -ForegroundColor Green
}
Assert-Equal $true $success.Succeeded 'captured action reports success'
Assert-Equal 2 $success.Lines.Count 'output and host streams are both retained'
Assert-Equal 'restore completed' $success.Lines[0].Text 'plain output remains raw'
Assert-Equal ([ConsoleColor]::Green) $success.Lines[1].ForegroundColor 'PowerShell host color reaches the menu result'
Assert-Equal 'Deckle · Running…' $script:StartedActionHeader 'running state is appended to the breadcrumb'
Assert-Equal 'restore completed' $script:ForwardedActionOutput[0] 'captured output is forwarded immediately'
Assert-Equal 'action-console' $script:StoppedActionConsole 'completed action restores the terminal surface'

$script:StoppedActionConsole = $null
$failure = Invoke-DeckleMenuAction -Header Deckle -Label Build -Source Build -MenuRows $menuRows -Action {
    Write-Output 'restore completed'
    throw 'compiler stopped'
}
Assert-Equal $false $failure.Succeeded 'terminating action error reports failure'
Assert-Equal $true ((@($failure.Lines | ForEach-Object Text) -join "`n") -like '*compiler stopped*') 'failure reason stays in the log'
Assert-Equal 'action-console' $script:StoppedActionConsole 'failed action also restores the terminal surface'

$partial = Invoke-DeckleMenuAction -Header Deckle -Label Setup -Source Setup -MenuRows $menuRows -Action {
    Write-Host '  Result        : Partial'
}
Assert-Equal 'Partial' $partial.Result 'script summary result is preserved'
Assert-Equal $false $partial.Succeeded 'partial is not presented as full success'
Assert-Equal $true $partial.Title.StartsWith('Setup partial · ') 'partial state stays visible in the title'

Write-Host 'action-results.tests.ps1: PASS' -ForegroundColor Green
