$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
$RepositoryRoot = Split-Path -Parent $ScriptsDir
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

function Get-TargetSegment([object]$Frame, [object]$Placement) {
    $matches = @($Frame.Lines[$Placement.Y].Segments | Where-Object { $_.X -eq $Placement.X })
    if ($matches.Count -ne 1) { throw "segment '$($Placement.TargetId)': expected one match, got $($matches.Count)" }
    return $matches[0]
}

$scopeFixture = Join-Path ([System.IO.Path]::GetTempPath()) ('deckle-preview-scope-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $scopeFixture | Out-Null
try {
    & git -C $scopeFixture init --quiet
    New-Item -ItemType Directory -Path (Join-Path $scopeFixture 'docs') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $scopeFixture 'src') | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $scopeFixture 'README.md'), '# Fixture')
    [System.IO.File]::WriteAllText((Join-Path $scopeFixture 'docs/guide.md'), '# Guide')
    [System.IO.File]::WriteAllText((Join-Path $scopeFixture 'src/App.cs'), 'class App {}')
    [System.IO.File]::WriteAllText((Join-Path $scopeFixture 'untracked.txt'), 'untracked')
    & git -C $scopeFixture add -- README.md docs/guide.md src/App.cs
    $fixtureOptions = @(Get-DecklePreviewRepositoryScopeOptions -RepositoryRoot $scopeFixture)
    Assert-Equal 3 $fixtureOptions.Count 'Scope offers tracked top-level partitions'
    Assert-Equal 'Root files' $fixtureOptions[0].Label 'root-level tracked files remain selectable'
    Assert-Equal 'docs/' $fixtureOptions[1].Label 'tracked folders are discovered instead of hard-coded'
    Assert-Equal 'src/' $fixtureOptions[2].Label 'tracked folder order is deterministic'
} finally {
    if (Test-Path -LiteralPath $scopeFixture) { Remove-Item -LiteralPath $scopeFixture -Recurse -Force }
}

$view = New-DecklePreviewStatisticsPreparation -RepositoryRoot $RepositoryRoot
Assert-Equal Preparation $view.Kind 'statistics opens one Preparation composition'
Assert-Equal 4 $view.Selectors.Count 'Preparation keeps material filters together'
Assert-Equal $view.Revision $view.EffectiveScope.Revision 'Effective Scope uses the current Preparation revision'
Assert-Equal $view.Revision $view.Review.Revision 'Review uses the current Preparation revision'
Assert-Equal $view.Revision $view.ConfirmationTarget.Payload.PreparationRevision 'Confirmation uses the reviewed revision'
Assert-Equal 'selector.scope.root-files' $view.DefaultTargetId 'Preparation begins at its first discovered scope option instead of Back'
Assert-Equal Multiple $view.Selectors[0].SelectionMode 'repository locations are additive choices'
Assert-Equal $view.Selectors[0].Options.Count $view.Selectors[0].SelectedValues.Count 'all atomic locations represent the initial whole repository'
Assert-Equal $true $view.ConfirmationTarget.Payload.Selections.Scope.IsWholeRepository 'whole repository is derived from the exhaustive Selection'
Assert-Equal 0 @($view.Selectors[0].Options | Where-Object { $_.Label -eq 'Whole repository' }).Count 'whole repository is not a redundant selectable option'

$wide = Get-TerminalInteractionFrame -View $view -Width 100 -Height 48 -FocusedTargetId selector.scope.root-files
$wideText = @(ConvertTo-TerminalFrameText -Frame $wide) -join "`n"
foreach ($heading in @('FILTERS', 'EFFECTIVE SCOPE', 'REVIEW', 'CONFIRMATION')) {
    Assert-True ($wideText -match $heading) "Preparation keeps $heading in the same View"
}
Assert-True ($wideText -match 'Enter Select') 'Preparation advertises selection rather than opening a destination'
Assert-True ($wideText -match 'Space Toggle') 'a multi-selection Selector advertises Space'
Assert-True ($wideText -match '\[x\] Root files') 'multi-selection scope has a structural checked marker'
Assert-True ($wideText -match '\[x\] File count') 'multi-selection has a structural checked marker'
Assert-True ($wideText -match 'Scope: Whole repository') 'exhaustive atomic scope is summarized as the whole repository'

