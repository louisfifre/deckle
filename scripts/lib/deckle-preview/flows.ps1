# Preview-only intent handling. No repository command is invoked here.

function New-DecklePreviewExecution {
    param(
        [Parameter(Mandatory)][object]$Action,
        [Parameter(Mandatory)][string]$OwnerActionMenuId
    )

    $variantLabel = if ($Action.Variant) { " / $($Action.Variant)" } else { '' }
    $journal = [System.Collections.Generic.List[object]]::new()
    $journal.Add('[preview] No repository command was started.')
    $journal.Add("[intent] Action $($Action.ActionId)$variantLabel")
    $journal.Add('[core] Intent Request accepted by the Deckle preview handler.')
    $journal.Add('[runtime] Installed an in-memory Execution View.')
    $journal.Add('')
    for ($index = 1; $index -le 28; $index++) {
        $journal.Add(('[sample] Journal line {0:d2} keeps its complete logical value for clipping and paging validation.' -f $index))
    }
    $journal.Add('[sample] This deliberately long line should be clipped at the Journal Panel boundary without wrapping into the Tracking Panel or changing the stored value.')
    $journal.Add('[preview] Completed safely.')

    $tracking = @(
        [pscustomobject]@{ Label = 'Accept intent'; State = 'Completed' }
        [pscustomobject]@{ Label = 'Freeze request'; State = 'Completed' }
        [pscustomobject]@{ Label = 'Render sample'; State = 'Completed' }
    )
    return New-TerminalExecutionView `
        -ViewId "execution.$($Action.ActionId)" `
        -Banner 'Deckle Interaction Preview' `
        -Context "$($Action.Label)$variantLabel" `
        -State Completed `
        -JournalLines @($journal) `
        -TrackingSteps $tracking `
        -Result 'Preview only; no repository changes.' `
        -BackTarget (New-DecklePreviewBackTarget) `
        -OwnerActionMenuId $OwnerActionMenuId
}

function Resolve-DecklePreviewIntent {
    param(
        [Parameter(Mandatory)][object]$Request,
        [Parameter(Mandatory)][object]$SourceView
    )

    if ($Request.IntentKind -eq 'Command' -and $Request.TargetId -eq 'command.quit') {
        return [pscustomobject]@{ Kind = 'Exit' }
    }
    if ($Request.IntentKind -eq 'Access') {
        return [pscustomobject]@{
            Kind = 'OpenView'
            View = Get-DecklePreviewAccessView -AccessId $Request.Payload.AccessId
        }
    }
    if ($Request.IntentKind -eq 'Action') {
        $owner = if ($SourceView.Kind -eq 'ActionMenu') { $SourceView.ViewId } else { $SourceView.OwnerActionMenuId }
        return [pscustomobject]@{
            Kind = 'OpenView'
            View = New-DecklePreviewExecution -Action $Request.Payload -OwnerActionMenuId $owner
        }
    }
    return [pscustomobject]@{ Kind = 'Stay' }
}

function Get-DecklePreviewSnapshotView {
    param([Parameter(Mandatory)][ValidateSet('Menu', 'Project', 'Execution')][string]$Name)

    switch ($Name) {
        'Menu' { return Get-DecklePreviewRootView }
        'Project' { return Get-DecklePreviewProjectView }
        'Execution' {
            return New-DecklePreviewExecution `
                -Action ([pscustomobject]@{ ActionId = 'build'; Variant = 'Release'; Label = 'Build' }) `
                -OwnerActionMenuId menu.root
        }
    }
}
