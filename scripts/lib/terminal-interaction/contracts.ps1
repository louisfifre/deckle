# Semantic descriptors consumed by the interaction core and renderer.

function New-TerminalTarget {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$TargetId,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Label,
        [Parameter(Mandatory)][ValidateSet('Action', 'Access', 'Adjust', 'Navigation', 'Command')][string]$IntentKind,
        [object]$Payload,
        [bool]$Enabled = $true,
        [string]$DisabledReason,
        [ValidateSet('Action', 'Access', 'Adjust', 'Navigation', 'Exit', 'Danger')]
        [string]$PresentationRole,
        [ValidateSet('None', 'Single', 'Multiple')][string]$SelectionMode = 'None',
        [bool]$Selected = $false
    )

    if (-not $Enabled -and [string]::IsNullOrWhiteSpace($DisabledReason)) {
        throw "Disabled target '$TargetId' requires a reason."
    }

    if ([string]::IsNullOrWhiteSpace($PresentationRole)) {
        $PresentationRole = switch ($IntentKind) {
            'Access' { 'Access' }
            'Adjust' { 'Adjust' }
            'Navigation' { 'Navigation' }
            default { 'Action' }
        }
    }

    if ($Selected -and $SelectionMode -eq 'None') {
        throw "Target '$TargetId' cannot be selected without a selection mode."
    }

    return [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.TerminalInteraction.Target'
        TargetId = $TargetId
        Label = $Label
        IntentKind = $IntentKind
        Payload = $Payload
        Enabled = $Enabled
        DisabledReason = $DisabledReason
        PresentationRole = $PresentationRole
        SelectionMode = $SelectionMode
        Selected = $Selected
    }
}

function New-TerminalActionRow {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Label,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][object[]]$Variants
    )

    foreach ($variant in $Variants) {
        if ($variant.IntentKind -ne 'Action') {
            throw "Action Row '$Label' can contain only Action targets."
        }
    }

    return [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.TerminalInteraction.ActionRow'
        Kind = 'ActionRow'
        Label = $Label
        Variants = @($Variants)
    }
}

function New-TerminalSection {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Label,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][object[]]$Items
    )

    foreach ($item in $Items) {
        $isActionRow = $item.PSObject.Properties['Kind'] -and $item.Kind -eq 'ActionRow'
        $isTarget = $null -ne $item.PSObject.Properties['IntentKind']
        if (-not $isActionRow -and -not $isTarget) {
            throw "Section '$Label' accepts only semantic targets and Action Rows."
        }
    }

    return [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.TerminalInteraction.Section'
        Label = $Label
        Items = @($Items)
    }
}

function Get-TerminalDescriptorTargets {
    param([Parameter(Mandatory)][object]$Descriptor)

    $targets = [System.Collections.Generic.List[object]]::new()
    $backProperty = $Descriptor.PSObject.Properties['BackTarget']
    if ($null -ne $backProperty -and $null -ne $backProperty.Value) { $targets.Add($backProperty.Value) }
    $sectionsProperty = $Descriptor.PSObject.Properties['Sections']
    if ($null -ne $sectionsProperty -and $null -ne $sectionsProperty.Value) {
        foreach ($section in $Descriptor.Sections) {
            foreach ($item in $section.Items) {
                if ($item.PSObject.Properties['Kind'] -and $item.Kind -eq 'ActionRow') {
                    foreach ($target in $item.Variants) { $targets.Add($target) }
                } else {
                    $targets.Add($item)
                }
            }
        }
    }
    $targetsProperty = $Descriptor.PSObject.Properties['Targets']
    if ($null -ne $targetsProperty -and $null -ne $targetsProperty.Value) {
        foreach ($target in $Descriptor.Targets) { $targets.Add($target) }
    }
    $selectorsProperty = $Descriptor.PSObject.Properties['Selectors']
    if ($null -ne $selectorsProperty -and $null -ne $selectorsProperty.Value) {
        foreach ($selector in $Descriptor.Selectors) {
            foreach ($target in $selector.Targets) { $targets.Add($target) }
        }
    }
    $confirmationProperty = $Descriptor.PSObject.Properties['ConfirmationTarget']
    if ($null -ne $confirmationProperty -and $null -ne $confirmationProperty.Value) {
        $targets.Add($confirmationProperty.Value)
    }
    return @($targets)
}

function Assert-TerminalDescriptorTargets {
    param([Parameter(Mandatory)][object]$Descriptor)

    $known = @{}
    foreach ($target in @(Get-TerminalDescriptorTargets -Descriptor $Descriptor)) {
        if ($known.ContainsKey($target.TargetId)) {
            throw "View '$($Descriptor.ViewId)' declares target '$($target.TargetId)' more than once."
        }
        $known[$target.TargetId] = $true
    }
}

function New-TerminalActionMenuView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ViewId,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Banner,
        [string]$Context,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][object[]]$Sections,
        [object]$BackTarget,
        [string]$OwnerActionMenuId
    )

    $descriptor = [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.TerminalInteraction.ActionMenuView'
        Kind = 'ActionMenu'
        ViewId = $ViewId
        Banner = $Banner
        Context = $Context
        Sections = @($Sections)
        BackTarget = $BackTarget
        OwnerActionMenuId = if ($OwnerActionMenuId) { $OwnerActionMenuId } else { $ViewId }
    }
    Assert-TerminalDescriptorTargets -Descriptor $descriptor
    return $descriptor
}

function New-TerminalContentView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ViewId,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Banner,
        [string]$Context,
        [Parameter(Mandatory)][object[]]$Lines,
        [object[]]$Targets = @(),
        [object]$BackTarget,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$OwnerActionMenuId
    )

    $descriptor = [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.TerminalInteraction.ContentView'
        Kind = 'Content'
        ViewId = $ViewId
        Banner = $Banner
        Context = $Context
        Lines = @($Lines)
        Targets = @($Targets)
        BackTarget = $BackTarget
        OwnerActionMenuId = $OwnerActionMenuId
    }
    Assert-TerminalDescriptorTargets -Descriptor $descriptor
    return $descriptor
}

function New-TerminalExecutionView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ViewId,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Banner,
        [string]$Context,
        [Parameter(Mandatory)][ValidateSet('Running', 'Completed', 'Failed')][string]$State,
        [Parameter(Mandatory)][object[]]$JournalLines,
        [Parameter(Mandatory)][object[]]$TrackingSteps,
        [string]$Result,
        [object]$BackTarget,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$OwnerActionMenuId
    )

    if ($State -eq 'Running' -and $null -ne $BackTarget) {
        $BackTarget = New-TerminalTarget `
            -TargetId $BackTarget.TargetId `
            -Label $BackTarget.Label `
            -IntentKind $BackTarget.IntentKind `
            -Payload $BackTarget.Payload `
            -Enabled $false `
            -DisabledReason 'Wait for the execution to finish.' `
            -PresentationRole $BackTarget.PresentationRole
    }

    $descriptor = [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.TerminalInteraction.ExecutionView'
        Kind = 'Execution'
        ViewId = $ViewId
        Banner = $Banner
        Context = $Context
        State = $State
        JournalLines = @($JournalLines)
        TrackingSteps = @($TrackingSteps)
        Result = $Result
        BackTarget = $BackTarget
        OwnerActionMenuId = $OwnerActionMenuId
    }
    Assert-TerminalDescriptorTargets -Descriptor $descriptor
    return $descriptor
}
