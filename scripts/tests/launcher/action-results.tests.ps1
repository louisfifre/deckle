$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'
$LauncherDir = Join-Path $LibDir 'launcher'
$MenuDir = Join-Path $LibDir 'menu'
. (Join-Path $MenuDir 'chrome.ps1')
. (Join-Path $MenuDir 'grid-layout.ps1')
. (Join-Path $MenuDir 'grid-picker.ps1')
. (Join-Path $LibDir 'script-output.ps1')
. (Join-Path $LibDir 'native-console.ps1')
. (Join-Path $LauncherDir 'action-results.ps1')

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

$msbuildSuccess = @(ConvertTo-DeckleActionLogRecords -InputObject '  Deckle.App -> X:\repo\deckle\artifacts\bin\Deckle.App.dll' -Source Build)
Assert-Equal '  Deckle.App -> X:\repo\deckle\artifacts\bin\Deckle.App.dll' $msbuildSuccess[0].Message 'MSBuild success output remains raw'
Assert-Equal $null $msbuildSuccess[0].ForegroundColor 'ordinary successful compiler output stays in the terminal body color'
$nonDeckleBuild = @(ConvertTo-DeckleActionLogRecords -InputObject '  Microsoft.WindowsAppSDK -> D:\packages\Microsoft.WindowsAppSDK.dll' -Source Build)
Assert-Equal $null $nonDeckleBuild[0].ForegroundColor 'unrelated native output also inherits the terminal body color'

$workflowOutput = New-DeckleWorkflowOutput -Category 'build'
$stepOutput = @(& { Write-DeckleWorkflowStep -Output $workflowOutput -Message 'dotnet build (Release x64)' } 6>&1)
$stepRecord = @(ConvertTo-DeckleActionLogRecords -InputObject $stepOutput -Source Build)
Assert-Equal 1 $stepRecord.Count 'colored host fragments remain one logical log line'
Assert-Equal '[build] dotnet build (Release x64)' $stepRecord[0].Message 'workflow step text is reassembled without presentation artifacts'
Assert-Equal 2 $stepRecord[0].Segments.Count 'workflow step preserves its category and body segments'
Assert-Equal (Get-DeckleOutputColor -Role Category) $stepRecord[0].Segments[0].ForegroundColor 'workflow category keeps its semantic color'
Assert-Equal (Get-DeckleOutputColor -Role Heading) $stepRecord[0].Segments[1].ForegroundColor 'workflow title keeps its semantic heading color'

$escape = [char]27
$bell = [char]7
$terminalOutput = "${escape}[31mfirst${escape}[0m`rsecond${escape}]9;4;1;50${bell}`b"
$sanitized = @(ConvertTo-DeckleActionLogLines -InputObject $terminalOutput -Source Clean -Timestamp ([datetime]'2026-07-29T14:32:08'))
Assert-Equal 2 $sanitized.Count 'terminal repaint output becomes stable log lines'
Assert-Equal $true $sanitized[0].EndsWith('first') 'CSI color controls are removed'
Assert-Equal $true $sanitized[1].EndsWith('second') 'OSC progress controls and carriage returns are removed'

$title = Get-DeckleActionResultTitle -Label 'Build Debug' -State Succeeded -Elapsed ([timespan]::FromSeconds(18.4))
Assert-Equal 'Build Debug succeeded · 18.4 s' $title 'summary states action outcome and duration'

