$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
$FixtureDir = Join-Path $ScriptsDir 'tests\fixtures\script-discovery'
Import-Module (Join-Path $LibDir 'terminal-interaction.psm1') -Force
Import-Module (Join-Path $LibDir 'script-discovery.psm1') -Force
. (Join-Path $LibDir 'deckle-preview\catalog.ps1')
. (Join-Path $LibDir 'deckle-preview\discovered-catalog.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected '$Expected', got '$Actual'" }
}

function Assert-True([bool]$Condition, [string]$Case) {
    if (-not $Condition) { throw "${Case}: condition was false" }
}

function Get-ViewTargets([object]$View) {
    $targets = [System.Collections.Generic.List[object]]::new()
    if ($null -ne $View.BackTarget) { $targets.Add($View.BackTarget) }
    foreach ($section in $View.Sections) {
        foreach ($item in $section.Items) {
            if ($item.PSObject.Properties['Kind'] -and $item.Kind -eq 'ActionRow') {
                foreach ($variant in $item.Variants) { $targets.Add($variant) }
            } else {
                $targets.Add($item)
            }
        }
    }
    return @($targets)
}

$catalog = Get-DeckleScriptCatalog -Path $FixtureDir
$root = ConvertTo-DecklePreviewDiscoveredView -Level $catalog -Banner 'Fixture Preview' -IsRoot
Assert-Equal 'menu.discovered' $root.ViewId 'the discovered root has a stable View id'
Assert-Equal $null $root.BackTarget 'the root View has no Back control'

$sections = @($root.Sections)
Assert-Equal 4 $sections.Count 'untitled group, Build, Care, then the closing Access group'
Assert-True ([string]::IsNullOrEmpty($sections[0].Label)) 'scripts without section form the first, untitled group'
Assert-Equal 'Build' $sections[1].Label 'named sections follow in catalog order'
Assert-Equal 'Care' $sections[2].Label 'a section without ordered rows comes after'
Assert-True ([string]::IsNullOrEmpty($sections[3].Label)) 'Accesses close the level as one untitled group'

$alphaTarget = $sections[0].Items[0]
Assert-Equal 'action.alpha.0' $alphaTarget.TargetId 'a row without Variants is one Action target'
Assert-Equal 'Alpha task' $alphaTarget.Label 'the Action target is labelled by its row'

$buildItems = @($sections[1].Items)
Assert-Equal 'action.gamma.0' $buildItems[0].TargetId 'gamma rows come first in Build'
Assert-Equal Action $buildItems[0].PresentationRole 'an ordinary Action keeps the Action role'
Assert-Equal 'action.gamma.1' $buildItems[1].TargetId 'the second action keeps its own id'
Assert-Equal Danger $buildItems[1].PresentationRole 'a danger action takes the Danger role'
$betaRow = $buildItems[2]
Assert-Equal 'ActionRow' $betaRow.Kind 'a row with Variants becomes an Action Row'
Assert-Equal 'Beta build' $betaRow.Label 'the Action Row keeps the row label'
Assert-Equal 'Release,Debug' ((@($betaRow.Variants) | ForEach-Object Label) -join ',') 'each Variant is one Action target labelled by its value'
Assert-Equal 'action.beta.0.release' $betaRow.Variants[0].TargetId 'Variant target ids append the lowercased value'
Assert-Equal 'Debug' $betaRow.Variants[1].Payload.Variant 'activating a Variant carries its value'

$closing = @($sections[3].Items)
Assert-Equal 'access.tools,access.zone,command.quit' ((@($closing) | ForEach-Object TargetId) -join ',') 'Accesses come last, Quit closes the root'
Assert-Equal Access $closing[0].IntentKind 'a folder is an Access'
Assert-Equal 'tools' $closing[0].Payload.AccessId 'the Access payload names the level path'
Assert-True ($closing[0].Label.StartsWith('Tooling') -and $closing[0].Label.EndsWith([string][char]0x2026)) 'an Access label carries the level label and the ellipsis'
Assert-Equal Exit $closing[2].PresentationRole 'Quit keeps the Exit role'

$allTargets = @(Get-ViewTargets $root)
$uniqueIds = @($allTargets | ForEach-Object TargetId | Sort-Object -Unique)
Assert-Equal $allTargets.Count $uniqueIds.Count 'target ids are unique in the View'

$tools = Resolve-DecklePreviewDiscoveredAccess -Catalog $catalog -AccessId 'tools'
Assert-Equal 'Tooling' $tools.Label 'an Access resolves to its level'
$toolsView = ConvertTo-DecklePreviewDiscoveredView -Level $tools -Banner 'Fixture Preview'
Assert-Equal 'menu.discovered.tools' $toolsView.ViewId 'a sub-level View id follows its path'
Assert-Equal 'navigation.back' $toolsView.BackTarget.TargetId 'a sub-level takes the Back control'
Assert-Equal 0 @(Get-ViewTargets $toolsView | Where-Object TargetId -eq 'command.quit').Count 'only the root offers Quit'
$deepAccess = @(Get-ViewTargets $toolsView | Where-Object IntentKind -eq 'Access')[0]
Assert-Equal 'access.tools.deep' $deepAccess.TargetId 'nested Access ids turn slashes into dots'
Assert-Equal 'tools/deep' $deepAccess.Payload.AccessId 'nested Access payloads keep the relative path'
$deep = Resolve-DecklePreviewDiscoveredAccess -Catalog $catalog -AccessId 'tools/deep'
Assert-Equal 'deep' $deep.Label 'a nested Access resolves through the catalog'
$deepView = ConvertTo-DecklePreviewDiscoveredView -Level $deep -Banner 'Fixture Preview'
Assert-Equal 'action.tools.deep.zeta.0' $deepView.Sections[0].Items[0].TargetId 'nested Action ids carry the whole path'
Assert-Equal $null (Resolve-DecklePreviewDiscoveredAccess -Catalog $catalog -AccessId 'missing') 'an unknown Access resolves to nothing'

foreach ($width in @(100, 50)) {
    $frame = Get-TerminalInteractionFrame -View $root -Width $width -Height 40 -FocusedTargetId (Get-DecklePreviewFirstTargetId -View $root)
    $text = @(ConvertTo-TerminalFrameText -Frame $frame) -join "`n"
    Assert-True ($text -match 'BUILD') "the root renders its named sections at width $width"
    Assert-True ($text -match 'Quit') "the root renders Quit at width $width"
}

Write-Host 'discovered-catalog.tests.ps1: PASS' -ForegroundColor Green
