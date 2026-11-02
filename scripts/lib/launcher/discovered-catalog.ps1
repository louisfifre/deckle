# Projection of a Script Discovery catalog into Action Menu descriptors.
# It builds descriptors only; the intent handler in flows.ps1 acts on them.

function New-LauncherBackTarget {
    return New-TerminalTarget `
        -TargetId 'navigation.back' `
        -Label '< Back' `
        -IntentKind Navigation `
        -Payload ([pscustomobject]@{ Command = 'Back' }) `
        -PresentationRole Navigation
}

function ConvertTo-LauncherIdPath {
    param([AllowEmptyString()][string]$RelativePath)

    return ($RelativePath -replace '\.ps1$', '').Replace('/', '.')
}

function New-LauncherQuitTarget {
    return New-TerminalTarget `
        -TargetId command.quit `
        -Label Quit `
        -IntentKind Command `
        -Payload ([pscustomobject]@{ Command = 'Exit' }) `
        -PresentationRole Exit
}

function New-LauncherDiscoveredActionTarget {
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.ScriptDiscovery.ActionRow')][object]$Row,
        [Parameter(Mandatory)][string]$Label,
        [string]$Variant
    )

    $actionId = "$(ConvertTo-LauncherIdPath -RelativePath $Row.RelativePath).$($Row.ActionIndex)"
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

function ConvertTo-LauncherDiscoveredItem {
    param([Parameter(Mandatory)][PSTypeName('DeckleScripts.ScriptDiscovery.ActionRow')][object]$Row)

    $variants = @($Row.Variants)
    if ($variants.Count -eq 0) {
        return New-LauncherDiscoveredActionTarget -Row $Row -Label $Row.Label
    }
    $targets = foreach ($variant in $variants) {
        New-LauncherDiscoveredActionTarget -Row $Row -Label $variant.Value -Variant $variant.Value
    }
    return New-TerminalActionRow -Label $Row.Label -Variants @($targets)
}

function New-LauncherDiscoveredAccessTarget {
    param([Parameter(Mandatory)][PSTypeName('DeckleScripts.ScriptDiscovery.Level')][object]$Level)

    return New-TerminalTarget `
        -TargetId "access.$(ConvertTo-LauncherIdPath -RelativePath $Level.RelativePath)" `
        -Label ($Level.Label + [char]0x2026) `
        -IntentKind Access `
        -Payload ([pscustomobject]@{ AccessId = $Level.RelativePath })
}

function ConvertTo-LauncherDiscoveredView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.ScriptDiscovery.Level')][object]$Level,
        [Parameter(Mandatory)][string]$Banner,
        [object]$BackTarget,
        [switch]$IsRoot
    )

    $sections = [System.Collections.Generic.List[object]]::new()
    foreach ($catalogSection in $Level.Sections) {
        $items = [System.Collections.Generic.List[object]]::new()
        $rows = @($catalogSection.Rows)
        for ($index = 0; $index -lt $rows.Count; $index++) {
            $row = $rows[$index]
            if ($null -eq $row.Row) { $items.Add((ConvertTo-LauncherDiscoveredItem -Row $row)); continue }
            # The catalog keeps the cells of one named row consecutive; each
            # stays an Action of its own, shown under the row's label.
            $cells = [System.Collections.Generic.List[object]]::new()
            while ($index -lt $rows.Count -and $rows[$index].Row -eq $row.Row) {
                $cells.Add((New-LauncherDiscoveredActionTarget -Row $rows[$index] -Label $rows[$index].Cell))
                $index++
            }
            $index--
            $items.Add((New-TerminalActionRow -Label $row.Row -Variants @($cells)))
        }
        $sections.Add((New-TerminalSection -Label $catalogSection.Label -Items @($items)))
    }

    # A folder with no described script beneath it is not offered: opening
    # it would show nothing to run. Settings belongs to the launcher and
    # closes the list wherever its order would place it.
    $accesses = @($Level.Accesses | Where-Object HasActions)
    $accesses = @($accesses | Where-Object { $_.RelativePath -ne 'settings' }) + @($accesses | Where-Object { $_.RelativePath -eq 'settings' })
    $closingItems = @($accesses | ForEach-Object { New-LauncherDiscoveredAccessTarget -Level $_ })
    if ($IsRoot) {
        $trailing = @((New-LauncherLogsAccessTarget), (New-LauncherQuitTarget))
        $sections.Add((New-TerminalSection -Label $null -Items $closingItems -Trailing $trailing))
    } elseif ($closingItems.Count -gt 0) {
        $sections.Add((New-TerminalSection -Label $null -Items $closingItems))
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

    $idPath = ConvertTo-LauncherIdPath -RelativePath $Level.RelativePath
    $viewId = if ($IsRoot -or $idPath -eq '') { 'menu.discovered' } else { "menu.discovered.$idPath" }
    $parameters = @{
        ViewId = $viewId
        Banner = $Banner
        Sections = @($sections)
    }
    if (-not $IsRoot) {
        $parameters.Context = $Level.Label
        $parameters.BackTarget = if ($null -ne $BackTarget) { $BackTarget } else { New-LauncherBackTarget }
    }
    return New-TerminalActionMenuView @parameters
}

