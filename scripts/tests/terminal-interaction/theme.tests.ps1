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
    Adjust = [ConsoleColor]::DarkYellow
    Navigation = [ConsoleColor]::DarkGray
    Exit = [ConsoleColor]::Red
    Danger = [ConsoleColor]::Red
    PanelTitle = [ConsoleColor]::Magenta
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
Assert-Equal $null (Get-TerminalPresentationStyle -Role ActionVariant).Foreground 'Action Variants inherit the terminal body color'
Assert-Equal $null (Get-TerminalPresentationStyle -Role Body).Foreground 'Body content preserves the host foreground'

$focus = Get-TerminalPresentationStyle -Role Access -State Focused
Assert-Equal ([ConsoleColor]::Black) $focus.Foreground 'ordinary focus has strong foreground contrast'
Assert-Equal ([ConsoleColor]::Gray) $focus.Background 'ordinary focus has a visible background'
$dangerFocus = Get-TerminalPresentationStyle -Role Danger -State Focused
Assert-Equal ([ConsoleColor]::White) $dangerFocus.Foreground 'focused danger preserves readable foreground contrast'
Assert-Equal ([ConsoleColor]::DarkRed) $dangerFocus.Background 'focused danger preserves danger semantics'
$noColor = Get-TerminalPresentationStyle -Role Danger -State Focused -ColorCapability Unsupported
Assert-Equal $null $noColor.Foreground 'no-color mode emits no foreground dependency'
Assert-Equal $null $noColor.Background 'no-color mode emits no background dependency'

$root = Get-DecklePreviewRootView
$rootFrame = Get-TerminalInteractionFrame -View $root -Width 100 -Height 24 -FocusedTargetId action.launch.release
Assert-Equal Banner (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text -eq 'Deckle Interaction Preview' } -Case 'banner').PresentationRole 'the repository banner keeps its semantic role'
Assert-Equal Section (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text -eq 'RUN ' } -Case 'Run Section').PresentationRole 'Section titles are categories'
Assert-Equal SectionSeparator (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.PresentationRole -eq 'SectionSeparator' -and $s.Text.Length -gt 90 } -Case 'Run Section separator').PresentationRole 'Section hierarchy owns its dashed separator'
Assert-Equal Action (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text -eq 'Launch' } -Case 'Action subject').PresentationRole 'Action Row subjects are distinct from their variants'
$focusedVariant = Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '> Release' } -Case 'focused Action Variant'
Assert-Equal ActionVariant $focusedVariant.PresentationRole 'Action Variants keep their own semantic role'
Assert-Equal Focused $focusedVariant.State 'focus is an independent state overlay'
Assert-Equal '>' $focusedVariant.Text.Substring(0, 1) 'focus remains structurally visible without color'
$focusedPlacement = @($rootFrame.Targets | Where-Object { $_.TargetId -eq 'action.launch.release' })[0]
Assert-Equal $focusedPlacement.Width $focusedVariant.Text.Length 'focus paints the complete stable grid cell rather than only its label'
Assert-Equal Access (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '  Project' } -Case 'Project Access').PresentationRole 'Accesses retain their disclosure role'
Assert-Equal Exit (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '  Quit' } -Case 'Quit command').PresentationRole 'Quit retains its exceptional exit role'

$projectFrame = Get-TerminalInteractionFrame -View (Get-DecklePreviewProjectView) -Width 100 -Height 24 -FocusedTargetId navigation.back
Assert-Equal Context (Get-Segment -Frame $projectFrame -Predicate { param($s) $s.Text -eq ' / Project' } -Case 'View context').PresentationRole 'the View context is visually subordinate to the banner'
Assert-Equal Navigation (Get-Segment -Frame $projectFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '> Back' } -Case 'Back Navigation Control').PresentationRole 'Back is navigation rather than an Action'
Assert-Equal Action (Get-Segment -Frame $projectFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '  README pulse' } -Case 'standalone Action').PresentationRole 'standalone Actions retain the Action hierarchy'

$maintenanceFrame = Get-TerminalInteractionFrame -View (Get-DecklePreviewMaintenanceView) -Width 100 -Height 30 -FocusedTargetId navigation.back
Assert-Equal Danger (Get-Segment -Frame $maintenanceFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '  Reset' } -Case 'danger Action').PresentationRole 'destructive Actions retain danger independently from activation'

$executionFrame = Get-TerminalInteractionFrame -View (Get-DecklePreviewSnapshotView -Name Execution) -Width 120 -Height 24 -FocusedTargetId navigation.back -JournalOffset ([int]::MaxValue)
Assert-Equal PanelTitle (Get-Segment -Frame $executionFrame -Predicate { param($s) $s.Text -eq 'Execution Journal' } -Case 'Journal Panel title').PresentationRole 'Panel titles share the category hierarchy'
Assert-Equal Success (Get-Segment -Frame $executionFrame -Predicate { param($s) $s.Text -match '^Result:' } -Case 'Execution Result').PresentationRole 'a completed Execution Result carries success semantics'

Write-Host 'theme.tests.ps1: PASS' -ForegroundColor Green
