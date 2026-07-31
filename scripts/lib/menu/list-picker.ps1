# One-dimensional menu and worktree pickers.
function Test-MenuHeader {
    param($Item)
    $prop = $Item.PSObject.Properties['IsHeader']
    return ($null -ne $prop) -and [bool]$prop.Value
}

function Read-NumberedMenuSelection {
    param(
        [string]$Header,
        [object[]]$Items,
        [int]$Default = 0
    )

    if ($Items.Count -eq 0) { return -1 }

    # Indices of selectable (non-header) items. If empty, nothing to pick.
    $selectableIdx = @()
    for ($i = 0; $i -lt $Items.Count; $i++) {
        if (-not $Items[$i].IsHeader) { $selectableIdx += $i }
    }
    if ($selectableIdx.Count -eq 0) { return -1 }

    # Clamp Default to a valid selectable index.
    $selected = if ($selectableIdx -contains $Default) { $Default } else { $selectableIdx[0] }

    Write-Host $Header
    for ($i = 0; $i -lt $selectableIdx.Count; $i++) {
        $idx = $selectableIdx[$i]
        Write-Host ('  {0}) {1}' -f ($i + 1), $Items[$idx].Label)
    }
    $answer = Read-Host 'Pick a number (Enter = default)'
    if ([string]::IsNullOrWhiteSpace($answer)) { return $selected }
    $choice = 0
    if ([int]::TryParse($answer, [ref]$choice) -and $choice -ge 1 -and $choice -le $selectableIdx.Count) {
        return $selectableIdx[$choice - 1]
    }
    return -1
}

function Get-WorktreeMenuLabel {
    param(
        [Parameter(Mandatory)][string]$Branch,
        [Parameter(Mandatory)][string]$Path
    )

    $directory = Split-Path -Leaf $Path
    $branchLeaf = @($Branch -split '/')[-1]
    if ($branchLeaf -eq $directory) { return $Branch }
    return "$Branch  ·  $directory"
}

function New-WorktreeGridRows {
    param([Parameter(Mandatory)][object[]]$Entries)

    $rows = @(
        @{ Cells = @( @{ Label = '< Back'; Value = '__back__'; Role = 'back' } ) }
        @{ Blank = $true }
        @{ Title = 'Available' }
    )
    foreach ($entry in $Entries) {
        $rows += @{
            FullWidth = $true
            Cells = @(
                @{
                    Label = Get-WorktreeMenuLabel -Branch ([string]$entry.Branch) -Path ([string]$entry.Path)
                    Value = [string]$entry.Path
                }
            )
        }
    }
    return $rows
}

function Select-Worktree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ContextDir,
        [switch]$ClearScreen,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    if ($ClearScreen) { Clear-MenuScreen }

    Push-Location $ContextDir
    try {
        $raw = git worktree list --porcelain 2>$null
    } finally {
        Pop-Location
    }
    if (-not $raw) { throw "git worktree list failed - not a git repo?" }

    # Parse porcelain output into (path, branch) tuples.
    $entries   = @()
    $curPath   = $null
    $curBranch = $null
    foreach ($line in $raw) {
        if ($line -like 'worktree *') {
            if ($curPath) {
                $entries += [pscustomobject]@{ Path = $curPath; Branch = ($curBranch ?? '(detached)') }
            }
            $curPath   = $line.Substring(9)
            $curBranch = $null
        } elseif ($line -like 'branch *') {
            $curBranch = ($line.Substring(7)) -replace '^refs/heads/', ''
        }
    }
    if ($curPath) {
        $entries += [pscustomobject]@{ Path = $curPath; Branch = ($curBranch ?? '(detached)') }
    }
    if ($entries.Count -eq 0) { throw "No worktrees found" }

    # Auto-pick when there is nothing to choose between.
    if ($entries.Count -eq 1) { return $entries[0].Path }

    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
        $items = foreach ($entry in $entries) {
            [pscustomobject]@{
                Label = "[$($entry.Branch)] $(Split-Path -Leaf $entry.Path)"
                IsHeader = $false
                Role = $null
            }
        }
        $idx = Read-NumberedMenuSelection -Header 'Pick a worktree:' -Items $items
        if ($idx -lt 0) { throw [System.OperationCanceledException]::new('Worktree selection was cancelled.') }
        return $entries[$idx].Path
    }

    $rows = @(New-WorktreeGridRows -Entries $entries)
    $choice = Select-Grid `
        -Header 'Deckle > Worktrees' `
        -Rows $rows `
        -StartSel 1 `
        -ClearScreen:$ClearScreen `
        -BannerStyle $BannerStyle `
        -Interaction Select
    if ($null -eq $choice -or $choice -eq '__back__') {
        throw [System.OperationCanceledException]::new('Worktree selection was cancelled.')
    }
    return [string]$choice
}

