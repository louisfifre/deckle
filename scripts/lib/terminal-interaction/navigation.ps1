# Focus and paging transitions over a rendered frame.

function Get-TerminalEnabledPlacements {
    param([Parameter(Mandatory)][object]$Frame)

    return @($Frame.Targets | Where-Object { $_.Target.Enabled })
}

function Get-TerminalInitialFocus {
    param([Parameter(Mandatory)][object]$Frame)

    if ($Frame.DefaultTargetId) {
        $declared = @(Get-TerminalEnabledPlacements -Frame $Frame | Where-Object { $_.TargetId -eq $Frame.DefaultTargetId } | Select-Object -First 1)
        if ($declared.Count -gt 0) { return $declared[0].TargetId }
    }
    $first = @(Get-TerminalEnabledPlacements -Frame $Frame | Sort-Object Y, X | Select-Object -First 1)
    if ($first.Count -eq 0) { return $null }
    return $first[0].TargetId
}

function Move-TerminalFocus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Frame,
        [string]$CurrentTargetId,
        [Parameter(Mandatory)][ValidateSet('Up', 'Down', 'Left', 'Right')][string]$Direction
    )

    $placements = @(Get-TerminalEnabledPlacements -Frame $Frame)
    if ($placements.Count -eq 0) { return $null }
    $current = @($placements | Where-Object { $_.TargetId -eq $CurrentTargetId } | Select-Object -First 1)
    if ($current.Count -eq 0) { return Get-TerminalInitialFocus -Frame $Frame }
    $current = $current[0]

    $best = $null
    $bestPrimary = [double]::PositiveInfinity
    $bestSecondary = [double]::PositiveInfinity
    foreach ($candidate in $placements) {
        if ($candidate.TargetId -eq $current.TargetId) { continue }
        $primary = 0.0
        $secondary = 0.0
        $eligible = $true
        switch ($Direction) {
            'Left' {
                if ($candidate.Y -ne $current.Y -or $candidate.CenterX -ge $current.CenterX) { $eligible = $false }
                else {
                    $primary = $current.CenterX - $candidate.CenterX
                    $secondary = 0
                }
            }
            'Right' {
                if ($candidate.Y -ne $current.Y -or $candidate.CenterX -le $current.CenterX) { $eligible = $false }
                else {
                    $primary = $candidate.CenterX - $current.CenterX
                    $secondary = 0
                }
            }
            'Up' {
                if ($candidate.Y -ge $current.Y) { $eligible = $false }
                else {
                    $primary = $current.Y - $candidate.Y
                    $secondary = [Math]::Abs($candidate.CenterX - $current.CenterX)
                }
            }
            'Down' {
                if ($candidate.Y -le $current.Y) { $eligible = $false }
                else {
                    $primary = $candidate.Y - $current.Y
                    $secondary = [Math]::Abs($candidate.CenterX - $current.CenterX)
                }
            }
        }
        if (-not $eligible) { continue }
        if ($primary -lt $bestPrimary -or ($primary -eq $bestPrimary -and $secondary -lt $bestSecondary)) {
            $best = $candidate
            $bestPrimary = $primary
            $bestSecondary = $secondary
        }
    }

    if ($null -eq $best) { return $current.TargetId }
    return $best.TargetId
}

function Get-TerminalFocusedTarget {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [string]$FocusedTargetId
    )

    $match = @($Frame.Targets | Where-Object { $_.TargetId -eq $FocusedTargetId } | Select-Object -First 1)
    if ($match.Count -eq 0) { return $null }
    return $match[0].Target
}

function Move-TerminalJournalPage {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][int]$CurrentOffset,
        [Parameter(Mandatory)][ValidateSet('Previous', 'Next', 'First', 'Last')][string]$Direction
    )

    $pageSize = [Math]::Max(1, $Frame.JournalPageSize)
    $lastOffset = [Math]::Max(0, $Frame.JournalLineCount - $pageSize)
    switch ($Direction) {
        'Previous' { return [Math]::Max(0, $CurrentOffset - $pageSize) }
        'Next' { return [Math]::Min($lastOffset, $CurrentOffset + $pageSize) }
        'First' { return 0 }
        'Last' { return $lastOffset }
    }
}

function Move-TerminalBodyPage {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][int]$CurrentOffset,
        [Parameter(Mandatory)][ValidateSet('Previous', 'Next', 'First', 'Last')][string]$Direction
    )

    $pageSize = [Math]::Max(1, $Frame.BodyPageSize)
    $lastOffset = [Math]::Max(0, $Frame.BodyLineCount - $pageSize)
    switch ($Direction) {
        'Previous' { return [Math]::Max(0, $CurrentOffset - $pageSize) }
        'Next' { return [Math]::Min($lastOffset, $CurrentOffset + $pageSize) }
        'First' { return 0 }
        'Last' { return $lastOffset }
    }
}
