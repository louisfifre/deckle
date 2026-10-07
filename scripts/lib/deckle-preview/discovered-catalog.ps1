# Preview projection of a Script Discovery catalog into Action Menu descriptors.
# It builds descriptors only; Actions still open the in-memory sample Execution.

function ConvertTo-DecklePreviewIdPath {
    param([AllowEmptyString()][string]$RelativePath)

    return ($RelativePath -replace '\.ps1$', '').Replace('/', '.')
}

function New-DecklePreviewQuitTarget {
    return New-TerminalTarget `
        -TargetId command.quit `
        -Label Quit `
        -IntentKind Command `
        -Payload ([pscustomobject]@{ Command = 'Exit' }) `
        -PresentationRole Exit
}

function New-DecklePreviewDiscoveredActionTarget {
    param(
        [Parameter(Mandatory)][object]$Row,
        [Parameter(Mandatory)][string]$Label,
        [string]$Variant
    )

    $actionId = "$(ConvertTo-DecklePreviewIdPath -RelativePath $Row.RelativePath).$($Row.ActionIndex)"
    $targetId = if ($Variant) { "action.$actionId.$($Variant.ToLowerInvariant())" } else { "action.$actionId" }
    $role = if ($Row.Danger) { 'Danger' } else { 'Action' }
    return New-TerminalTarget `
        -TargetId $targetId `
        -Label $Label `
        -IntentKind Action `
        -Payload ([pscustomobject]@{
            ActionId = $actionId
            Variant = $Variant
            Label = $Row.Label
            ScriptRelativePath = $Row.RelativePath
            Arguments = $Row.Arguments
            VariantParameter = $Row.VariantParameter
            Confirmation = $Row.Confirmation
        }) `
        -PresentationRole $role
}

function ConvertTo-DecklePreviewDiscoveredItem {
    param([Parameter(Mandatory)][object]$Row)

    $variants = @($Row.Variants)
    if ($variants.Count -eq 0) {
        return New-DecklePreviewDiscoveredActionTarget -Row $Row -Label $Row.Label
    }
    $targets = foreach ($variant in $variants) {
        New-DecklePreviewDiscoveredActionTarget -Row $Row -Label $variant.Value -Variant $variant.Value
    }
    return New-TerminalActionRow -Label $Row.Label -Variants @($targets)
}

function New-DecklePreviewDiscoveredAccessTarget {
    param([Parameter(Mandatory)][object]$Level)

    return New-TerminalTarget `
        -TargetId "access.$(ConvertTo-DecklePreviewIdPath -RelativePath $Level.RelativePath)" `
        -Label ($Level.Label + [char]0x2026) `
        -IntentKind Access `
        -Payload ([pscustomobject]@{ AccessId = $Level.RelativePath })
}

function ConvertTo-DecklePreviewDiscoveredView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Level,
        [Parameter(Mandatory)][string]$Banner,
        [object]$BackTarget,
        [switch]$IsRoot
    )

    $sections = [System.Collections.Generic.List[object]]::new()
    foreach ($catalogSection in $Level.Sections) {
        $items = @(foreach ($row in $catalogSection.Rows) { ConvertTo-DecklePreviewDiscoveredItem -Row $row })
        $sections.Add((New-TerminalSection -Label $catalogSection.Label -Items $items))
    }

    # Accesses come last as one untitled group; Quit closes the root menu.
    $closingItems = [System.Collections.Generic.List[object]]::new()
    foreach ($access in $Level.Accesses) { $closingItems.Add((New-DecklePreviewDiscoveredAccessTarget -Level $access)) }
    if ($IsRoot) { $closingItems.Add((New-DecklePreviewQuitTarget)) }
    if ($closingItems.Count -gt 0) {
        $sections.Add((New-TerminalSection -Label $null -Items @($closingItems)))
    }
    if ($sections.Count -eq 0) {
        $empty = New-TerminalTarget `
            -TargetId 'info.empty' `
            -Label 'No described script' `
            -IntentKind Command `
            -Enabled $false `
            -DisabledReason 'This folder holds no script with a Header.'
        $sections.Add((New-TerminalSection -Label $null -Items @($empty)))
    }

    $idPath = ConvertTo-DecklePreviewIdPath -RelativePath $Level.RelativePath
    $viewId = if ($IsRoot -or $idPath -eq '') { 'menu.discovered' } else { "menu.discovered.$idPath" }
    $parameters = @{
        ViewId = $viewId
        Banner = $Banner
        Sections = @($sections)
    }
    if (-not $IsRoot) {
        $parameters.Context = $Level.Label
        $parameters.BackTarget = if ($null -ne $BackTarget) { $BackTarget } else { New-DecklePreviewBackTarget }
    }
    return New-TerminalActionMenuView @parameters
}

function Resolve-DecklePreviewDiscoveredAccess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Catalog,
        [Parameter(Mandatory)][AllowEmptyString()][string]$AccessId
    )

    if ($Catalog.RelativePath -eq $AccessId) { return $Catalog }
    foreach ($access in $Catalog.Accesses) {
        $found = Resolve-DecklePreviewDiscoveredAccess -Catalog $access -AccessId $AccessId
        if ($null -ne $found) { return $found }
    }
    return $null
}

function Get-DecklePreviewFirstTargetId {
    param([Parameter(Mandatory)][object]$View)

    $first = $View.Sections[0].Items[0]
    if ($first.PSObject.Properties['Kind'] -and $first.Kind -eq 'ActionRow') { return $first.Variants[0].TargetId }
    return $first.TargetId
}
