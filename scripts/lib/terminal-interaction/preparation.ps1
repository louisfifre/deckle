# Semantic Preparation descriptors and their cross-revision invariants.

function New-TerminalSelectionOption {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$OptionId,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Label,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value,
        [bool]$Enabled = $true,
        [string]$DisabledReason
    )

    if (-not $Enabled -and [string]::IsNullOrWhiteSpace($DisabledReason)) {
        throw "Disabled selection option '$OptionId' requires a reason."
    }

    return [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.TerminalInteraction.SelectionOption'
        OptionId = $OptionId
        Label = $Label
        Value = $Value
        Enabled = $Enabled
        DisabledReason = $DisabledReason
    }
}

function New-TerminalSelector {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$SelectorId,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$FilterLabel,
        [Parameter(Mandatory)][ValidateSet('Single', 'Multiple')][string]$SelectionMode,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][object[]]$Options,
        [AllowEmptyCollection()][string[]]$SelectedValues = @()
    )

    if ($SelectionMode -eq 'Single' -and $SelectedValues.Count -ne 1) {
        throw "Single-value Selector '$SelectorId' requires exactly one Selection."
    }

    $optionIds = @{}
    $optionValues = @{}
    foreach ($option in $Options) {
        if ($optionIds.ContainsKey($option.OptionId)) {
            throw "Selector '$SelectorId' declares option '$($option.OptionId)' more than once."
        }
        if ($optionValues.ContainsKey($option.Value)) {
            throw "Selector '$SelectorId' declares value '$($option.Value)' more than once."
        }
        $optionIds[$option.OptionId] = $true
        $optionValues[$option.Value] = $true
    }
    foreach ($selectedValue in $SelectedValues) {
        if (-not $optionValues.ContainsKey($selectedValue)) {
            throw "Selector '$SelectorId' selects unknown value '$selectedValue'."
        }
    }

    $targets = [System.Collections.Generic.List[object]]::new()
    foreach ($option in $Options) {
        $isSelected = $SelectedValues -contains $option.Value
        $targets.Add((New-TerminalTarget `
            -TargetId "selector.$SelectorId.$($option.OptionId)" `
            -Label $option.Label `
            -IntentKind Adjust `
            -Payload ([pscustomobject][ordered]@{
                SelectorId = $SelectorId
                OptionId = $option.OptionId
                Value = $option.Value
                SelectionMode = $SelectionMode
            }) `
            -Enabled $option.Enabled `
            -DisabledReason $option.DisabledReason `
            -PresentationRole Adjust `
            -SelectionMode $SelectionMode `
            -Selected $isSelected))
    }

    return [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.TerminalInteraction.Selector'
        SelectorId = $SelectorId
        FilterLabel = $FilterLabel
        SelectionMode = $SelectionMode
        Options = @($Options)
        SelectedValues = @($SelectedValues)
        Targets = @($targets)
    }
}

function New-TerminalEffectiveScope {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Revision,
        [Parameter(Mandatory)][ValidateSet('Resolving', 'Resolved', 'Failed')][string]$State,
        [AllowEmptyCollection()][string[]]$Lines = @(),
        [string]$FailureReason
    )

    if ($State -eq 'Failed' -and [string]::IsNullOrWhiteSpace($FailureReason)) {
        throw "Failed Effective Scope revision '$Revision' requires a reason."
    }

    return [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.TerminalInteraction.EffectiveScope'
        Revision = $Revision
        State = $State
        Lines = @($Lines)
        FailureReason = $FailureReason
    }
}

function New-TerminalReview {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Revision,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Lines
    )

    return [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.TerminalInteraction.Review'
        Revision = $Revision
        Lines = @($Lines)
    }
}

function New-TerminalPreparationView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ViewId,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Banner,
        [string]$Context,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ActionId,
        [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Revision,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][object[]]$Selectors,
        [Parameter(Mandatory)][object]$EffectiveScope,
        [Parameter(Mandatory)][object]$Review,
        [Parameter(Mandatory)][object]$ConfirmationTarget,
        [object]$BackTarget,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$OwnerActionMenuId
    )

    if ($EffectiveScope.Revision -ne $Revision) {
        throw "Effective Scope revision '$($EffectiveScope.Revision)' does not match Preparation revision '$Revision'."
    }
    if ($Review.Revision -ne $Revision) {
        throw "Review revision '$($Review.Revision)' does not match Preparation revision '$Revision'."
    }
    if ($ConfirmationTarget.IntentKind -ne 'Action') {
        throw "Preparation Confirmation target '$($ConfirmationTarget.TargetId)' must request an Action."
    }
    $confirmationRevision = $ConfirmationTarget.Payload.PSObject.Properties['PreparationRevision']
    if ($null -eq $confirmationRevision -or [int]$confirmationRevision.Value -ne $Revision) {
        throw "Preparation Confirmation target '$($ConfirmationTarget.TargetId)' must reference revision '$Revision'."
    }
    if ($EffectiveScope.State -ne 'Resolved' -and $ConfirmationTarget.Enabled) {
        throw "Preparation Confirmation cannot be enabled while Effective Scope is '$($EffectiveScope.State)'."
    }

    $defaultTargetId = $null
    foreach ($selector in $Selectors) {
        $firstEnabled = @($selector.Targets | Where-Object { $_.Enabled } | Select-Object -First 1)
        if ($firstEnabled.Count -gt 0) {
            $defaultTargetId = $firstEnabled[0].TargetId
            break
        }
    }

    $descriptor = [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.TerminalInteraction.PreparationView'
        Kind = 'Preparation'
        ViewId = $ViewId
        Banner = $Banner
        Context = $Context
        ActionId = $ActionId
        Revision = $Revision
        Selectors = @($Selectors)
        EffectiveScope = $EffectiveScope
        Review = $Review
        ConfirmationTarget = $ConfirmationTarget
        BackTarget = $BackTarget
        OwnerActionMenuId = $OwnerActionMenuId
        DefaultTargetId = $defaultTargetId
    }
    Assert-TerminalDescriptorTargets -Descriptor $descriptor
    return $descriptor
}
