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

function Get-SegmentColors([string]$Role, [string]$State, [object]$HostState) {
    & $module {
        param($role, $state, $hostState)
        Get-TerminalSegmentColors `
            -Segment ([pscustomobject]@{ PresentationRole = $role; State = $state }) `
            -HostState $hostState
    } $Role $State $HostState
}

function Get-SegmentSequence([int]$X, [int]$Y, [string]$Text, [object]$Colors) {
    & $module {
        param($x, $y, $text, $colors)
        Get-TerminalSegmentSequence `
            -X $x `
            -Y $y `
            -Text $text `
            -Foreground $colors.Foreground `
            -Background $colors.Background `
            -ForegroundRgb $colors.ForegroundRgb `
            -BackgroundRgb $colors.BackgroundRgb
    } $X $Y $Text $Colors
}

# The Terminal palette, the default, sends console colours only.
$terminalHost = [pscustomobject]@{
    Color = 'Supported'
    OriginalForeground = [ConsoleColor]::Gray
    OriginalBackground = [ConsoleColor]::Black
    Palette = 'Terminal'
}
$unnamedHost = [pscustomobject]@{
    Color = 'Supported'
    OriginalForeground = [ConsoleColor]::Gray
    OriginalBackground = [ConsoleColor]::White
}
foreach ($hostState in @($terminalHost, $unnamedHost)) {
    $sectionColors = Get-SegmentColors -Role Section -State Normal -HostState $hostState
    Assert-Equal ([ConsoleColor]::Magenta) $sectionColors.Foreground 'a Terminal Section takes its console colour'
    Assert-Equal $null $sectionColors.ForegroundRgb 'a Terminal Section names no RGB foreground'
    Assert-Equal $null $sectionColors.BackgroundRgb 'a Terminal Section names no RGB background'
}
$terminalSection = Get-SegmentSequence -X 1 -Y 4 -Text 'RUN ' -Colors (Get-SegmentColors -Role Section -State Normal -HostState $terminalHost)
Assert-Equal "$escape[5;2H$escape[95;40mRUN " $terminalSection 'a Terminal Section emits sixteen-colour attributes and text in one VT sequence'
$terminalActionFocus = Get-SegmentSequence -X 7 -Y 3 -Text '> Launch' -Colors (Get-SegmentColors -Role Action -State Focused -HostState $terminalHost)
Assert-Equal "$escape[4;8H$escape[30;47m> Launch" $terminalActionFocus 'a focused Action emits cursor, classic selection attributes, and content as one VT sequence'
$terminalExitFocus = Get-SegmentSequence -X 7 -Y 3 -Text '  Quit' -Colors (Get-SegmentColors -Role Exit -State Focused -HostState $terminalHost)
Assert-Equal "$escape[4;8H$escape[30;47m  Quit" $terminalExitFocus 'a focused Exit uses the classic ordinary selection treatment'
$terminalDangerFocus = Get-SegmentSequence -X 7 -Y 3 -Text '  Reset' -Colors (Get-SegmentColors -Role Danger -State Focused -HostState $terminalHost)
Assert-Equal "$escape[4;8H$escape[97;41m  Reset" $terminalDangerFocus 'a focused Danger target inverts to white on dark red'
$terminalCommandKey = Get-SegmentSequence -X 0 -Y 0 -Text 'Enter' -Colors (Get-SegmentColors -Role CommandKey -State Normal -HostState $terminalHost)
Assert-Equal "$escape[1;1H$escape[37;40mEnter" $terminalCommandKey 'a command key remains distinct from its muted command label'
$terminalCommandLabel = Get-SegmentSequence -X 6 -Y 0 -Text 'Open' -Colors (Get-SegmentColors -Role CommandLabel -State Normal -HostState $terminalHost)
Assert-Equal "$escape[1;7H$escape[90;40mOpen" $terminalCommandLabel 'a command label emits the classic muted foreground through the VT stream'

# The Deckle palette sends its exact 24-bit tokens, console colours kept for a legacy host.
$darkHost = [pscustomobject]@{
    Color = 'Supported'
    OriginalForeground = [ConsoleColor]::Gray
    OriginalBackground = [ConsoleColor]::Black
    Palette = 'DeckleDark'
}
foreach ($expectedRoleColor in @(
    [pscustomobject]@{ Role = 'Section'; Color = [ConsoleColor]::Magenta; Rgb = '199,146,234' },
    [pscustomobject]@{ Role = 'ActionLabel'; Color = [ConsoleColor]::Cyan; Rgb = '119,207,192' },
    [pscustomobject]@{ Role = 'Access'; Color = [ConsoleColor]::DarkYellow; Rgb = '186,217,115' }
)) {
    $resolvedColors = Get-SegmentColors -Role $expectedRoleColor.Role -State Normal -HostState $darkHost
    Assert-Equal $expectedRoleColor.Color $resolvedColors.Foreground "$($expectedRoleColor.Role) retains its legacy renderer fallback"
    Assert-Equal ([ConsoleColor]::Black) $resolvedColors.Background "$($expectedRoleColor.Role) retains the host background fallback"
    Assert-Rgb $expectedRoleColor.Rgb $resolvedColors.ForegroundRgb "$($expectedRoleColor.Role) exact dark token reaches the VT renderer"
    Assert-Rgb '1,22,39' $resolvedColors.BackgroundRgb "$($expectedRoleColor.Role) exact dark surface reaches the VT renderer"
}
$darkSection = Get-SegmentSequence -X 1 -Y 4 -Text 'RUN ' -Colors (Get-SegmentColors -Role Section -State Normal -HostState $darkHost)
Assert-Equal "$escape[5;2H$escape[38;2;199;146;234;48;2;1;22;39mRUN " $darkSection 'a Section emits its exact dark attributes and text in one VT sequence'
$darkActionFocus = Get-SegmentSequence -X 7 -Y 3 -Text '> Launch' -Colors (Get-SegmentColors -Role Action -State Focused -HostState $darkHost)
Assert-Equal "$escape[4;8H$escape[38;2;1;22;39;48;2;255;255;255m> Launch" $darkActionFocus 'a focused Action emits the exact inverted dark selection in one VT sequence'
foreach ($role in @('Exit', 'Danger')) {
    $darkFocus = Get-SegmentSequence -X 7 -Y 3 -Text '  Quit' -Colors (Get-SegmentColors -Role $role -State Focused -HostState $darkHost)
    Assert-Equal "$escape[4;8H$escape[38;2;1;22;39;48;2;255;255;255m  Quit" $darkFocus "a focused $role uses the same exact selection treatment as every target"
}
$darkCommandKey = Get-SegmentSequence -X 0 -Y 0 -Text 'Enter' -Colors (Get-SegmentColors -Role CommandKey -State Normal -HostState $darkHost)
Assert-Equal "$escape[1;1H$escape[38;2;204;204;204;48;2;1;22;39mEnter" $darkCommandKey 'a command key uses the exact body neutral'
$darkCommandLabel = Get-SegmentSequence -X 6 -Y 0 -Text 'Open' -Colors (Get-SegmentColors -Role CommandLabel -State Normal -HostState $darkHost)
Assert-Equal "$escape[1;7H$escape[90;48;2;1;22;39mOpen" $darkCommandLabel 'a command label retains its muted level on the exact dark surface'

# The host resolves the variant once; the renderer applies it as given.
$lightHost = [pscustomobject]@{
    Color = 'Supported'
    OriginalForeground = [ConsoleColor]::DarkGray
    OriginalBackground = [ConsoleColor]::Black
    Palette = 'DeckleLight'
}
$lightFocusColors = Get-SegmentColors -Role Action -State Focused -HostState $lightHost
Assert-Rgb '246,246,246' $lightFocusColors.ForegroundRgb 'a resolved light palette selects the Figma light focus foreground'
Assert-Rgb '53,53,53' $lightFocusColors.BackgroundRgb 'a resolved light palette selects the Figma light focus background'
$lightFocus = Get-SegmentSequence -X 7 -Y 3 -Text '  Release' -Colors $lightFocusColors
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