function Resolve-LauncherDiscoveredAccess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.ScriptDiscovery.Level')][object]$Catalog,
        [Parameter(Mandatory)][AllowEmptyString()][string]$AccessId
    )

    if ($Catalog.RelativePath -eq $AccessId) { return $Catalog }
    foreach ($access in $Catalog.Accesses) {
        $found = Resolve-LauncherDiscoveredAccess -Catalog $access -AccessId $AccessId
        if ($null -ne $found) { return $found }
    }
    return $null
}

function Get-LauncherDiscoveredOwnerId {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$RelativePath)

    # The owning Action Menu of a script is the View of its folder.
    $separator = $RelativePath.LastIndexOf('/')
    if ($separator -lt 0) { return 'menu.discovered' }
    return "menu.discovered.$(ConvertTo-LauncherIdPath -RelativePath $RelativePath.Substring(0, $separator))"
}

function New-LauncherConfirmationView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Action,
        [Parameter(Mandatory)][string]$Banner,
        [Parameter(Mandatory)][string]$OwnerActionMenuId
    )

    # The Confirmation step names the effect, puts the safe choice first so
    # it takes the focus, and makes the other choice a deliberate Danger
    # activation. Escape and Back leave without running anything.
    $variantLabel = if ($Action.Variant) { " / $($Action.Variant)" } else { '' }
    $keep = New-TerminalTarget `
        -TargetId "confirmation.$($Action.ActionId).keep" `
        -Label 'Keep everything as it is' `
        -IntentKind Navigation `
        -Payload ([pscustomobject]@{ Command = 'Back' }) `
        -PresentationRole Navigation
    $proceed = New-TerminalTarget `
        -TargetId "confirmation.$($Action.ActionId).proceed" `
        -Label "$($Action.Label)$variantLabel" `
        -IntentKind Action `
        -Payload $Action `
        -PresentationRole Danger
    return New-TerminalContentView `
        -ViewId "confirmation.$($Action.ActionId)" `
        -Banner $Banner `
        -Context "$($Action.Label)$variantLabel" `
        -Lines @(
            $Action.Confirmation
            "Script: $($Action.ScriptRelativePath)$(if ($Action.Arguments) { " $($Action.Arguments)" })"
            ''
            'Nothing has run yet.'
        ) `
        -Targets @($keep, $proceed) `
        -BackTarget (New-LauncherBackTarget) `
        -OwnerActionMenuId $OwnerActionMenuId
}

function Test-LauncherConfirmationView {
    param([Parameter(Mandatory)][object]$View)

    return $View.Kind -eq 'Content' -and $View.ViewId.StartsWith('confirmation.', [StringComparison]::Ordinal)
}

function Get-LauncherFirstTargetId {
    param([Parameter(Mandatory)][object]$View)

    # A root with no script and no folder holds only its trailing Logs and
    # Quit, so the first target is looked for there too.
    $first = @($View.Sections | ForEach-Object {
        $_.Items
        if ($_.PSObject.Properties['Trailing']) { $_.Trailing }
    } | Where-Object { $null -ne $_ })[0]
    if ($first.PSObject.Properties['Kind'] -and $first.Kind -eq 'ActionRow') { return $first.Variants[0].TargetId }
    return $first.TargetId
}
