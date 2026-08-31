$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $ScriptsDir 'lib\terminal-interaction.psm1') -Force
$module = Get-Module terminal-interaction

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected '$Expected', got '$Actual'" }
}

function Assert-Rgb([string]$Expected, $Actual, [string]$Case) {
    Assert-Equal $Expected (@($Actual) -join ',') $Case
}

$bridgeAvailable = & $module { Initialize-TerminalHostBridge }
Assert-Equal $true $bridgeAvailable 'Windows console bridge compiles for this PowerShell engine'

$escape = [char]27
$origin = & $module { Get-TerminalCursorPositionSequence -X 0 -Y 0 }
$position = & $module { Get-TerminalCursorPositionSequence -X 7 -Y 3 }
Assert-Equal "$escape[1;1H" $origin 'terminal origin converts to one-based ANSI coordinates'
Assert-Equal "$escape[4;8H" $position 'terminal position converts row and column to ANSI coordinates'

$darkHost = [pscustomobject]@{
    Color = 'Supported'
    OriginalForeground = [ConsoleColor]::Gray
    OriginalBackground = [ConsoleColor]::Black
    ThemeVariant = 'Dark'
}
foreach ($expectedRoleColor in @(
    [pscustomobject]@{ Role = 'Section'; Color = [ConsoleColor]::Magenta; Rgb = '199,146,234' },
    [pscustomobject]@{ Role = 'ActionLabel'; Color = [ConsoleColor]::Cyan; Rgb = '119,207,192' },
    [pscustomobject]@{ Role = 'Access'; Color = [ConsoleColor]::DarkYellow; Rgb = '186,217,115' }
)) {
    $resolvedColors = & $module {
        param($role, $hostState)
        Get-TerminalSegmentColors `
            -Segment ([pscustomobject]@{ PresentationRole = $role; State = 'Normal' }) `
            -HostState $hostState
    } $expectedRoleColor.Role $darkHost
    Assert-Equal $expectedRoleColor.Color $resolvedColors.Foreground "$($expectedRoleColor.Role) retains its legacy renderer fallback"
    Assert-Equal ([ConsoleColor]::Black) $resolvedColors.Background "$($expectedRoleColor.Role) retains the host background fallback"
    Assert-Rgb $expectedRoleColor.Rgb $resolvedColors.ForegroundRgb "$($expectedRoleColor.Role) exact dark token reaches the VT renderer"
    Assert-Rgb '1,22,39' $resolvedColors.BackgroundRgb "$($expectedRoleColor.Role) exact dark surface reaches the VT renderer"
}
$sectionSequence = & $module {
    Get-TerminalSegmentSequence `
        -X 1 `
        -Y 4 `
        -Text 'RUN ' `
        -Foreground ([ConsoleColor]::Magenta) `
        -Background ([ConsoleColor]::Black) `
        -ForegroundRgb ([int[]]@(199, 146, 234)) `
        -BackgroundRgb ([int[]]@(1, 22, 39))
}
Assert-Equal "$escape[5;2H$escape[38;2;199;146;234;48;2;1;22;39mRUN " $sectionSequence 'a Section emits its exact dark attributes and text in one VT sequence'

$actionFocusStyle = Get-TerminalPresentationStyle -Role Action -State Focused -ThemeVariant Dark
$actionFocus = & $module {
    param($style)
    Get-TerminalSegmentSequence `
        -X 7 `
        -Y 3 `
        -Text '> Launch' `
        -Foreground $style.Foreground `
        -Background $style.Background `
        -ForegroundRgb $style.ForegroundRgb `
        -BackgroundRgb $style.BackgroundRgb
} $actionFocusStyle
Assert-Equal "$escape[4;8H$escape[38;2;1;22;39;48;2;255;255;255m> Launch" $actionFocus 'a focused Action emits the exact inverted dark selection in one VT sequence'

$exitFocusStyle = Get-TerminalPresentationStyle -Role Exit -State Focused -ThemeVariant Dark
$exitFocus = & $module {
    param($style)
    Get-TerminalSegmentSequence `
        -X 7 `
        -Y 3 `
        -Text '  Quit' `
        -Foreground $style.Foreground `
        -Background $style.Background `
        -ForegroundRgb $style.ForegroundRgb `
        -BackgroundRgb $style.BackgroundRgb
} $exitFocusStyle
Assert-Equal "$escape[4;8H$escape[38;2;1;22;39;48;2;255;255;255m  Quit" $exitFocus 'a focused Exit uses the same exact selection treatment as every target'

$commandKeyStyle = Get-TerminalPresentationStyle -Role CommandKey
$commandKey = & $module {
    param($style)
    Get-TerminalSegmentSequence `
        -X 0 `
        -Y 0 `
        -Text 'Enter' `
        -Foreground $style.Foreground `
        -Background ([ConsoleColor]::Black) `
        -ForegroundRgb $style.ForegroundRgb `
        -BackgroundRgb $style.BackgroundRgb
} $commandKeyStyle
Assert-Equal "$escape[1;1H$escape[38;2;204;204;204;48;2;1;22;39mEnter" $commandKey 'a command key uses the exact body neutral'

