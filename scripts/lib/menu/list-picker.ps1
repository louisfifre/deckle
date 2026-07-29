# One-dimensional menu and worktree pickers.
function Test-MenuHeader {
    param($Item)
    $prop = $Item.PSObject.Properties['IsHeader']
    return ($null -ne $prop) -and [bool]$prop.Value
}

function Write-MenuLine {
    param(
        [int]$Row,
        [string]$Label,
        [string]$Role,
        [bool]$Selected,
        [bool]$IsHeader,
        [int]$ContentWidth,
        [int]$InnerWidth
    )

    Write-MenuLinePrefix -Row $Row
    $written = 0
    if ($IsHeader) {
        $title = ' ' + ([string]$Label).ToUpperInvariant() + ' '
        Write-MenuContentSegment -Text $title -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor Magenta -BackgroundColor $null
        $rule = New-MenuRule -MaxWidth ($ContentWidth - $written) -Style Section
        Write-MenuContentSegment -Text $rule -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor Gray -BackgroundColor $null
    } else {
        $itemRole = Get-MenuCellRole -Cell ([pscustomobject]@{ Label = $Label; Role = $Role })
        $colors = Get-MenuRoleColor -Role $itemRole -Selected:$Selected
        if ($Selected) {
            $line = ('    ' + $Label)
            if ($line.Length -lt $ContentWidth) { $line += ' ' * ($ContentWidth - $line.Length) }
            Write-MenuContentSegment -Text $line -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor $colors.Foreground -BackgroundColor $colors.Background
        } else {
            Write-MenuContentSegment -Text '    ' -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor DarkGray -BackgroundColor $null
            Write-MenuContentSegment -Text $Label -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor $colors.Foreground -BackgroundColor $colors.Background
        }
    }
    Write-MenuLineRemainder -InnerWidth $InnerWidth -Written $written
}

