$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
Import-Module (Join-Path $LibDir 'terminal-interaction.psm1') -Force
. (Join-Path $LibDir 'deckle-preview\catalog.ps1')
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