function New-GridStatusView {
    param(
        [string]$Header,
        [string]$HeaderCommands,
        [object[]]$Rows,
        [string]$Title,
        [object[]]$Lines,
        [switch]$Follow
    )
    $script:StartedActionHeader = $Header
    $script:StartedActionCommands = $HeaderCommands
    return [pscustomobject]@{ Name = 'grid-status' }
}
function Update-GridStatusView {
    param($View, [string]$Title, [object[]]$Lines, [switch]$Follow)
    $script:RenderedActionSnapshots.Add(@($Lines))
    return $View
}
function Close-GridStatusView {
    param($View)
    $script:ClosedActionView = $View.Name
}
$script:StartedActionHeader = $null
$script:StartedActionCommands = $null
$script:ClosedActionView = $null
$script:RenderedActionSnapshots = [System.Collections.Generic.List[object]]::new()
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
Assert-Equal 'Ctrl+C quit' $script:StartedActionCommands 'running surface keeps its exit command visible'
Assert-Equal $true ($script:RenderedActionSnapshots.Count -ge 2) 'completed lines refresh the retained live transcript'
Assert-Equal 'grid-status' $script:ClosedActionView 'completed action restores the cursor lifecycle'

$native = Invoke-DeckleMenuAction -Header Deckle -Label Build -Source Build -MenuRows $menuRows -Action {
    $nativeExitCode = Invoke-DeckleConsoleProcess -FilePath (Join-Path $PSHOME 'pwsh.exe') -ArgumentList @(
        '-NoProfile', '-Command', "Write-Output 'native first'; Write-Output 'native second'; exit 0"
    )
    if ($nativeExitCode -ne 0) { throw "native process failed with code $nativeExitCode" }
}
Assert-Equal 2 $native.Lines.Count 'native process lines remain available in the persistent result'
Assert-Equal 'native first' $native.Lines[0].Text 'persistent native history keeps its opening line'
Assert-Equal 'native second' $native.Lines[1].Text 'persistent native history keeps its following line'

$script:ClosedActionView = $null
$failure = Invoke-DeckleMenuAction -Header Deckle -Label Build -Source Build -MenuRows $menuRows -Action {
    Write-Output 'restore completed'
    throw 'compiler stopped'
}
Assert-Equal $false $failure.Succeeded 'terminating action error reports failure'
Assert-Equal $true ((@($failure.Lines | ForEach-Object Text) -join "`n") -like '*compiler stopped*') 'failure reason stays in the log'
Assert-Equal 'grid-status' $script:ClosedActionView 'failed action also restores the cursor lifecycle'

$partial = Invoke-DeckleMenuAction -Header Deckle -Label Setup -Source Setup -MenuRows $menuRows -Action {
    Write-DeckleOutputLine -Segments @(
        New-DeckleOutputSegment -Text '  Result        : ' -Role Body
        New-DeckleOutputSegment -Text 'Partial' -Role Warning
    )
}
Assert-Equal 'Partial' $partial.Result 'script summary result is preserved'
Assert-Equal $false $partial.Succeeded 'partial is not presented as full success'
Assert-Equal $true $partial.Title.StartsWith('Setup partial · ') 'partial state stays visible in the title'
Assert-Equal 2 $partial.Lines[0].Segments.Count 'segmented summary state remains one persistent result line'

$repeated = Invoke-DeckleMenuAction -Header Deckle -Label Build -Source Build -MenuRows $menuRows -Action {
    Write-Output 'same line'
    Write-Output 'same line'
}
Assert-Equal 2 $repeated.Lines.Count 'intentional repeated output is retained without deduplication'

$script:RenderedActionSnapshots.Clear()
$burst = Invoke-DeckleMenuAction -Header Deckle -Label Build -Source Build -MenuRows $menuRows -Action {
    1..100 | ForEach-Object { Write-Output "line $_" }
}
Assert-Equal 100 $burst.Lines.Count 'a burst larger than the viewport retains its complete transcript'
Assert-Equal $true ($script:RenderedActionSnapshots.Count -lt 10) 'a fast burst is coalesced instead of moving the cursor for every line'
$lastSnapshot = $script:RenderedActionSnapshots[$script:RenderedActionSnapshots.Count - 1]
Assert-Equal $burst.Lines[-1].Text $lastSnapshot[-1].Text 'the forced final frame and retained result end on the same line'

Write-Host 'action-results.tests.ps1: PASS' -ForegroundColor Green
