# Preview controller for one compact Repository statistics Preparation.

function Get-DecklePreviewOptionLabel {
    param(
        [Parameter(Mandatory)][object[]]$Options,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )

    $match = @($Options | Where-Object { $_.Value -eq $Value } | Select-Object -First 1)
    if ($match.Count -eq 0) { return $Value }
    return $match[0].Label
}

function Get-DecklePreviewSelectedLabels {
    param(
        [Parameter(Mandatory)][object]$Selector,
        [string]$EmptyLabel = 'None selected'
    )

    $labels = @(
        foreach ($option in $Selector.Options) {
            if ($selector.SelectedValues -contains $option.Value) { $option.Label }
        }
    )
    if ($labels.Count -eq 0) { return $EmptyLabel }
    return $labels -join ', '
}

function New-DecklePreviewStatisticsPreparation {
    param(
        [int]$Revision = 1,
        [System.Collections.IDictionary]$Selections,
        [string]$OwnerActionMenuId = 'menu.maintenance'
    )

    if ($null -eq $Selections) {
        $Selections = [ordered]@{
            scope = @('repository')
            files = @('text')
            measures = @('files', 'bytes', 'lines')
            grouping = @('extension')
        }
    }

    $scopeOptions = @(
        New-TerminalSelectionOption -OptionId repository -Label 'Whole repository' -Value repository
        New-TerminalSelectionOption -OptionId src -Label 'src/' -Value src
        New-TerminalSelectionOption -OptionId scripts -Label 'scripts/' -Value scripts
        New-TerminalSelectionOption -OptionId docs -Label 'docs/' -Value docs
    )
    $fileOptions = @(
        New-TerminalSelectionOption -OptionId all -Label 'All tracked files' -Value all
        New-TerminalSelectionOption -OptionId text -Label 'Supported text' -Value text
        New-TerminalSelectionOption -OptionId source -Label 'C#, XAML, RESW' -Value source
        New-TerminalSelectionOption -OptionId docs -Label 'Documentation' -Value documentation
    )
    $measureOptions = @(
        New-TerminalSelectionOption -OptionId files -Label 'File count' -Value files
        New-TerminalSelectionOption -OptionId bytes -Label 'Size' -Value bytes
        New-TerminalSelectionOption -OptionId lines -Label 'Text lines' -Value lines
        New-TerminalSelectionOption -OptionId source -Label 'Source LOC' -Value source
    )
    $groupingOptions = @(
        New-TerminalSelectionOption -OptionId extension -Label 'File extension' -Value extension
        New-TerminalSelectionOption -OptionId folder -Label 'Top-level folder' -Value folder
        New-TerminalSelectionOption -OptionId none -Label 'No grouping' -Value none
    )

    $selectors = @(
        New-TerminalSelector -SelectorId scope -FilterLabel Scope -SelectionMode Single -Options $scopeOptions -SelectedValues @($Selections['scope'])
        New-TerminalSelector -SelectorId files -FilterLabel Files -SelectionMode Single -Options $fileOptions -SelectedValues @($Selections['files'])
        New-TerminalSelector -SelectorId measures -FilterLabel Measures -SelectionMode Multiple -Options $measureOptions -SelectedValues @($Selections['measures'])
        New-TerminalSelector -SelectorId grouping -FilterLabel Grouping -SelectionMode Single -Options $groupingOptions -SelectedValues @($Selections['grouping'])
    )

    $scopeSelector = @($selectors | Where-Object { $_.SelectorId -eq 'scope' })[0]
    $filesSelector = @($selectors | Where-Object { $_.SelectorId -eq 'files' })[0]
    $measuresSelector = @($selectors | Where-Object { $_.SelectorId -eq 'measures' })[0]
    $groupingSelector = @($selectors | Where-Object { $_.SelectorId -eq 'grouping' })[0]
    $scopeLabel = Get-DecklePreviewSelectedLabels -Selector $scopeSelector
    $filesLabel = Get-DecklePreviewSelectedLabels -Selector $filesSelector
    $measuresLabel = Get-DecklePreviewSelectedLabels -Selector $measuresSelector
    $groupingLabel = Get-DecklePreviewSelectedLabels -Selector $groupingSelector

    $effectiveScope = New-TerminalEffectiveScope `
        -Revision $Revision `
        -State Resolved `
        -Lines @(
            $scopeLabel
            "$filesLabel; tracked files only."
            'Links and junctions are excluded.'
        )
    $review = New-TerminalReview `
        -Revision $Revision `
        -Lines @(
            'Action: Repository statistics'
            "Scope: $scopeLabel"
            "Files: $filesLabel"
            "Measures: $measuresLabel"
            "Grouping: $groupingLabel"
        )

    $canConfirm = $measuresSelector.SelectedValues.Count -gt 0
    $selectionSnapshot = [pscustomobject][ordered]@{
        Scope = @($scopeSelector.SelectedValues)
        Files = @($filesSelector.SelectedValues)
        Measures = @($measuresSelector.SelectedValues)
        Grouping = @($groupingSelector.SelectedValues)
    }
    $confirmation = New-TerminalTarget `
        -TargetId confirmation.repository-stats.run `
        -Label 'Run scan' `
        -IntentKind Action `
        -Payload ([pscustomobject][ordered]@{
            ActionId = 'repository-stats'
            Variant = $null
            Label = 'Repository statistics'
            PreparationRevision = $Revision
            Selections = $selectionSnapshot
        }) `
        -Enabled $canConfirm `
        -DisabledReason $(if ($canConfirm) { $null } else { 'Select at least one measure.' }) `
        -PresentationRole Action

    return New-TerminalPreparationView `
        -ViewId preparation.repository-stats `
        -Banner 'Deckle Interaction Preview' `
        -Context 'Maintenance / Repository statistics' `
        -ActionId repository-stats `
        -Revision $Revision `
        -Selectors $selectors `
        -EffectiveScope $effectiveScope `
        -Review $review `
        -ConfirmationTarget $confirmation `
        -BackTarget (New-DecklePreviewBackTarget) `
        -OwnerActionMenuId $OwnerActionMenuId
}

function Update-DecklePreviewStatisticsPreparation {
    param(
        [Parameter(Mandatory)][object]$View,
        [Parameter(Mandatory)][object]$Adjustment
    )

    $selections = [ordered]@{}
    foreach ($selector in $View.Selectors) {
        $selections[$selector.SelectorId] = @($selector.SelectedValues)
    }

    $sourceSelector = @($View.Selectors | Where-Object { $_.SelectorId -eq $Adjustment.SelectorId } | Select-Object -First 1)
    if ($sourceSelector.Count -ne 1) {
        throw "Unknown preview Selector '$($Adjustment.SelectorId)'."
    }
    $selector = $sourceSelector[0]
    if ($selector.SelectionMode -eq 'Single') {
        $selections[$selector.SelectorId] = @([string]$Adjustment.Value)
    } else {
        $selected = @{}
        foreach ($value in $selector.SelectedValues) { $selected[[string]$value] = $true }
        if ($selected.ContainsKey([string]$Adjustment.Value)) {
            $selected.Remove([string]$Adjustment.Value)
        } else {
            $selected[[string]$Adjustment.Value] = $true
        }
        $selections[$selector.SelectorId] = @(
            foreach ($option in $selector.Options) {
                if ($selected.ContainsKey([string]$option.Value)) { [string]$option.Value }
            }
        )
    }

    return New-DecklePreviewStatisticsPreparation `
        -Revision ($View.Revision + 1) `
        -Selections $selections `
        -OwnerActionMenuId $View.OwnerActionMenuId
}