$scopeRoot = Get-Placement -Frame $wide -TargetId selector.scope.root-files
$scopeSource = Get-Placement -Frame $wide -TargetId 'selector.scope.folder:src'
$filesAll = Get-Placement -Frame $wide -TargetId selector.files.all
Assert-True ($scopeSource.Y -gt $scopeRoot.Y) 'discovered scope options wrap onto additional rows'
Assert-Equal $scopeRoot.X $filesAll.X 'Selectors reuse stable option columns'
$focusedSegment = Get-TargetSegment -Frame $wide -Placement $scopeRoot
Assert-Equal Adjust $focusedSegment.PresentationRole 'Selector targets retain their semantic presentation role'
Assert-Equal Focused $focusedSegment.State 'focus overlays rather than replaces Selector semantics'
Assert-Equal $scopeRoot.Width $focusedSegment.Text.Length 'Selector focus fills its complete grid cell'
Assert-Equal '>' $focusedSegment.Text.Substring(0, 1) 'Selector focus remains visible without color'

$initialFocus = Move-TerminalFocus -Frame $wide -CurrentTargetId $null -Direction Down
Assert-Equal selector.scope.root-files $initialFocus 'Preparation default focus bypasses the Back control'

$narrowFirst = Get-TerminalInteractionFrame -View $view -Width 60 -Height 30 -FocusedTargetId selector.scope.root-files
$nextPage = Get-Placement -Frame $narrowFirst -TargetId navigation.page.next
Assert-Equal $true $nextPage.Target.Enabled 'narrow Preparation paginates instead of creating technical Views'
$narrowLast = Get-TerminalInteractionFrame -View $view -Width 60 -Height 30 -BodyOffset ([int]::MaxValue) -FocusedTargetId confirmation.repository-stats.run
$lastText = @(ConvertTo-TerminalFrameText -Frame $narrowLast) -join "`n"
Assert-True ($lastText -match 'CONFIRMATION') 'the final narrow page reaches Confirmation'
Assert-Equal 1 @($narrowLast.Targets | Where-Object { $_.TargetId -eq 'confirmation.repository-stats.run' }).Count 'Confirmation remains keyboard reachable after paging'

$module = Get-Module terminal-interaction
$retainedState = & $module {
    param($originalView, $updatedView)
    $stack = [System.Collections.Generic.List[object]]::new()
    $state = New-TerminalViewState -View $originalView
    $state.FocusedTargetId = 'selector.measures.lines'
    $state.BodyOffset = 4
    $stack.Add($state)
    [void](Set-TerminalDecision -ViewStack $stack -Decision ([pscustomobject]@{ Kind = 'UpdateView'; View = $updatedView }))
    return $stack[0]
} $view (Update-DecklePreviewStatisticsPreparation -View $view -Adjustment ([pscustomobject]@{ SelectorId = 'scope'; Value = 'src' }))
Assert-Equal selector.measures.lines $retainedState.FocusedTargetId 'accepted Selection changes retain focus'
Assert-Equal 4 $retainedState.BodyOffset 'accepted Selection changes retain the current Preparation page'

$withoutScope = $view
foreach ($value in @($view.Selectors[0].SelectedValues)) {
    $withoutScope = Update-DecklePreviewStatisticsPreparation `
        -View $withoutScope `
        -Adjustment ([pscustomobject]@{ SelectorId = 'scope'; Value = $value })
}
Assert-Equal 0 $withoutScope.Selectors[0].SelectedValues.Count 'scope checkboxes can intentionally select no location'
Assert-Equal $false $withoutScope.ConfirmationTarget.Enabled 'empty scope keeps Confirmation unavailable'
Assert-Equal 'Select at least one repository location.' $withoutScope.ConfirmationTarget.DisabledReason 'empty scope explains how to recover'

$withoutMeasures = $view
foreach ($value in @('files', 'bytes', 'lines')) {
    $withoutMeasures = Update-DecklePreviewStatisticsPreparation `
        -View $withoutMeasures `
        -Adjustment ([pscustomobject]@{ SelectorId = 'measures'; Value = $value })
}
Assert-Equal 0 $withoutMeasures.Selectors[2].SelectedValues.Count 'multi-selection can represent an intentionally empty Selection'
Assert-Equal $false $withoutMeasures.ConfirmationTarget.Enabled 'validation keeps Confirmation unavailable without a measure'
Assert-Equal 'Select at least one measure.' $withoutMeasures.ConfirmationTarget.DisabledReason 'disabled Confirmation explains how to recover'
$invalidFrame = Get-TerminalInteractionFrame -View $withoutMeasures -Width 100 -Height 32 -FocusedTargetId selector.measures.source
$invalidText = @(ConvertTo-TerminalFrameText -Frame $invalidFrame) -join "`n"
Assert-True ($invalidText -match 'x Run scan' -and $invalidText -match 'Select at least one measure\.') 'invalid Preparation exposes a structural marker and recovery text'

Write-Host 'preparation.tests.ps1: PASS' -ForegroundColor Green
