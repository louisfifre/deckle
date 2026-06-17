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
        [int]$InnerWidth
    )

    Write-MenuLinePrefix -Row $Row
    $written = 0
    if ($IsHeader) {
        $title = ' ' + ([string]$Label).ToUpperInvariant() + ' '
        Write-MenuContentSegment -Text $title -Written ([ref]$written) -InnerWidth $InnerWidth -ForegroundColor Magenta -BackgroundColor $null
        $rule = New-MenuRule -MaxWidth ($InnerWidth - $written) -Style Section
        Write-MenuContentSegment -Text $rule -Written ([ref]$written) -InnerWidth $InnerWidth -ForegroundColor Gray -BackgroundColor $null
    } else {
        Write-MenuContentSegment -Text '    ' -Written ([ref]$written) -InnerWidth $InnerWidth -ForegroundColor DarkGray -BackgroundColor $null
        $itemRole = Get-MenuCellRole -Cell ([pscustomobject]@{ Label = $Label; Role = $Role })
        $colors = Get-MenuRoleColor -Role $itemRole -Selected:$Selected
        Write-MenuContentSegment -Text $Label -Written ([ref]$written) -InnerWidth $InnerWidth -ForegroundColor $colors.Foreground -BackgroundColor $colors.Background
    }
    Write-MenuLineRemainder -InnerWidth $InnerWidth -Written $written
}

function Invoke-MenuLoop {
    param(
        [string]$Header,
        [object[]]$Items,
        [int]$Default = 0,
        [switch]$ClearScreen,
        [string]$Footer = 'Up/Down move   Enter confirm   Esc back'
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

    $viewport = New-MenuViewport -Header $Header -Footer $Footer -BodyCount $Items.Count -ClearScreen:$ClearScreen

    for ($i = 0; $i -lt $Items.Count; $i++) {
        $it = $Items[$i]
        Write-MenuLine -Row ($viewport.BodyTop + $i) -Label $it.Label -Role $it.Role -Selected ($i -eq $selected) -IsHeader $it.IsHeader -InnerWidth $viewport.InnerWidth
    }

    [Console]::CursorVisible = $false
    try {
        while ($true) {
            $key  = [Console]::ReadKey($true)
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
                    [Console]::SetCursorPosition(0, $viewport.Bottom)
                    return $selected
                }
                'Escape' {
                    [Console]::SetCursorPosition(0, $viewport.Bottom)
                    return -1
                }
            }
            if ($selected -eq $prev) { continue }
            Write-MenuLine -Row ($viewport.BodyTop + $prev)     -Label $Items[$prev].Label     -Role $Items[$prev].Role     -Selected $false -IsHeader $Items[$prev].IsHeader -InnerWidth $viewport.InnerWidth
            Write-MenuLine -Row ($viewport.BodyTop + $selected) -Label $Items[$selected].Label -Role $Items[$selected].Role -Selected $true  -IsHeader $Items[$selected].IsHeader -InnerWidth $viewport.InnerWidth
        }
    } finally {
        [Console]::CursorVisible = $true
    }
}

function Format-WorktreeLabel {
    param([string]$Branch, [string]$Path)

    $maxLineLen = [Console]::WindowWidth - 5  # "  > " prefix + trailing gap
    $branchLbl  = "{0,-28}" -f "[$Branch]"
    $budget     = $maxLineLen - $branchLbl.Length - 1
    if ($budget -lt 4) {
        $Path = [char]0x2026
    } elseif ($Path.Length -gt $budget) {
        $Path = ([char]0x2026) + $Path.Substring($Path.Length - ($budget - 1))
    }
    "$branchLbl $Path"
}

function Select-Worktree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ContextDir,
        [switch]$ClearScreen
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

    $items = foreach ($e in $entries) {
        [pscustomobject]@{
            Label    = (Format-WorktreeLabel -Branch $e.Branch -Path $e.Path)
            IsHeader = $false
        }
    }
    $idx = Invoke-MenuLoop -Header 'Pick a worktree (Up/Down, Enter = confirm, Esc = cancel):' -Items $items -ClearScreen:$false
    if ($idx -lt 0) { throw "Cancelled" }
    return $entries[$idx].Path
}

function Select-Action {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]                 [string]$Header,
        [Parameter(Mandatory)][AllowEmptyCollection()] $Items,
        [int]$Default = 0,
        [switch]$ClearScreen
    )

    if ($Items.Count -eq 0) { throw "No items to select from" }

    # Normalise: every item ends up with Label + IsHeader. Missing
    # IsHeader defaults to false (regular selectable entry).
    $normalised = foreach ($it in $Items) {
        $role = $it.PSObject.Properties['Role']
        [pscustomobject]@{
            Label    = [string]$it.Label
            IsHeader = (Test-MenuHeader $it)
            Role     = if ($null -ne $role) { [string]$role.Value } else { $null }
        }
    }

    $idx = Invoke-MenuLoop -Header $Header -Items $normalised -Default $Default -ClearScreen:$ClearScreen
    if ($idx -lt 0) { throw "Cancelled" }
    return $Items[$idx].Value
}
