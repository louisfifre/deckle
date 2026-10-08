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
Assert-Equal 'Dark' (& $module { Resolve-TerminalThemeVariant -BackgroundColor ([ConsoleColor]::Black) }) 'an indexed dark background reads as dark'
Assert-Equal 'Light' (& $module { Resolve-TerminalThemeVariant -BackgroundColor ([ConsoleColor]::White) }) 'an indexed light background reads as light'
Assert-Equal 'Terminal' (& $module { Resolve-TerminalPalette -Palette Terminal -BackgroundColor ([ConsoleColor]::White) }) 'the Terminal palette ignores the console background'
Assert-Equal 'DeckleDark' (& $module { Resolve-TerminalPalette -Palette Deckle -BackgroundColor ([ConsoleColor]::Black) }) 'the Deckle palette takes its dark variant on a dark background'
Assert-Equal 'DeckleLight' (& $module { Resolve-TerminalPalette -Palette Deckle -BackgroundColor ([ConsoleColor]::White) }) 'the Deckle palette takes its light variant on a light background'

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
    Assert-Equal $entry.Value $style.Foreground "the Terminal palette maps $($entry.Key) to its console colour"
    $designedStyle = Get-TerminalPresentationStyle -Role $entry.Key -Palette DeckleDark
    Assert-Equal $entry.Value $designedStyle.Foreground "the Deckle palette keeps the same console colour for $($entry.Key) as its legacy fallback"
}
foreach ($role in @('Body', 'Action', 'ActionVariant', 'Adjust')) {
    Assert-Equal $null (Get-TerminalPresentationStyle -Role $role).Foreground "$role reads as body text in the host foreground"
}

# The Terminal palette is the default and never names an absolute colour.
foreach ($role in @($palette.Keys) + @('Body', 'Action', 'ActionVariant', 'Adjust', 'Supporting', 'Separator')) {
    foreach ($state in @('Normal', 'Focused', 'Disabled')) {
        $style = Get-TerminalPresentationStyle -Role $role -State $state
        Assert-Equal $null $style.ForegroundRgb "the default palette emits no RGB foreground for $role $state"
        Assert-Equal $null $style.BackgroundRgb "the default palette emits no RGB background for $role $state"
    }
}

$figmaRoles = @(
    [pscustomobject]@{ Role = 'Banner'; Dark = '130,170,255'; Light = '40,142,215' },
    [pscustomobject]@{ Role = 'Section'; Dark = '199,146,234'; Light = '192,60,124' },
    [pscustomobject]@{ Role = 'PanelTitle'; Dark = '199,146,234'; Light = '192,60,124' },
    [pscustomobject]@{ Role = 'ActionLabel'; Dark = '119,207,192'; Light = '29,117,109' },
    [pscustomobject]@{ Role = 'Access'; Dark = '186,217,115'; Light = '106,138,32' },
    [pscustomobject]@{ Role = 'Body'; Dark = '204,204,204'; Light = '83,83,83' },
    [pscustomobject]@{ Role = 'Action'; Dark = '204,204,204'; Light = '83,83,83' },
    [pscustomobject]@{ Role = 'ActionVariant'; Dark = '204,204,204'; Light = '83,83,83' },
    [pscustomobject]@{ Role = 'Adjust'; Dark = '204,204,204'; Light = '83,83,83' },
    [pscustomobject]@{ Role = 'Navigation'; Dark = '204,204,204'; Light = '83,83,83' },
    [pscustomobject]@{ Role = 'CommandKey'; Dark = '204,204,204'; Light = '83,83,83' },
    [pscustomobject]@{ Role = 'SectionSeparator'; Dark = '255,255,255'; Light = '53,53,53' },
    [pscustomobject]@{ Role = 'PanelSeparator'; Dark = '204,204,204'; Light = '83,83,83' }
)
foreach ($expectedRole in $figmaRoles) {
    $darkStyle = Get-TerminalPresentationStyle -Role $expectedRole.Role -Palette DeckleDark
    $lightStyle = Get-TerminalPresentationStyle -Role $expectedRole.Role -Palette DeckleLight
    Assert-Rgb $expectedRole.Dark $darkStyle.ForegroundRgb "$($expectedRole.Role) uses the Figma dark token"
    Assert-Rgb $expectedRole.Light $lightStyle.ForegroundRgb "$($expectedRole.Role) uses the Figma light token"
    Assert-Rgb '1,22,39' $darkStyle.BackgroundRgb "$($expectedRole.Role) uses the Figma dark surface"
    Assert-Rgb '246,246,246' $lightStyle.BackgroundRgb "$($expectedRole.Role) uses the Figma light surface"
}
foreach ($mutedRole in @('Context', 'Supporting', 'PrimarySeparator', 'PagingSeparator', 'Separator', 'CommandLabel')) {
    $mutedStyle = Get-TerminalPresentationStyle -Role $mutedRole -Palette DeckleDark
    Assert-Equal $null $mutedStyle.ForegroundRgb "$mutedRole keeps its distinct muted neutral"
}
$enabledNavigation = Get-TerminalPresentationStyle -Role Navigation -Palette DeckleDark
$disabledNavigation = Get-TerminalPresentationStyle -Role Navigation -State Disabled -Palette DeckleDark
Assert-Equal $false ($null -eq $enabledNavigation.ForegroundRgb) 'enabled Navigation keeps the body-neutral level'
Assert-Equal $null $disabledNavigation.ForegroundRgb 'disabled Navigation drops to the muted level'
Assert-Rgb '1,22,39' $disabledNavigation.BackgroundRgb 'a disabled target keeps the designed surface'

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
foreach ($themePalette in @('Terminal', 'DeckleDark', 'DeckleLight')) {
    foreach ($role in $neutralRoles) {
        foreach ($state in @('Normal', 'Focused', 'Disabled')) {
            $style = Get-TerminalPresentationStyle -Role $role -State $state -Palette $themePalette
            Assert-Equal $false ($style.Foreground -in $chromaticColors) "$themePalette $role $state foreground preserves the grayscale hierarchy"
            Assert-Equal $false ($style.Background -in $chromaticColors) "$themePalette $role $state background preserves the grayscale hierarchy"
        }
    }
    foreach ($role in @('Banner', 'Section', 'PanelTitle', 'Danger', 'Exit', 'Success', 'Warning', 'Error')) {
        $disabledStyle = Get-TerminalPresentationStyle -Role $role -State Disabled -Palette $themePalette
        Assert-Equal ([ConsoleColor]::DarkGray) $disabledStyle.Foreground "$themePalette $role becomes muted when disabled"
        Assert-Equal $null $disabledStyle.Background "$themePalette $role disabled state does not retain a chromatic background"
    }
}
foreach ($themePalette in @('DeckleDark', 'DeckleLight')) {
    $sectionRgb = @((Get-TerminalPresentationStyle -Role Section -Palette $themePalette).ForegroundRgb) -join ','
    $actionLabelRgb = @((Get-TerminalPresentationStyle -Role ActionLabel -Palette $themePalette).ForegroundRgb) -join ','
    $accessRgb = @((Get-TerminalPresentationStyle -Role Access -Palette $themePalette).ForegroundRgb) -join ','
    Assert-Equal $false ($sectionRgb -eq $actionLabelRgb) "$themePalette Section and Action subjects use distinct hierarchy colors"
    Assert-Equal $false ($actionLabelRgb -eq $accessRgb) "$themePalette Action subjects and Accesses use distinct hierarchy colors"
    Assert-Equal $false ($sectionRgb -eq $accessRgb) "$themePalette Section and Accesses use distinct hierarchy colors"
}

