# Deckle launcher submenu definitions.
. (Join-Path $PSScriptRoot 'menu-layout.ps1')

function Show-Submenu {
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Rows,
        [string]$Footer,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact',
        [string]$ResultTitle,
        [object[]]$ResultLines = @(),
        [ValidateSet('Guidance', 'Log', 'Report')]
        [string]$ResultMode = 'Guidance',
        [ValidateSet('Run', 'Select')]
        [string]$Interaction = 'Run',
        [hashtable]$SelectionState,
        [switch]$PreparedRows
    )

    $withBack = if ($PreparedRows) { @($Rows) } else { @(Get-DeckleSubmenuRows -Sections $Rows) }

    # Arrive on the first action: '< Back' keeps its top spot (one ↑ away) but
    # never holds the entry selection.
    $v = Select-Grid -Header $Header -Rows $withBack -Footer $Footer -StartSel 1 -ClearScreen -BannerStyle $BannerStyle -ResultTitle $ResultTitle -ResultLines $ResultLines -ResultFollowTail:($ResultMode -eq 'Log') -Interaction $Interaction -SelectionState $SelectionState
    if ($null -eq $v -or $v -eq '__back__') { return $null }
    return $v
}

function Get-DeckleSubmenuRows {
    param([Parameter(Mandatory)][object[]]$Sections)
    return @(
        @{ Cells = @( @{ Label = '< Back'; Value = '__back__'; Role = 'back' } ) }
        @{ Blank = $true }
    ) + @(ConvertTo-MenuRows -Sections $Sections -Columns 2)
}

. (Join-Path $PSScriptRoot 'statistics-menus.ps1')

function Show-ProjectMenu {
    $sections = @(
        @{ Prefix = 'Docs'; Items = @(
            @{ Label = 'Update README pulse'; Value = 'readme-stats' }
            @{ Label = 'Update changelog';    Value = 'changelog' }
        ) }
        @{ Prefix = 'Version'; Items = @(
            @{ Label = 'Record version'; Value = 'record-version' }
        ) }
    )
    $menuRows = @(Get-DeckleSubmenuRows -Sections $sections)
    $resultTitle = 'Results'
    $resultLines = @('Select a project action.')
    $resultMode = 'Guidance'
    $selection = @{ Index = 1; PreferredColumn = 0 }
    while ($true) {
        $v = Show-Submenu -Header 'Deckle > Project' -Rows $menuRows -PreparedRows -ResultTitle $resultTitle -ResultLines $resultLines -ResultMode $resultMode -SelectionState $selection
        if ($null -eq $v) { return }
        $result = switch ($v) {
            'readme-stats'   { Invoke-WorktreeScript -Script 'update-readme-stats.ps1' -Label 'Update README pulse' -Source Project -MenuRows $menuRows -ScriptParameters @{ Commit = $true } }
            'changelog'      { Invoke-WorktreeScript -Script 'changelog.ps1' -Label 'Update changelog' -Source Project -MenuRows $menuRows -ScriptParameters @{ Commit = $true } }
            'record-version' { Invoke-RecordVersion -MenuRows $menuRows }
        }
        if ($null -ne $result) { $resultTitle = $result.Title; $resultLines = @($result.Lines); $resultMode = 'Log' }
    }
}

function Show-ReleaseMenu {
    $sections = @(
        @{ Prefix = 'Publish'; Items = @(
            @{ Label = 'App release';    Value = 'publish-app' }
            @{ Label = 'Native runtime'; Value = 'publish-native' }
        ) }
        @{ Prefix = 'Prepare'; Items = @(
            @{ Label = 'App artifacts';  Value = 'prepare-app' }
            @{ Label = 'Native runtime'; Value = 'prepare-native' }
        ) }
    )
    $menuRows = @(Get-DeckleSubmenuRows -Sections $sections)
    $resultTitle = 'Results'
    $resultLines = @('Publish and prepare are independent for app and native runtime.')
    $resultMode = 'Guidance'
    $selection = @{ Index = 1; PreferredColumn = 0 }
    while ($true) {
        $v = Show-Submenu -Header 'Deckle > Release' -Rows $menuRows -PreparedRows -ResultTitle $resultTitle -ResultLines $resultLines -ResultMode $resultMode -SelectionState $selection
        if ($null -eq $v) { return }
        $result = switch ($v) {
            'publish-app'    { Invoke-PublishRelease -MenuRows $menuRows }
            'publish-native' { Invoke-NativeRuntime -MenuRows $menuRows -Publish }
            'prepare-app'    { Invoke-PrepareArtifacts -MenuRows $menuRows }
            'prepare-native' { Invoke-NativeRuntime -MenuRows $menuRows }
        }
        if ($null -ne $result) { $resultTitle = $result.Title; $resultLines = @($result.Lines); $resultMode = 'Log' }
    }
}

