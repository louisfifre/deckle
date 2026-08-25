$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
Import-Module (Join-Path $LibDir 'terminal-interaction.psm1') -Force
. (Join-Path $LibDir 'deckle-preview\catalog.ps1')
. (Join-Path $LibDir 'deckle-preview\statistics-preparation.ps1')
. (Join-Path $LibDir 'deckle-preview\flows.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected '$Expected', got '$Actual'" }
}

function Get-Segment {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][scriptblock]$Predicate,
        [Parameter(Mandatory)][string]$Case
    )

    $matches = @(
        foreach ($line in $Frame.Lines) {
            foreach ($segment in $line.Segments) {
                if (& $Predicate $segment) { $segment }
            }
        }
    )
    if ($matches.Count -ne 1) { throw "${Case}: expected one segment, got $($matches.Count)" }
    return $matches[0]
}

$palette = @{
    Banner = [ConsoleColor]::Blue
    Context = [ConsoleColor]::DarkGray
    Section = [ConsoleColor]::Magenta
    SectionSeparator = [ConsoleColor]::Gray
    Action = [ConsoleColor]::Cyan
    Access = [ConsoleColor]::DarkYellow
    Navigation = [ConsoleColor]::DarkGray
    Exit = [ConsoleColor]::Red
    Danger = [ConsoleColor]::Red
    PanelTitle = [ConsoleColor]::Magenta
    PrimarySeparator = [ConsoleColor]::DarkGray
    PanelSeparator = [ConsoleColor]::Gray
    PagingSeparator = [ConsoleColor]::DarkGray
    CommandKey = [ConsoleColor]::Gray
    CommandLabel = [ConsoleColor]::DarkGray
    Success = [ConsoleColor]::Green
    Warning = [ConsoleColor]::Yellow
    Error = [ConsoleColor]::Red
}
foreach ($entry in $palette.GetEnumerator()) {
    $style = Get-TerminalPresentationStyle -Role $entry.Key
    Assert-Equal $entry.Value $style.Foreground "the Deckle theme maps $($entry.Key) semantically"
}
Assert-Equal $null (Get-TerminalPresentationStyle -Role Body).Foreground 'Body content preserves the host foreground'
Assert-Equal $null (Get-TerminalPresentationStyle -Role ActionVariant).Foreground 'Action Variants preserve the host foreground'
Assert-Equal $null (Get-TerminalPresentationStyle -Role Adjust).Foreground 'adjustments preserve the host foreground'

$chromaticColors = @(
    [ConsoleColor]::DarkBlue,
    [ConsoleColor]::DarkGreen,
    [ConsoleColor]::DarkCyan,
    [ConsoleColor]::DarkRed,
    [ConsoleColor]::DarkMagenta,
    [ConsoleColor]::DarkYellow,
    [ConsoleColor]::Blue,
    [ConsoleColor]::Green,
    [ConsoleColor]::Cyan,
    [ConsoleColor]::Red,
    [ConsoleColor]::Magenta,
    [ConsoleColor]::Yellow
)
$neutralRoles = @(
    'SectionSeparator', 'ActionVariant', 'Adjust', 'Navigation',
    'Body', 'Context', 'Supporting', 'PrimarySeparator', 'PanelSeparator',
    'PagingSeparator', 'Separator', 'CommandKey', 'CommandLabel'
)
foreach ($role in $neutralRoles) {
    foreach ($state in @('Normal', 'Focused', 'Disabled')) {
        $style = Get-TerminalPresentationStyle -Role $role -State $state
        Assert-Equal $false ($style.Foreground -in $chromaticColors) "$role $state foreground preserves the grayscale hierarchy"
        Assert-Equal $false ($style.Background -in $chromaticColors) "$role $state background preserves the grayscale hierarchy"
    }
}
foreach ($role in @('Banner', 'Section', 'PanelTitle', 'Danger', 'Exit', 'Success', 'Warning', 'Error')) {
    $disabledStyle = Get-TerminalPresentationStyle -Role $role -State Disabled
    Assert-Equal ([ConsoleColor]::DarkGray) $disabledStyle.Foreground "$role becomes muted when disabled"
    Assert-Equal $null $disabledStyle.Background "$role disabled state does not retain a chromatic background"
}
Assert-Equal $false ((Get-TerminalPresentationStyle -Role Section).Foreground -eq (Get-TerminalPresentationStyle -Role Action).Foreground) 'Section and Action subjects use distinct hierarchy colors'
Assert-Equal $false ((Get-TerminalPresentationStyle -Role Action).Foreground -eq (Get-TerminalPresentationStyle -Role Access).Foreground) 'Action subjects and Accesses use distinct hierarchy colors'
Assert-Equal $false ((Get-TerminalPresentationStyle -Role Section).Foreground -eq (Get-TerminalPresentationStyle -Role Access).Foreground) 'Section and Accesses use distinct hierarchy colors'

