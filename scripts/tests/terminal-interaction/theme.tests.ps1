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

function Assert-Rgb([string]$Expected, $Actual, [string]$Case) {
    Assert-Equal $Expected (@($Actual) -join ',') $Case
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

$module = Get-Module terminal-interaction
Assert-Equal 'Dark' (& $module { Resolve-TerminalThemeVariant -BackgroundColor ([ConsoleColor]::Black) }) 'Auto treats an indexed dark background as dark'
Assert-Equal 'Light' (& $module { Resolve-TerminalThemeVariant -BackgroundColor ([ConsoleColor]::White) }) 'Auto treats an indexed light background as light'

$palette = @{
    Banner = [ConsoleColor]::Blue
    Context = [ConsoleColor]::DarkGray
    Section = [ConsoleColor]::Magenta
    SectionSeparator = [ConsoleColor]::Gray
    ActionLabel = [ConsoleColor]::Cyan
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
    Assert-Equal $entry.Value $style.Foreground "the Deckle theme retains a legacy fallback for $($entry.Key)"
}
Assert-Equal $null (Get-TerminalPresentationStyle -Role Body).Foreground 'Body content keeps the host foreground as its legacy fallback'
Assert-Equal $null (Get-TerminalPresentationStyle -Role ActionVariant).Foreground 'Action Variants keep the host foreground as their legacy fallback'
Assert-Equal $null (Get-TerminalPresentationStyle -Role Action).Foreground 'plain Actions read as body text, as in the launcher'
Assert-Equal $null (Get-TerminalPresentationStyle -Role Adjust).Foreground 'adjustments keep the host foreground as their legacy fallback'

$figmaRoles = @(
    [pscustomobject]@{ Role = 'Banner'; Dark = '130,170,255'; Light = '40,142,215' },
    [pscustomobject]@{ Role = 'Section'; Dark = '199,146,234'; Light = '192,60,124' },
    [pscustomobject]@{ Role = 'PanelTitle'; Dark = '199,146,234'; Light = '192,60,124' },
    [pscustomobject]@{ Role = 'ActionLabel'; Dark = '119,207,192'; Light = '29,117,109' },
    [pscustomobject]@{ Role = 'Access'; Dark = '186,217,115'; Light = '106,138,32' },
    [pscustomobject]@{ Role = 'Body'; Dark = '204,204,204'; Light = '83,83,83' },
    [pscustomobject]@{ Role = 'ActionVariant'; Dark = '204,204,204'; Light = '83,83,83' },
    [pscustomobject]@{ Role = 'Action'; Dark = '204,204,204'; Light = '83,83,83' },
    [pscustomobject]@{ Role = 'Adjust'; Dark = '204,204,204'; Light = '83,83,83' },
    [pscustomobject]@{ Role = 'Navigation'; Dark = '204,204,204'; Light = '83,83,83' },
    [pscustomobject]@{ Role = 'CommandKey'; Dark = '204,204,204'; Light = '83,83,83' },
    [pscustomobject]@{ Role = 'SectionSeparator'; Dark = '255,255,255'; Light = '53,53,53' },
    [pscustomobject]@{ Role = 'PanelSeparator'; Dark = '204,204,204'; Light = '83,83,83' }
)
foreach ($expectedRole in $figmaRoles) {
    $darkStyle = Get-TerminalPresentationStyle -Role $expectedRole.Role -ThemeVariant Dark
    $lightStyle = Get-TerminalPresentationStyle -Role $expectedRole.Role -ThemeVariant Light
    Assert-Rgb $expectedRole.Dark $darkStyle.ForegroundRgb "$($expectedRole.Role) uses the Figma dark token"
    Assert-Rgb $expectedRole.Light $lightStyle.ForegroundRgb "$($expectedRole.Role) uses the Figma light token"
    Assert-Rgb '1,22,39' $darkStyle.BackgroundRgb "$($expectedRole.Role) uses the Figma dark surface"
    Assert-Rgb '246,246,246' $lightStyle.BackgroundRgb "$($expectedRole.Role) uses the Figma light surface"
}
foreach ($mutedRole in @('Context', 'Supporting', 'PrimarySeparator', 'PagingSeparator', 'Separator', 'CommandLabel')) {
    $mutedStyle = Get-TerminalPresentationStyle -Role $mutedRole
    Assert-Equal $null $mutedStyle.ForegroundRgb "$mutedRole keeps its distinct muted neutral"
}
$enabledNavigation = Get-TerminalPresentationStyle -Role Navigation
$disabledNavigation = Get-TerminalPresentationStyle -Role Navigation -State Disabled
Assert-Equal $false ($null -eq $enabledNavigation.ForegroundRgb) 'enabled Navigation keeps the body-neutral level'
Assert-Equal $null $disabledNavigation.ForegroundRgb 'disabled Navigation drops to the muted level'

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
    'SectionSeparator', 'Action', 'ActionVariant', 'Adjust', 'Navigation',
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
Assert-Equal $false ((@((Get-TerminalPresentationStyle -Role Section).ForegroundRgb) -join ',') -eq (@((Get-TerminalPresentationStyle -Role ActionLabel).ForegroundRgb) -join ',')) 'Section and Action subjects use distinct hierarchy colors'
Assert-Equal $false ((@((Get-TerminalPresentationStyle -Role ActionLabel).ForegroundRgb) -join ',') -eq (@((Get-TerminalPresentationStyle -Role Access).ForegroundRgb) -join ',')) 'Action subjects and Accesses use distinct hierarchy colors'
Assert-Equal $false ((@((Get-TerminalPresentationStyle -Role Section).ForegroundRgb) -join ',') -eq (@((Get-TerminalPresentationStyle -Role Access).ForegroundRgb) -join ',')) 'Section and Accesses use distinct hierarchy colors'

$actionFocus = Get-TerminalPresentationStyle -Role Action -State Focused -ThemeVariant Dark
Assert-Equal ([ConsoleColor]::Black) $actionFocus.Foreground 'focused Actions have strong foreground contrast'
Assert-Equal ([ConsoleColor]::White) $actionFocus.Background 'focused Actions have a high-contrast legacy fallback'
Assert-Rgb '1,22,39' $actionFocus.ForegroundRgb 'dark focus reuses the dark surface as its text color'
Assert-Rgb '255,255,255' $actionFocus.BackgroundRgb 'dark focus uses the Figma selection surface'
$accessFocus = Get-TerminalPresentationStyle -Role Access -State Focused -ThemeVariant Dark
Assert-Equal ([ConsoleColor]::Black) $accessFocus.Foreground 'focused Accesses have strong foreground contrast'
Assert-Equal ([ConsoleColor]::White) $accessFocus.Background 'focused Accesses share the uniform selection fallback'
$navigationFocus = Get-TerminalPresentationStyle -Role Navigation -State Focused -ThemeVariant Dark
Assert-Equal ([ConsoleColor]::Black) $navigationFocus.Foreground 'focused Navigation has strong foreground contrast'
Assert-Equal ([ConsoleColor]::White) $navigationFocus.Background 'focused Navigation shares the uniform selection fallback'
$dangerFocus = Get-TerminalPresentationStyle -Role Danger -State Focused -ThemeVariant Dark
Assert-Equal ([ConsoleColor]::Black) $dangerFocus.Foreground 'focused danger uses the same foreground as every selection'
Assert-Equal ([ConsoleColor]::White) $dangerFocus.Background 'focused danger uses the same background as every selection'
Assert-Rgb '1,22,39' $dangerFocus.ForegroundRgb 'focused danger uses the uniform dark-theme text token'
Assert-Rgb '255,255,255' $dangerFocus.BackgroundRgb 'focused danger uses the uniform dark-theme selection token'
$exit = Get-TerminalPresentationStyle -Role Exit
Assert-Equal ([ConsoleColor]::Red) $exit.Foreground 'Exit remains visibly exceptional at rest'
$exitFocus = Get-TerminalPresentationStyle -Role Exit -State Focused -ThemeVariant Dark
Assert-Equal ([ConsoleColor]::Black) $exitFocus.Foreground 'focused Exit uses the classic ordinary selection foreground'
Assert-Equal ([ConsoleColor]::White) $exitFocus.Background 'focused Exit uses the ordinary selection background'
$lightFocus = Get-TerminalPresentationStyle -Role Action -State Focused -ThemeVariant Light
Assert-Equal ([ConsoleColor]::White) $lightFocus.Foreground 'light focus has a high-contrast legacy foreground'
Assert-Equal ([ConsoleColor]::DarkGray) $lightFocus.Background 'light focus has a high-contrast legacy background'
Assert-Rgb '246,246,246' $lightFocus.ForegroundRgb 'light focus reuses the light surface as its text color'
Assert-Rgb '53,53,53' $lightFocus.BackgroundRgb 'light focus uses the Figma selection surface'
$noColor = Get-TerminalPresentationStyle -Role Danger -State Focused -ColorCapability Unsupported
Assert-Equal $null $noColor.Foreground 'no-color mode emits no foreground dependency'
Assert-Equal $null $noColor.Background 'no-color mode emits no background dependency'
Assert-Equal $null $noColor.ForegroundRgb 'no-color mode emits no RGB foreground dependency'
Assert-Equal $null $noColor.BackgroundRgb 'no-color mode emits no RGB background dependency'

$root = Get-DecklePreviewRootView
$rootFrame = Get-TerminalInteractionFrame -View $root -Width 100 -Height 24 -FocusedTargetId action.launch.release
Assert-Equal Banner (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text -eq 'Deckle Interaction Preview' } -Case 'banner').PresentationRole 'the repository banner keeps its semantic role'
Assert-Equal Section (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text -eq 'RUN ' } -Case 'Run Section').PresentationRole 'Section titles are categories'
Assert-Equal SectionSeparator (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.PresentationRole -eq 'SectionSeparator' -and $s.Text.Length -gt 90 } -Case 'Run Section separator').PresentationRole 'Section hierarchy owns its dashed separator'
Assert-Equal ActionLabel (Get-Segment -Frame $rootFrame -Predicate { param($s) $s.Text -eq 'Launch' } -Case 'Action subject').PresentationRole 'Action Row subjects are distinct from their variants'
Assert-Equal $false ((Get-TerminalPresentationStyle -Role ActionLabel).Foreground -eq (Get-TerminalPresentationStyle -Role Action).Foreground) 'Action Row subjects and Action targets use distinct colors'
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
Assert-Equal Action (Get-Segment -Frame $projectFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '  README pulse' } -Case 'grouped independent Action').PresentationRole 'independent Actions in a compact group keep their Action role'

