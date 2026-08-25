$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $ScriptsDir 'lib\terminal-interaction.psm1') -Force
$module = Get-Module terminal-interaction

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected '$Expected', got '$Actual'" }
}

$bridgeAvailable = & $module { Initialize-TerminalHostBridge }
Assert-Equal $true $bridgeAvailable 'Windows console bridge compiles for this PowerShell engine'

$escape = [char]27
$origin = & $module { Get-TerminalCursorPositionSequence -X 0 -Y 0 }
$position = & $module { Get-TerminalCursorPositionSequence -X 7 -Y 3 }
Assert-Equal "$escape[1;1H" $origin 'terminal origin converts to one-based ANSI coordinates'
Assert-Equal "$escape[4;8H" $position 'terminal position converts row and column to ANSI coordinates'

$actionFocusStyle = Get-TerminalPresentationStyle -Role Action -State Focused
$actionFocus = & $module {
    param($style)
    Get-TerminalSegmentSequence `
        -X 7 `
        -Y 3 `
        -Text '> Launch' `
        -Foreground $style.Foreground `
        -Background $style.Background
} $actionFocusStyle
Assert-Equal "$escape[4;8H$escape[30;47m> Launch" $actionFocus 'a focused Action emits cursor, classic selection attributes, and content as one VT sequence'

$exitFocusStyle = Get-TerminalPresentationStyle -Role Exit -State Focused
$exitFocus = & $module {
    param($style)
    Get-TerminalSegmentSequence `
        -X 7 `
        -Y 3 `
        -Text '  Quit' `
        -Foreground $style.Foreground `
        -Background $style.Background
} $exitFocusStyle
Assert-Equal "$escape[4;8H$escape[30;47m  Quit" $exitFocus 'a focused Exit uses the classic ordinary selection treatment'

$commandKeyStyle = Get-TerminalPresentationStyle -Role CommandKey
$commandKey = & $module {
    param($style)
    Get-TerminalSegmentSequence `
        -X 0 `
        -Y 0 `
        -Text 'Enter' `
        -Foreground $style.Foreground `
        -Background ([ConsoleColor]::Black)
} $commandKeyStyle
Assert-Equal "$escape[1;1H$escape[37;40mEnter" $commandKey 'a command key remains distinct from its muted command label'

$commandLabelStyle = Get-TerminalPresentationStyle -Role CommandLabel
$commandLabel = & $module {
    param($style)
    Get-TerminalSegmentSequence `
        -X 6 `
        -Y 0 `
        -Text 'Open' `
        -Foreground $style.Foreground `
        -Background ([ConsoleColor]::Black)
} $commandLabelStyle
Assert-Equal "$escape[1;7H$escape[90;40mOpen" $commandLabel 'a command label emits the classic muted foreground through the VT stream'

$action = New-TerminalTarget -TargetId action.sample -Label Sample -IntentKind Action
$section = New-TerminalSection -Label Sample -Items @(New-TerminalActionRow -Label Test -Variants @($action))
$view = New-TerminalActionMenuView -ViewId menu.sample -Banner Sample -Sections @($section)
$frame = Get-TerminalInteractionFrame -View $view -Width 60 -Height 12 -FocusedTargetId action.sample
$snapshot = @(ConvertTo-TerminalFrameText -Frame $frame) -join "`n"
Assert-Equal $false $snapshot.Contains([string]$escape) 'redirectable frame text contains no cursor-control sequences'

Write-Host 'host.tests.ps1: PASS' -ForegroundColor Green
