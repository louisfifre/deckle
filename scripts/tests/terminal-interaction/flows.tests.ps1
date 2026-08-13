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

$root = Get-DecklePreviewRootView
$accessDecision = Resolve-DecklePreviewIntent `
    -Request ([pscustomobject]@{
        TargetId = 'access.project'
        IntentKind = 'Access'
        Payload = [pscustomobject]@{ AccessId = 'project' }
        SourceViewId = 'menu.root'
    }) `
    -SourceView $root
Assert-Equal OpenView $accessDecision.Kind 'Access opens another View'
Assert-Equal menu.project $accessDecision.View.ViewId 'Project Access opens the Project Action Menu'
Assert-Equal ActionMenu $accessDecision.View.Kind 'Access destination remains an Action Menu composition'

$actionDecision = Resolve-DecklePreviewIntent `
    -Request ([pscustomobject]@{
        TargetId = 'action.build.release'
        IntentKind = 'Action'
        Payload = [pscustomobject]@{ ActionId = 'build'; Variant = 'Release'; Label = 'Build' }
        SourceViewId = 'menu.root'
    }) `
    -SourceView $root
Assert-Equal OpenView $actionDecision.Kind 'preview Action installs an Execution View'
Assert-Equal Execution $actionDecision.View.Kind 'Action destination is an Execution composition'
Assert-Equal menu.root $actionDecision.View.OwnerActionMenuId 'Execution remembers its owning Action Menu'
Assert-True (@($actionDecision.View.JournalLines | Where-Object { $_ -match 'No repository command was started' }).Count -eq 1) 'preview Execution explicitly records that it is safe'

$maintenance = Get-DecklePreviewMaintenanceView
$statisticsTarget = @($maintenance.Sections[0].Items | Where-Object { $_.TargetId -eq 'action.repository-stats' })[0]
$preparationDecision = Resolve-DecklePreviewIntent `
    -Request ([pscustomobject]@{
        TargetId = $statisticsTarget.TargetId
        IntentKind = $statisticsTarget.IntentKind
        Payload = $statisticsTarget.Payload
        SourceViewId = $maintenance.ViewId
    }) `
    -SourceView $maintenance `
    -RepositoryRoot $RepositoryRoot
Assert-Equal OpenView $preparationDecision.Kind 'statistics Action opens Preparation before Execution'
Assert-Equal Preparation $preparationDecision.View.Kind 'material statistics inputs stay in one Preparation View'

$scopeTarget = @($preparationDecision.View.Selectors[0].Targets | Where-Object { $_.Payload.Value -eq 'src' })[0]
$adjustmentDecision = Resolve-DecklePreviewIntent `
    -Request ([pscustomobject]@{
        TargetId = $scopeTarget.TargetId
        IntentKind = $scopeTarget.IntentKind
        Payload = $scopeTarget.Payload
        SourceViewId = $preparationDecision.View.ViewId
        Activation = 'Enter'
    }) `
    -SourceView $preparationDecision.View
Assert-Equal UpdateView $adjustmentDecision.Kind 'Selector editing updates the current View instead of navigating'
Assert-Equal 2 $adjustmentDecision.View.Revision 'an accepted Selection creates a distinct revision'
Assert-Equal $false ($adjustmentDecision.View.Selectors[0].SelectedValues -contains 'src') 'the next revision carries the toggled additive Selection'
Assert-Equal 2 $adjustmentDecision.View.Review.Revision 'the Review is rebuilt from the accepted Selection revision'
Assert-Equal $false $adjustmentDecision.View.ConfirmationTarget.Payload.Selections.Scope.IsWholeRepository 'partial scope is explicit in the Action request'
Assert-Equal $false ($adjustmentDecision.View.ConfirmationTarget.Payload.Selections.Scope.Paths -contains 'src') 'the Action request carries every selected path rather than one ScopePath'

$confirmation = $adjustmentDecision.View.ConfirmationTarget
$confirmationDecision = Resolve-DecklePreviewIntent `
    -Request ([pscustomobject]@{
        TargetId = $confirmation.TargetId
        IntentKind = $confirmation.IntentKind
        Payload = $confirmation.Payload
        SourceViewId = $adjustmentDecision.View.ViewId
        Activation = 'Enter'
    }) `
    -SourceView $adjustmentDecision.View
Assert-Equal ReplaceView $confirmationDecision.Kind 'Confirmation replaces Preparation with Execution'
Assert-Equal Execution $confirmationDecision.View.Kind 'confirmed statistics enters Execution'
Assert-True (@($confirmationDecision.View.JournalLines | Where-Object { $_ -match 'Frozen reviewed revision 2' }).Count -eq 1) 'Execution freezes the exact reviewed revision'

$executionFrame = Get-TerminalInteractionFrame -View $actionDecision.View -Width 120 -Height 24 -FocusedTargetId navigation.back -JournalOffset ([int]::MaxValue)
$executionText = @(ConvertTo-TerminalFrameText -Frame $executionFrame) -join "`n"
Assert-True ($executionText -notmatch '^\s*Run\s*$') 'Execution replaces the Action Menu body'
Assert-True ($executionText -match 'Execution Journal') 'Execution installs its Journal Panel'
Assert-True ($executionText -match 'Execution Tracking') 'Execution installs its Tracking Panel'

$quitDecision = Resolve-DecklePreviewIntent `
    -Request ([pscustomobject]@{ TargetId = 'command.quit'; IntentKind = 'Command'; Payload = $null; SourceViewId = 'menu.root' }) `
    -SourceView $root
Assert-Equal Exit $quitDecision.Kind 'Quit requests launcher exit'

Write-Host 'flows.tests.ps1: PASS' -ForegroundColor Green
