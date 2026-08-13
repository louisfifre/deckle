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

function Assert-True([bool]$Condition, [string]$Case) {
    if (-not $Condition) { throw "${Case}: condition was false" }
}

function Get-Placement([object]$Frame, [string]$TargetId) {
    $match = @($Frame.Targets | Where-Object { $_.TargetId -eq $TargetId })
    if ($match.Count -ne 1) { throw "placement '$TargetId': expected one match, got $($match.Count)" }
    return $match[0]
}

$root = Get-DecklePreviewRootView
$wide = Get-TerminalInteractionFrame -View $root -Width 100 -Height 24 -FocusedTargetId action.launch.release
$wideText = @(ConvertTo-TerminalFrameText -Frame $wide)
$launchRelease = Get-Placement -Frame $wide -TargetId action.launch.release
$launchDebug = Get-Placement -Frame $wide -TargetId action.launch.debug
Assert-Equal $launchRelease.Y $launchDebug.Y 'Action Variants share their Action Row in a wide terminal'
Assert-True ($launchRelease.X -lt $launchDebug.X) 'Release precedes Debug without semantic reordering'

$maintenance = Get-Placement -Frame $wide -TargetId access.maintenance
$setup = Get-Placement -Frame $wide -TargetId access.setup
$releaseAccess = Get-Placement -Frame $wide -TargetId access.release
$quit = Get-Placement -Frame $wide -TargetId command.quit
Assert-Equal $maintenance.Y $quit.Y 'Quit keeps the final Access row in a wide terminal'
Assert-True ($maintenance.X -lt $setup.X -and $setup.X -lt $quit.X) 'Accesses and Quit keep their declared order'
Assert-Equal $releaseAccess.X $setup.X 'Quit does not shift Setup out of the second option column'
Assert-Equal 0 @($wide.Targets | Where-Object { $_.TargetId -like 'navigation.page.*' }).Count 'a fitting menu has no paging controls'
Assert-True (($wideText -join "`n") -match 'Enter Open') 'global command indications are visible in the Header'
Assert-True (($wideText -join "`n") -notmatch 'Backspace Back') 'the root menu does not advertise Back'
$arrowLegend = -join @([char]0x2191, [char]0x2193, [char]0x2190, [char]0x2192)
Assert-True (($wideText -join "`n").Contains($arrowLegend)) 'Unicode-capable output preserves the arrow command key'

$asciiFallback = Get-TerminalInteractionFrame -View $root -Width 100 -Height 24 -FocusedTargetId action.launch.release -SupportsUnicode $false
$asciiFallbackText = @(ConvertTo-TerminalFrameText -Frame $asciiFallback) -join "`n"
Assert-True ($asciiFallbackText -match 'Arrows Move') 'an output host without Unicode receives an explicit ASCII key label'
Assert-Equal $false $asciiFallbackText.Contains($arrowLegend) 'the ASCII fallback emits no unsupported arrow glyph'

$narrow = Get-TerminalInteractionFrame -View $root -Width 60 -Height 30 -FocusedTargetId action.launch.release
$narrowRelease = Get-Placement -Frame $narrow -TargetId action.launch.release
$narrowDebug = Get-Placement -Frame $narrow -TargetId action.launch.debug
Assert-True ($narrowRelease.Y -lt $narrowDebug.Y) 'Action Variants stack in declared order in a narrow terminal'
Assert-Equal 11 @($narrow.Targets | Where-Object { $_.TargetId -notlike 'navigation.page.*' }).Count 'all root targets remain reachable at narrow width'
foreach ($line in @(ConvertTo-TerminalFrameText -Frame $narrow -PreserveWidth)) {
    Assert-Equal 60 $line.Length 'narrow frame lines honor terminal width'
}

$shortFirst = Get-TerminalInteractionFrame -View $root -Width 60 -Height 14 -FocusedTargetId action.launch.release
$nextPage = Get-Placement -Frame $shortFirst -TargetId navigation.page.next
Assert-Equal $true $nextPage.Target.Enabled 'a short first page exposes an enabled Next target'
Assert-True ((@(ConvertTo-TerminalFrameText -Frame $shortFirst) -join "`n") -match 'Wheel Scroll') 'scrolling commands appear only in the paging footer'

$shortLast = Get-TerminalInteractionFrame -View $root -Width 60 -Height 14 -BodyOffset ([int]::MaxValue) -FocusedTargetId navigation.page.previous
$previousPage = Get-Placement -Frame $shortLast -TargetId navigation.page.previous
Assert-Equal $true $previousPage.Target.Enabled 'a later page exposes an enabled Previous target'
Assert-Equal 1 @($shortLast.Targets | Where-Object { $_.TargetId -eq 'command.quit' }).Count 'the final page makes Quit reachable'

$project = Get-DecklePreviewProjectView
$projectFrame = Get-TerminalInteractionFrame -View $project -Width 100 -Height 24 -FocusedTargetId navigation.back
$projectText = @(ConvertTo-TerminalFrameText -Frame $projectFrame) -join "`n"
$projectBack = Get-Placement -Frame $projectFrame -TargetId navigation.back
$readmePulse = Get-Placement -Frame $projectFrame -TargetId action.readme-stats
Assert-Equal $readmePulse.X $projectBack.X 'Back occupies the first option column instead of the label column'
Assert-True ($projectText -match 'Backspace Back') 'a nested View advertises Backspace'
Assert-True ($projectText -match 'Escape Menu') 'a nested View advertises Escape to its Action Menu'

$execution = Get-DecklePreviewSnapshotView -Name Execution
$executionWide = Get-TerminalInteractionFrame -View $execution -Width 120 -Height 24 -FocusedTargetId navigation.back -JournalOffset ([int]::MaxValue)
$executionWideText = @(ConvertTo-TerminalFrameText -Frame $executionWide)
$panelTitleLine = @($executionWideText | Where-Object { $_ -match 'Execution Journal' -and $_ -match 'Execution Tracking' })
Assert-Equal 1 $panelTitleLine.Count 'wide Execution presents Journal and Tracking side by side'
Assert-True ($panelTitleLine[0].IndexOf('Execution Tracking') -gt 90) 'wide Tracking occupies the compact right-hand region'
Assert-Equal 1 @($executionWideText | Where-Object { $_ -match 'deliberately long line' }).Count 'a long Journal line is clipped without wrapping'

$executionNarrow = Get-TerminalInteractionFrame -View $execution -Width 60 -Height 20 -FocusedTargetId navigation.back -JournalOffset ([int]::MaxValue)
$executionNarrowText = @(ConvertTo-TerminalFrameText -Frame $executionNarrow)
$journalTitleIndex = [Array]::IndexOf($executionNarrowText, '  Execution Journal')
$trackingTitleIndex = [Array]::IndexOf($executionNarrowText, '  Execution Tracking')
Assert-True ($journalTitleIndex -ge 0 -and $trackingTitleIndex -gt $journalTitleIndex) 'narrow Execution preserves Journal then Tracking order'
Assert-True (($executionNarrowText -join "`n") -match 'Result: Preview only') 'narrow Execution keeps the final Result visible'
foreach ($line in @(ConvertTo-TerminalFrameText -Frame $executionNarrow -PreserveWidth)) {
    Assert-Equal 60 $line.Length 'Execution clipping protects narrow width'
}

Write-Host 'layout.tests.ps1: PASS' -ForegroundColor Green