$actionFocus = Get-TerminalPresentationStyle -Role Action -State Focused
Assert-Equal ([ConsoleColor]::Black) $actionFocus.Foreground 'focused Actions have strong foreground contrast'
Assert-Equal ([ConsoleColor]::Gray) $actionFocus.Background 'focused Actions use the classic selection background'
$accessFocus = Get-TerminalPresentationStyle -Role Access -State Focused
Assert-Equal ([ConsoleColor]::Black) $accessFocus.Foreground 'focused Accesses have strong foreground contrast'
Assert-Equal ([ConsoleColor]::Gray) $accessFocus.Background 'focused Accesses use the classic selection background'
$navigationFocus = Get-TerminalPresentationStyle -Role Navigation -State Focused
Assert-Equal ([ConsoleColor]::Black) $navigationFocus.Foreground 'focused Navigation has strong foreground contrast'
Assert-Equal ([ConsoleColor]::Gray) $navigationFocus.Background 'focused Navigation uses the classic selection background'
$dangerFocus = Get-TerminalPresentationStyle -Role Danger -State Focused
Assert-Equal ([ConsoleColor]::White) $dangerFocus.Foreground 'focused danger uses the classic contrasting foreground'
Assert-Equal ([ConsoleColor]::DarkRed) $dangerFocus.Background 'focused danger uses the classic danger background'
$exit = Get-TerminalPresentationStyle -Role Exit
Assert-Equal ([ConsoleColor]::Red) $exit.Foreground 'Exit remains visibly exceptional at rest'
$exitFocus = Get-TerminalPresentationStyle -Role Exit -State Focused
Assert-Equal ([ConsoleColor]::Black) $exitFocus.Foreground 'focused Exit uses the classic ordinary selection foreground'
Assert-Equal ([ConsoleColor]::Gray) $exitFocus.Background 'focused Exit uses the classic ordinary selection background'
$noColor = Get-TerminalPresentationStyle -Role Danger -State Focused -ColorCapability Unsupported
Assert-Equal $null $noColor.Foreground 'no-color mode emits no foreground dependency'
Assert-Equal $null $noColor.Background 'no-color mode emits no background dependency'

