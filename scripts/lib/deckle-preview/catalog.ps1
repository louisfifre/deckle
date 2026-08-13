# Deckle-specific descriptors for the parallel interaction preview.

function New-DecklePreviewBackTarget {
    return New-TerminalTarget `
        -TargetId 'navigation.back' `
        -Label 'Back' `
        -IntentKind Navigation `
        -Payload ([pscustomobject]@{ Command = 'Back' }) `
        -PresentationRole Navigation
}

function New-DecklePreviewActionTarget {
    param(
        [Parameter(Mandatory)][string]$ActionId,
        [Parameter(Mandatory)][string]$Label,
        [string]$Variant,
        [ValidateSet('Action', 'Danger')][string]$Role = 'Action'
    )

    $targetId = if ($Variant) { "action.$ActionId.$($Variant.ToLowerInvariant())" } else { "action.$ActionId" }
    return New-TerminalTarget `
        -TargetId $targetId `
        -Label $Label `
        -IntentKind Action `
        -Payload ([pscustomobject]@{ ActionId = $ActionId; Variant = $Variant; Label = $Label }) `
        -PresentationRole $Role
}

function New-DecklePreviewAccessTarget {
    param(
        [Parameter(Mandatory)][string]$AccessId,
        [Parameter(Mandatory)][string]$Label
    )

    return New-TerminalTarget `
        -TargetId "access.$AccessId" `
        -Label $Label `
        -IntentKind Access `
        -Payload ([pscustomobject]@{ AccessId = $AccessId })
}

function Get-DecklePreviewRootView {
    $release = New-DecklePreviewActionTarget -ActionId launch -Label Release -Variant Release
    $debug = New-DecklePreviewActionTarget -ActionId launch -Label Debug -Variant Debug
    $buildRunRelease = New-DecklePreviewActionTarget -ActionId build-run -Label Release -Variant Release
    $buildRunDebug = New-DecklePreviewActionTarget -ActionId build-run -Label Debug -Variant Debug
    $buildRelease = New-DecklePreviewActionTarget -ActionId build -Label Release -Variant Release
    $buildDebug = New-DecklePreviewActionTarget -ActionId build -Label Debug -Variant Debug

    $project = New-DecklePreviewAccessTarget -AccessId project -Label Project
    $releaseAccess = New-DecklePreviewAccessTarget -AccessId release -Label Release
    $maintenance = New-DecklePreviewAccessTarget -AccessId maintenance -Label Maintenance
    $setup = New-DecklePreviewAccessTarget -AccessId setup -Label Setup
    $quit = New-TerminalTarget `
        -TargetId command.quit `
        -Label Quit `
        -IntentKind Command `
        -Payload ([pscustomobject]@{ Command = 'Exit' }) `
        -PresentationRole Exit

    $sections = @(
        New-TerminalSection -Label Run -Items @(
            New-TerminalActionRow -Label Launch -Variants @($release, $debug)
            New-TerminalActionRow -Label 'Build & run' -Variants @($buildRunRelease, $buildRunDebug)
            New-TerminalActionRow -Label 'Build (no run)' -Variants @($buildRelease, $buildDebug)
        )
        New-TerminalSection -Label Workspace -Items @($project, $releaseAccess, $maintenance, $setup, $quit)
    )
    return New-TerminalActionMenuView -ViewId menu.root -Banner 'Deckle Interaction Preview' -Sections $sections
}

function Get-DecklePreviewProjectView {
    return New-TerminalActionMenuView `
        -ViewId menu.project `
        -Banner 'Deckle Interaction Preview' `
        -Context Project `
        -BackTarget (New-DecklePreviewBackTarget) `
        -Sections @(
            New-TerminalSection -Label Docs -Items @(
                New-DecklePreviewActionTarget -ActionId readme-stats -Label 'README pulse'
                New-DecklePreviewActionTarget -ActionId changelog -Label Changelog
            )
            New-TerminalSection -Label Version -Items @(
                New-DecklePreviewActionTarget -ActionId record-version -Label 'Record version'
            )
        )
}

function Get-DecklePreviewReleaseView {
    return New-TerminalActionMenuView `
        -ViewId menu.release `
        -Banner 'Deckle Interaction Preview' `
        -Context Release `
        -BackTarget (New-DecklePreviewBackTarget) `
        -Sections @(
            New-TerminalSection -Label Publish -Items @(
                New-DecklePreviewActionTarget -ActionId publish-app -Label App
                New-DecklePreviewActionTarget -ActionId publish-native -Label 'Native runtime'
            )
            New-TerminalSection -Label Prepare -Items @(
                New-DecklePreviewActionTarget -ActionId prepare-app -Label 'App artifacts'
                New-DecklePreviewActionTarget -ActionId prepare-native -Label 'Native runtime'
            )
        )
}

function Get-DecklePreviewMaintenanceView {
    return New-TerminalActionMenuView `
        -ViewId menu.maintenance `
        -Banner 'Deckle Interaction Preview' `
        -Context Maintenance `
        -BackTarget (New-DecklePreviewBackTarget) `
        -Sections @(
            New-TerminalSection -Label Statistics -Items @(
                New-DecklePreviewActionTarget -ActionId repository-stats -Label Repository
                New-DecklePreviewActionTarget -ActionId context-stats -Label Context
            )
            New-TerminalSection -Label Cleanup -Items @(
                New-DecklePreviewActionTarget -ActionId clean -Label 'Build outputs'
                New-DecklePreviewActionTarget -ActionId build-servers -Label 'Build servers'
            )
            New-TerminalSection -Label 'AI sessions' -Items @(
                New-DecklePreviewActionTarget -ActionId inspect-agent-state -Label Inspect
                New-DecklePreviewActionTarget -ActionId reset-agent-state -Label Reset -Role Danger
            )
        )
}

function Get-DecklePreviewSetupView {
    return New-TerminalActionMenuView `
        -ViewId menu.setup `
        -Banner 'Deckle Interaction Preview' `
        -Context Setup `
        -BackTarget (New-DecklePreviewBackTarget) `
        -Sections @(
            New-TerminalSection -Label Machine -Items @(
                New-DecklePreviewActionTarget -ActionId bootstrap -Label Bootstrap
                New-DecklePreviewActionTarget -ActionId assets -Label 'Runtime assets'
            )
            New-TerminalSection -Label Repo -Items @(
                New-DecklePreviewActionTarget -ActionId hooks -Label 'Install git hooks'
            )
        )
}

function Get-DecklePreviewAccessView {
    param([Parameter(Mandatory)][string]$AccessId)

    switch ($AccessId) {
        'project' { return Get-DecklePreviewProjectView }
        'release' { return Get-DecklePreviewReleaseView }
        'maintenance' { return Get-DecklePreviewMaintenanceView }
        'setup' { return Get-DecklePreviewSetupView }
        default { throw "Unknown preview Access '$AccessId'." }
    }
}