function Show-MaintenanceMenu {
    $sections = @(
        @{ Prefix = 'Statistics'; Items = @(
            @{ Label = Get-MaintenanceScanLabel -Kind Repository; Value = 'stats' }
            @{ Label = Get-MaintenanceScanLabel -Kind Context;    Value = 'context' }
        ) }
        @{ Prefix = 'Cleanup'; Items = @(
            @{ Label = 'Clean build outputs';     Value = 'clean' }
            @{ Label = 'Stop .NET build servers'; Value = 'build-servers' }
        ) }
    )
    $menuRows = @(Get-DeckleSubmenuRows -Sections $sections)
    $resultTitle = 'Results'
    $resultLines = @('Select a statistics action to inspect this worktree.')
    $resultMode = 'Guidance'
    $selection = @{ Index = 1; PreferredColumn = 0 }
    while ($true) {
        $v = Show-Submenu `
            -Header 'Deckle > Maintenance' `
            -BannerStyle Compact `
            -Rows $menuRows -PreparedRows `
            -ResultTitle $resultTitle `
            -ResultLines $resultLines `
            -ResultMode $resultMode `
            -SelectionState $selection

        if ($null -eq $v) { return }
        switch ($v) {
            'clean' {
                $result = Invoke-CleanBuildOutputs -MenuRows $menuRows
                if ($null -ne $result) { $resultTitle = $result.Title; $resultLines = @($result.Lines); $resultMode = 'Log' }
            }
            'build-servers' {
                $result = Invoke-StopBuildServers -MenuRows $menuRows
                if ($null -ne $result) { $resultTitle = $result.Title; $resultLines = @($result.Lines); $resultMode = 'Log' }
            }
            'stats' {
                Invoke-MaintenanceScanFlow -Kind Repository
            }
            'context' {
                Invoke-MaintenanceScanFlow -Kind Context
            }
        }
    }
}

function Show-SetupMenu {
    $sections = @(
        @{ Prefix = 'Machine'; Items = @(
            @{ Label = 'Bootstrap dev environment'; Value = 'bootstrap' }
            @{ Label = 'Set up runtime assets';     Value = 'assets'    }
        ) }
        @{ Prefix = 'Repo'; Items = @(
            @{ Label = 'Install git hooks'; Value = 'hooks' }
        ) }
    )
    $menuRows = @(Get-DeckleSubmenuRows -Sections $sections)
    $resultTitle = 'Results'
    $resultLines = @('Select a setup action.')
    $resultMode = 'Guidance'
    $selection = @{ Index = 1; PreferredColumn = 0 }
    while ($true) {
        $v = Show-Submenu -Header 'Deckle > Setup' -Rows $menuRows -PreparedRows -ResultTitle $resultTitle -ResultLines $resultLines -ResultMode $resultMode -SelectionState $selection
        if ($null -eq $v) { return }
        $result = switch ($v) {
            'bootstrap' { Invoke-BootstrapDev -MenuRows $menuRows }
            'assets'    { Invoke-SetupAssets -MenuRows $menuRows }
            'hooks' {
                $scriptPath = Join-Path $CommandDir 'install-hooks.ps1'
                Invoke-DeckleMenuAction -Header 'Deckle > Setup > Git hooks' -Label 'Install git hooks' -Source Setup -MenuRows $menuRows -Action { & $scriptPath }
            }
        }
        if ($null -ne $result) { $resultTitle = $result.Title; $resultLines = @($result.Lines); $resultMode = 'Log' }
    }
}
