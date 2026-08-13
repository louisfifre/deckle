# Preview controller for one compact Repository statistics Preparation.

function Get-DecklePreviewRepositoryScopeOptions {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$RepositoryRoot)

    $root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $trackedPaths = @(& git -C $root ls-files)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not discover tracked repository locations under '$root'."
    }

    $hasRootFiles = $false
    $topLevelFolders = @{}
    foreach ($trackedPathValue in $trackedPaths) {
        $trackedPath = ([string]$trackedPathValue).Replace('\', '/').Trim('/')
        if ([string]::IsNullOrWhiteSpace($trackedPath)) { continue }
        $separatorIndex = $trackedPath.IndexOf('/')
        if ($separatorIndex -lt 0) {
            $hasRootFiles = $true
            continue
        }
        $folder = $trackedPath.Substring(0, $separatorIndex)
        $topLevelFolders[$folder] = $true
    }

    $options = [System.Collections.Generic.List[object]]::new()
    if ($hasRootFiles) {
        $options.Add((New-TerminalSelectionOption `
            -OptionId root-files `
            -Label 'Root files' `
            -Value root-files))
    }
    foreach ($folder in @($topLevelFolders.Keys | Sort-Object)) {
        $options.Add((New-TerminalSelectionOption `
            -OptionId "folder:$folder" `
            -Label "$folder/" `
            -Value $folder))
    }

    if ($options.Count -eq 0) {
        throw "Repository '$root' has no tracked locations to select."
    }
    return @($options)
}

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

function Get-DecklePreviewScopeSnapshot {
    param([Parameter(Mandatory)][object]$Selector)

    $selectedValues = @($Selector.SelectedValues)
    return [pscustomobject][ordered]@{
        IsWholeRepository = $selectedValues.Count -eq $Selector.Options.Count
        IncludeRootFiles = $selectedValues -contains 'root-files'
        Paths = @($selectedValues | Where-Object { $_ -ne 'root-files' })
    }
}

function Get-DecklePreviewScopeDescription {
    param([Parameter(Mandatory)][object]$Selector)

    $snapshot = Get-DecklePreviewScopeSnapshot -Selector $Selector
    if ($snapshot.IsWholeRepository) { return 'Whole repository' }
    if ($Selector.SelectedValues.Count -eq 0) { return 'Nothing selected' }
    return '{0} of {1} tracked locations' -f $Selector.SelectedValues.Count, $Selector.Options.Count
}

function Get-DecklePreviewScopeDetailLines {
    param([Parameter(Mandatory)][object]$Selector)

    $snapshot = Get-DecklePreviewScopeSnapshot -Selector $Selector
    if ($snapshot.IsWholeRepository) {
        return @('Whole repository; tracked files only.')
    }
    if ($Selector.SelectedValues.Count -eq 0) {
        return @('No repository location selected.')
    }

    $selectedOptions = @($Selector.Options | Where-Object { $Selector.SelectedValues -contains $_.Value })
    $excludedOptions = @($Selector.Options | Where-Object { $Selector.SelectedValues -notcontains $_.Value })
    if ($excludedOptions.Count -lt $selectedOptions.Count) {
        return @('Excluded: {0}.' -f (($excludedOptions | ForEach-Object Label) -join ', '))
    }
    return @('Included: {0}.' -f (($selectedOptions | ForEach-Object Label) -join ', '))
}

function New-DecklePreviewStatisticsPreparation {
    param(
        [int]$Revision = 1,
        [System.Collections.IDictionary]$Selections,
        [object[]]$ScopeOptions,
        [string]$RepositoryRoot,
        [string]$OwnerActionMenuId = 'menu.maintenance'
    )

    if ($null -eq $ScopeOptions -or $ScopeOptions.Count -eq 0) {
        if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
            throw 'RepositoryRoot is required when ScopeOptions are not supplied.'
        }
        $ScopeOptions = @(Get-DecklePreviewRepositoryScopeOptions -RepositoryRoot $RepositoryRoot)
    }
    if ($null -eq $Selections) {
        $Selections = [ordered]@{
            scope = @($ScopeOptions | ForEach-Object { $_.Value })
            files = @('text')
            measures = @('files', 'bytes', 'lines')
            grouping = @('extension')
        }
    }

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
        New-TerminalSelector -SelectorId scope -FilterLabel Scope -SelectionMode Multiple -Options $ScopeOptions -SelectedValues @($Selections['scope'])
        New-TerminalSelector -SelectorId files -FilterLabel Files -SelectionMode Single -Options $fileOptions -SelectedValues @($Selections['files'])
        New-TerminalSelector -SelectorId measures -FilterLabel Measures -SelectionMode Multiple -Options $measureOptions -SelectedValues @($Selections['measures'])
        New-TerminalSelector -SelectorId grouping -FilterLabel Grouping -SelectionMode Single -Options $groupingOptions -SelectedValues @($Selections['grouping'])
    )

    $scopeSelector = @($selectors | Where-Object { $_.SelectorId -eq 'scope' })[0]
    $filesSelector = @($selectors | Where-Object { $_.SelectorId -eq 'files' })[0]
    $measuresSelector = @($selectors | Where-Object { $_.SelectorId -eq 'measures' })[0]
    $groupingSelector = @($selectors | Where-Object { $_.SelectorId -eq 'grouping' })[0]
    $scopeLabel = Get-DecklePreviewScopeDescription -Selector $scopeSelector
    $filesLabel = Get-DecklePreviewSelectedLabels -Selector $filesSelector
    $measuresLabel = Get-DecklePreviewSelectedLabels -Selector $measuresSelector
    $groupingLabel = Get-DecklePreviewSelectedLabels -Selector $groupingSelector

    $effectiveScope = New-TerminalEffectiveScope `
        -Revision $Revision `
        -State Resolved `
        -Lines @(
            @(Get-DecklePreviewScopeDetailLines -Selector $scopeSelector)
            "$filesLabel; tracked files only."
            'Links and junctions are excluded.'
        )
    $review = New-TerminalReview `
        -Revision $Revision `
        -Lines @(
            'Action: Repository statistics'
            "Scope: $scopeLabel"
            $(if (-not (Get-DecklePreviewScopeSnapshot -Selector $scopeSelector).IsWholeRepository) {
                Get-DecklePreviewScopeDetailLines -Selector $scopeSelector
            })
            "Files: $filesLabel"
            "Measures: $measuresLabel"
            "Grouping: $groupingLabel"
        )

    $hasScope = $scopeSelector.SelectedValues.Count -gt 0
    $hasMeasures = $measuresSelector.SelectedValues.Count -gt 0
    $canConfirm = $hasScope -and $hasMeasures
    $selectionSnapshot = [pscustomobject][ordered]@{
        Scope = (Get-DecklePreviewScopeSnapshot -Selector $scopeSelector)
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
        -DisabledReason $(if (-not $hasScope) {
            'Select at least one repository location.'
        } elseif (-not $hasMeasures) {
            'Select at least one measure.'
        } else {
            $null
        }) `
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
    $scopeSelector = @($View.Selectors | Where-Object { $_.SelectorId -eq 'scope' } | Select-Object -First 1)[0]

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
        -ScopeOptions $scopeSelector.Options `
        -OwnerActionMenuId $View.OwnerActionMenuId
}