function Select-Action {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]                 [string]$Header,
        [Parameter(Mandatory)][AllowEmptyCollection()] $Items,
        [int]$Default = 0,
        [switch]$ClearScreen,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    if ($Items.Count -eq 0) { throw "No items to select from" }

    # Normalise: every item ends up with Label + IsHeader. Missing
    # IsHeader defaults to false (regular selectable entry).
    $normalised = foreach ($it in $Items) {
        $role = $it.PSObject.Properties['Role']
        $prefix = $it.PSObject.Properties['Prefix']
        $value = $it.PSObject.Properties['Value']
        [pscustomobject]@{
            Label    = [string]$it.Label
            IsHeader = (Test-MenuHeader $it)
            Role     = if ($null -ne $role) { [string]$role.Value } else { $null }
            Prefix   = if ($null -ne $prefix) { [string]$prefix.Value } else { '' }
            Value    = if ($null -ne $value) { $value.Value } else { $null }
        }
    }

    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
        $idx = Read-NumberedMenuSelection -Header $Header -Items $normalised -Default $Default
        if ($idx -lt 0) { throw 'Cancelled' }
        return $normalised[$idx].Value
    }

    $rows = @(
        @{ Cells = @( @{ Label = '< Back'; Value = '__back__'; Role = 'back' } ) }
        @{ Blank = $true }
    )
    foreach ($item in $normalised) {
        if ($item.IsHeader) {
            $rows += @{ Title = $item.Label }
            continue
        }
        $rows += @{
            Prefix = $item.Prefix
            Cells = @( @{ Label = $item.Label; Value = $item.Value; Role = $item.Role } )
        }
    }

    $defaultSelection = 0
    if ($Default -ge 0 -and $Default -lt $normalised.Count -and -not $normalised[$Default].IsHeader) {
        for ($index = 0; $index -lt $Default; $index++) {
            if (-not $normalised[$index].IsHeader) { $defaultSelection++ }
        }
    }

    $choice = Select-Grid `
        -Header $Header `
        -Rows $rows `
        -StartSel ($defaultSelection + 1) `
        -ClearScreen:$ClearScreen `
        -BannerStyle $BannerStyle `
        -Interaction Select
    if ($null -eq $choice -or $choice -eq '__back__') { throw 'Cancelled' }
    return $choice
}

function New-YesNoGridRows {
    param(
        [Parameter(Mandatory)][string]$ConfirmLabel,
        [Parameter(Mandatory)][string]$CancelLabel,
        [string[]]$ContextLines = @(),
        [switch]$Destructive
    )

    $rows = @()
    if ($ContextLines.Count -gt 0) {
        $rows += @{ Title = 'Before you continue' }
        foreach ($line in $ContextLines) { $rows += @{ Text = $line } }
        $rows += @{ Blank = $true }
    }
    $rows += @(
        @{ Cells = @(
            @{ Label = $ConfirmLabel; Value = $true; Role = $(if ($Destructive) { 'danger' } else { 'action' }) }
            @{ Label = $CancelLabel;  Value = $false; Role = 'back' }
        ) }
    )
    return $rows
}

function Select-YesNo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Question,
        [bool]$Default = $false,
        [string]$ConfirmLabel = 'Yes',
        [string]$CancelLabel = 'No',
        [string[]]$ContextLines = @(),
        [switch]$Destructive,
        [switch]$ClearScreen,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
        foreach ($line in $ContextLines) { Write-Host $line }
        $hint = if ($Default) { '[Y/n]' } else { '[y/N]' }
        $answer = Read-Host "$Question $hint"
        if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
        return ($answer -match '^(y|yes|o|oui)$')
    }

    $rows = @(New-YesNoGridRows -ConfirmLabel $ConfirmLabel -CancelLabel $CancelLabel -ContextLines $ContextLines -Destructive:$Destructive)

    $choice = Select-Grid `
        -Header $Question `
        -Rows $rows `
        -StartSel 0 `
        -StartCol $(if ($Default) { 0 } else { 1 }) `
        -ClearScreen:$ClearScreen `
        -BannerStyle $BannerStyle `
        -Interaction Confirm

    return [bool]$choice
}