function Invoke-MenuLoop {
    param(
        [string]$Header,
        [object[]]$Items,
        [int]$Default = 0,
        [switch]$ClearScreen,
        [string]$Footer = 'Up/Down move   Enter confirm   Esc back',
        [ValidateSet('Full', 'Compact')]
        [string]$BannerStyle = 'Full'
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

    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
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

    $viewport = New-MenuViewport -Header $Header -Footer $Footer -BodyCount $Items.Count -ClearScreen:$ClearScreen -BannerStyle $BannerStyle
    $metrics = Get-MenuMetrics

    for ($i = 0; $i -lt $Items.Count; $i++) {
        $it = $Items[$i]
        Write-MenuLine -Row ($viewport.BodyTop + $i) -Label $it.Label -Role $it.Role -Selected ($i -eq $selected) -IsHeader $it.IsHeader -ContentWidth $viewport.ContentWidth -InnerWidth $viewport.InnerWidth
    }

    [Console]::CursorVisible = $false
    try {
        while ($true) {
            $key  = [Console]::ReadKey($true)
            $currentMetrics = Get-MenuMetrics
            if ($currentMetrics.TerminalWidth -ne $metrics.TerminalWidth -or $currentMetrics.WindowHeight -ne $metrics.WindowHeight) {
                $viewport = New-MenuViewport -Header $Header -Footer $Footer -BodyCount $Items.Count -ClearScreen -BannerStyle $BannerStyle
                $metrics = Get-MenuMetrics
                for ($i = 0; $i -lt $Items.Count; $i++) {
                    $it = $Items[$i]
                    Write-MenuLine -Row ($viewport.BodyTop + $i) -Label $it.Label -Role $it.Role -Selected ($i -eq $selected) -IsHeader $it.IsHeader -ContentWidth $viewport.ContentWidth -InnerWidth $viewport.InnerWidth
                }
            }
            $prev = $selected
            switch ($key.Key) {
                'UpArrow' {
                    # Step up through selectables only.
                    $pos = $selectableIdx.IndexOf($selected)
                    if ($pos -gt 0) { $selected = $selectableIdx[$pos - 1] }
                }
                'DownArrow' {
                    $pos = $selectableIdx.IndexOf($selected)
                    if ($pos -lt $selectableIdx.Count - 1) { $selected = $selectableIdx[$pos + 1] }
                }
                'Enter'  {
                    Set-MenuCursorPosition -Left 0 -Top $viewport.Bottom
                    return $selected
                }
                'Escape' {
                    Set-MenuCursorPosition -Left 0 -Top $viewport.Bottom
                    return -1
                }
            }
            if ($selected -eq $prev) { continue }
            Write-MenuLine -Row ($viewport.BodyTop + $prev)     -Label $Items[$prev].Label     -Role $Items[$prev].Role     -Selected $false -IsHeader $Items[$prev].IsHeader -ContentWidth $viewport.ContentWidth -InnerWidth $viewport.InnerWidth
            Write-MenuLine -Row ($viewport.BodyTop + $selected) -Label $Items[$selected].Label -Role $Items[$selected].Role -Selected $true  -IsHeader $Items[$selected].IsHeader -ContentWidth $viewport.ContentWidth -InnerWidth $viewport.InnerWidth
        }
    } finally {
        [Console]::CursorVisible = $true
    }
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
        [ValidateSet('Full', 'Compact')]
        [string]$BannerStyle = 'Full'
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
        $idx = Invoke-MenuLoop -Header 'Pick a worktree:' -Items $items
        if ($idx -lt 0) { throw [System.OperationCanceledException]::new('Worktree selection was cancelled.') }
        return $entries[$idx].Path
    }

    $rows = @(New-WorktreeGridRows -Entries $entries)
    $choice = Select-Grid `
        -Header 'Deckle > Worktrees   -   ↑↓ move   Enter select   Esc back' `
        -Footer '↑↓ move   Enter select   Esc back' `
        -Rows $rows `
        -StartSel 1 `
        -ClearScreen:$false `
        -BannerStyle $BannerStyle `
        -CategoryWidth $script:MenuCategoryWidth
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
        [ValidateSet('Full', 'Compact')]
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
        $idx = Invoke-MenuLoop -Header $Header -Items $normalised -Default $Default -ClearScreen:$ClearScreen -BannerStyle $BannerStyle
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
        -Footer '↑↓ move   Enter select   Esc back' `
        -Rows $rows `
        -StartSel ($defaultSelection + 1) `
        -ClearScreen:$ClearScreen `
        -BannerStyle $BannerStyle `
        -CategoryWidth $script:MenuCategoryWidth
    if ($null -eq $choice -or $choice -eq '__back__') { throw 'Cancelled' }
    return $choice
}

function Select-YesNo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Question,
        [bool]$Default = $false,
        [string]$ConfirmLabel = 'Yes',
        [string]$CancelLabel = 'No',
        [switch]$Destructive,
        [switch]$ClearScreen,
        [ValidateSet('Full', 'Compact')]
        [string]$BannerStyle = 'Full'
    )

    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
        $hint = if ($Default) { '[Y/n]' } else { '[y/N]' }
        $answer = Read-Host "$Question $hint"
        if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
        return ($answer -match '^(y|yes|o|oui)$')
    }

    $rows = @(
        @{ Cells = @(
            @{ Label = $ConfirmLabel; Value = $true; Role = $(if ($Destructive) { 'danger' } else { 'action' }) }
            @{ Label = $CancelLabel;  Value = $false; Role = 'back' }
        ) }
    )

    $choice = Select-Grid `
        -Header $Question `
        -Footer 'Left/Right move   Enter confirm' `
        -Rows $rows `
        -StartSel 0 `
        -StartCol $(if ($Default) { 0 } else { 1 }) `
        -EscapeAction Ignore `
        -ClearScreen:$ClearScreen `
        -BannerStyle $BannerStyle `
        -CategoryWidth $script:MenuCategoryWidth

    return [bool]$choice
}