$commandLabelStyle = Get-TerminalPresentationStyle -Role CommandLabel
$commandLabel = & $module {
    param($style)
    Get-TerminalSegmentSequence `
        -X 6 `
        -Y 0 `
        -Text 'Open' `
        -Foreground $style.Foreground `
        -Background ([ConsoleColor]::Black) `
        -ForegroundRgb $style.ForegroundRgb `
        -BackgroundRgb $style.BackgroundRgb
} $commandLabelStyle
Assert-Equal "$escape[1;7H$escape[90;48;2;1;22;39mOpen" $commandLabel 'a command label retains its muted level on the exact dark surface'

$lightHost = [pscustomobject]@{
    Color = 'Supported'
    OriginalForeground = [ConsoleColor]::DarkGray
    OriginalBackground = [ConsoleColor]::Black
    ThemeVariant = 'Light'
}
$lightFocusColors = & $module {
    param($hostState)
    Get-TerminalSegmentColors `
        -Segment ([pscustomobject]@{ PresentationRole = 'Action'; State = 'Focused' }) `
        -HostState $hostState
} $lightHost
Assert-Rgb '246,246,246' $lightFocusColors.ForegroundRgb 'an explicit light variant selects the Figma light focus foreground independently from the indexed background'
Assert-Rgb '53,53,53' $lightFocusColors.BackgroundRgb 'an explicit light variant selects the Figma light focus background independently from the indexed background'
$lightFocus = & $module {
    param($colors)
    Get-TerminalSegmentSequence `
        -X 7 `
        -Y 3 `
        -Text '  Release' `
        -Foreground $colors.Foreground `
        -Background $colors.Background `
        -ForegroundRgb $colors.ForegroundRgb `
        -BackgroundRgb $colors.BackgroundRgb
} $lightFocusColors
Assert-Equal "$escape[4;8H$escape[38;2;246;246;246;48;2;53;53;53m  Release" $lightFocus 'a light selection emits its exact inverted Figma pair'

$action = New-TerminalTarget -TargetId action.sample -Label Sample -IntentKind Action
$otherAction = New-TerminalTarget -TargetId action.other -Label Other -IntentKind Action
$section = New-TerminalSection -Label Sample -Items @(New-TerminalActionRow -Label Test -Variants @($action, $otherAction))
$view = New-TerminalActionMenuView -ViewId menu.sample -Banner Sample -Sections @($section)
$frame = Get-TerminalInteractionFrame -View $view -Width 100 -Height 12 -FocusedTargetId action.sample
$snapshot = @(ConvertTo-TerminalFrameText -Frame $frame) -join "`n"
Assert-Equal $false $snapshot.Contains([string]$escape) 'redirectable frame text contains no cursor-control sequences'

$unchangedLines = @(& $module { param($current) Get-TerminalChangedLineIndexes -Frame $current -PreviousFrame $current } $frame)
Assert-Equal 0 $unchangedLines.Count 'an identical retained frame schedules no repaint'
$otherFocusedFrame = Get-TerminalInteractionFrame -View $view -Width 100 -Height 12 -FocusedTargetId action.other
$focusLines = @(& $module { param($current, $previous) Get-TerminalChangedLineIndexes -Frame $current -PreviousFrame $previous } $otherFocusedFrame $frame)
$focusLine = @($otherFocusedFrame.Targets | Where-Object { $_.TargetId -eq 'action.other' })[0].Y
Assert-Equal 1 $focusLines.Count 'focus movement schedules only the affected frame line'
Assert-Equal $focusLine $focusLines[0] 'focus movement schedules the line that contains both target states'
$resizedFrame = Get-TerminalInteractionFrame -View $view -Width 101 -Height 12 -FocusedTargetId action.other
$resizedLines = @(& $module { param($current, $previous) Get-TerminalChangedLineIndexes -Frame $current -PreviousFrame $previous } $resizedFrame $otherFocusedFrame)
Assert-Equal $resizedFrame.Lines.Count $resizedLines.Count 'a geometry change schedules a complete redraw'

Write-Host 'host.tests.ps1: PASS' -ForegroundColor Green