# The Terminal palette keeps the console-colour selection.
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

# The Deckle palette has one selection for every target.
$designedActionFocus = Get-TerminalPresentationStyle -Role Action -State Focused -Palette DeckleDark
Assert-Equal ([ConsoleColor]::Black) $designedActionFocus.Foreground 'designed dark focus has a high-contrast legacy foreground'
Assert-Equal ([ConsoleColor]::White) $designedActionFocus.Background 'designed dark focus has a high-contrast legacy background'
Assert-Rgb '1,22,39' $designedActionFocus.ForegroundRgb 'dark focus reuses the dark surface as its text color'
Assert-Rgb '255,255,255' $designedActionFocus.BackgroundRgb 'dark focus uses the Figma selection surface'
foreach ($role in @('Access', 'Navigation', 'Danger', 'Error', 'Exit')) {
    $designedFocus = Get-TerminalPresentationStyle -Role $role -State Focused -Palette DeckleDark
    Assert-Equal ([ConsoleColor]::Black) $designedFocus.Foreground "designed focused $role shares the uniform selection fallback foreground"
    Assert-Equal ([ConsoleColor]::White) $designedFocus.Background "designed focused $role shares the uniform selection fallback background"
    Assert-Rgb '1,22,39' $designedFocus.ForegroundRgb "designed focused $role uses the uniform dark text token"
    Assert-Rgb '255,255,255' $designedFocus.BackgroundRgb "designed focused $role uses the uniform dark selection token"
}
$lightFocus = Get-TerminalPresentationStyle -Role Danger -State Focused -Palette DeckleLight
Assert-Equal ([ConsoleColor]::White) $lightFocus.Foreground 'light focus has a high-contrast legacy foreground'
Assert-Equal ([ConsoleColor]::DarkGray) $lightFocus.Background 'light focus has a high-contrast legacy background'
Assert-Rgb '246,246,246' $lightFocus.ForegroundRgb 'light focus reuses the light surface as its text color'
Assert-Rgb '53,53,53' $lightFocus.BackgroundRgb 'light focus uses the Figma selection surface'
foreach ($themePalette in @('Terminal', 'DeckleDark', 'DeckleLight')) {
    $noColor = Get-TerminalPresentationStyle -Role Danger -State Focused -ColorCapability Unsupported -Palette $themePalette
    Assert-Equal $null $noColor.Foreground "$themePalette no-color mode emits no foreground dependency"
    Assert-Equal $null $noColor.Background "$themePalette no-color mode emits no background dependency"
    Assert-Equal $null $noColor.ForegroundRgb "$themePalette no-color mode emits no RGB foreground dependency"
    Assert-Equal $null $noColor.BackgroundRgb "$themePalette no-color mode emits no RGB background dependency"
}

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