$root = Get-DecklePreviewRootView
$rootFrame = Get-TerminalInteractionFrame -View $root -Width 100 -Height 24 -FocusedTargetId action.launch.release
Assert-Equal Banner (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text -eq 'Deckle Interaction Preview' } -Case 'banner').PresentationRole 'the repository banner keeps its semantic role'
Assert-Equal Section (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text -eq 'RUN ' } -Case 'Run Section').PresentationRole 'Section titles are categories'
Assert-Equal SectionSeparator (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.PresentationRole -eq 'SectionSeparator' -and $s.Text.Length -gt 90 } -Case 'Run Section separator').PresentationRole 'Section hierarchy owns its dashed separator'
Assert-Equal Action (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text -eq 'Launch' } -Case 'Action subject').PresentationRole 'Action Row subjects are distinct from their variants'
$focusedVariant = Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '  Release' -and $s.State -eq 'Focused' } -Case 'focused Action Variant'
Assert-Equal ActionVariant $focusedVariant.PresentationRole 'Action Variants keep their own semantic role'
Assert-Equal Focused $focusedVariant.State 'focus is an independent state overlay'
Assert-Equal ' ' $focusedVariant.Text.Substring(0, 1) 'color focus keeps the marker column quiet'
$focusedPlacement = @($rootFrame.Targets | Where-Object { $_.TargetId -eq 'action.launch.release' })[0]
Assert-Equal $focusedPlacement.Width $focusedVariant.Text.Length 'focus paints the complete stable grid cell rather than only its label'
$noColorFrame = Get-TerminalInteractionFrame -View $root -Width 100 -Height 24 -FocusedTargetId action.launch.release -ColorCapability Unsupported
$noColorFocus = Get-Segment -Frame $noColorFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '> Release' } -Case 'no-color focused Action Variant'
Assert-Equal '>' $noColorFocus.Text.Substring(0, 1) 'no-color focus uses the reserved marker column'
$ellipsis = [string][char]0x2026
Assert-Equal Access (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text.TrimEnd() -eq ('  Project' + $ellipsis) } -Case 'Project Access').PresentationRole 'Accesses retain their disclosure role'
Assert-Equal $true (@($root.Sections | ForEach-Object { $_.Items } | Where-Object { $_.IntentKind -eq 'Access' -and -not $_.Label.EndsWith($ellipsis) }).Count -eq 0) 'every Access label carries the disclosure ellipsis'
Assert-Equal Exit (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '  Quit' } -Case 'Quit command').PresentationRole 'Quit retains its exceptional exit role'

$projectFrame = Get-TerminalInteractionFrame -View (Get-DecklePreviewProjectView) -Width 100 -Height 24 -FocusedTargetId navigation.back
Assert-Equal Context (Get-Segment -Frame $projectFrame -Predicate { param($s) $s.Text -eq 'Project' } -Case 'View context').PresentationRole 'the View context owns its stable Header rail'
Assert-Equal Navigation (Get-Segment -Frame $projectFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '  < Back' } -Case 'Back Navigation Control').PresentationRole 'Back carries its direction independently from focus'
Assert-Equal Action (Get-Segment -Frame $projectFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '  README pulse' } -Case 'standalone Action').PresentationRole 'standalone Actions retain the Action hierarchy'

$maintenanceFrame = Get-TerminalInteractionFrame -View (Get-DecklePreviewMaintenanceView) -Width 100 -Height 30 -FocusedTargetId navigation.back
Assert-Equal Danger (Get-Segment -Frame $maintenanceFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '  Reset' } -Case 'danger Action').PresentationRole 'destructive Actions retain danger independently from activation'

$executionFrame = Get-TerminalInteractionFrame -View (Get-DecklePreviewSnapshotView -Name Execution) -OwnerActionMenu $root -Width 120 -Height 24 -FocusedTargetId navigation.back -JournalOffset ([int]::MaxValue)
Assert-Equal PanelTitle (Get-Segment -Frame $executionFrame -Predicate { param($s) $s.Text -eq 'Execution Journal' } -Case 'Journal Panel title').PresentationRole 'Panel titles share the category hierarchy'
Assert-Equal Success (Get-Segment -Frame $executionFrame -Predicate { param($s) $s.Text -match '^Result:' } -Case 'Execution Result').PresentationRole 'a completed Execution Result carries success semantics'
Assert-Equal Supporting (Get-Segment -Frame $executionFrame -Predicate { param($s) $s.Text -eq '[ok] Accept intent' } -Case 'completed Tracking step').PresentationRole 'completed Tracking steps stay neutral beside the final Result'

Write-Host 'theme.tests.ps1: PASS' -ForegroundColor Green