$maintenanceFrame = Get-TerminalInteractionFrame -View (Get-DecklePreviewMaintenanceView) -Width 100 -Height 30 -FocusedTargetId navigation.back
Assert-Equal Danger (Get-Segment -Frame $maintenanceFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '  Reset AI session state' } -Case 'danger Action').PresentationRole 'destructive grouped Actions retain danger independently from activation'
$statisticsGroup = Get-Segment -Frame $maintenanceFrame -Predicate { param($s) $s.Text -eq 'Statistics' } -Case 'compact Action Group'
Assert-Equal ActionLabel $statisticsGroup.PresentationRole 'a compact Action Group label titles its row as an Action Row label does'
$repositoryStatistics = Get-Segment -Frame $maintenanceFrame -Predicate { param($s) $s.Text.TrimEnd() -eq '  Repository statistics' } -Case 'grouped Action'
Assert-Equal Action $repositoryStatistics.PresentationRole 'grouped Actions stay distinct from Action Variants'

$executionFrame = Get-TerminalInteractionFrame -View (Get-DecklePreviewSnapshotView -Name Execution) -OwnerActionMenu $root -Width 120 -Height 24 -FocusedTargetId navigation.back -JournalOffset ([int]::MaxValue)
Assert-Equal PanelTitle (Get-Segment -Frame $executionFrame -Predicate { param($s) $s.Text -eq 'Execution Journal' } -Case 'Journal Panel title').PresentationRole 'Panel titles share the category hierarchy'
Assert-Equal Success (Get-Segment -Frame $executionFrame -Predicate { param($s) $s.Text -match '^Result:' } -Case 'Execution Result').PresentationRole 'a completed Execution Result carries success semantics'
Assert-Equal Supporting (Get-Segment -Frame $executionFrame -Predicate { param($s) $s.Text -eq '[ok] Accept intent' } -Case 'completed Tracking step').PresentationRole 'completed Tracking steps stay neutral beside the final Result'

Write-Host 'theme.tests.ps1: PASS' -ForegroundColor Green
