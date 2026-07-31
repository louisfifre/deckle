# Pure grid geometry shared by interactive and status surfaces.
function New-GridPlan {
    param(
        [Parameter(Mandatory)][object[]]$Rows,
        [ValidateRange(0, 40)]
        [int]$CategoryWidth = $script:MenuCategoryWidth
    )

    $body = @()
    $selectableRows = @()
    $prefixWidth = 0
    $columnLabels = @{}
    $trailingWidth = 0

    foreach ($row in $Rows) {
        if ($row.ContainsKey('Title')) {
            $body += @{ Kind = 'title'; Text = [string]$row['Title'] }
            continue
        }
        if ($row.ContainsKey('Text')) {
            $body += @{ Kind = 'text'; Text = [string]$row['Text'] }
            continue
        }
        if (-not $row.ContainsKey('Cells')) {
            $body += @{ Kind = 'blank' }
            continue
        }

        $prefix = if ($row.ContainsKey('Prefix') -and $row['Prefix']) { [string]$row['Prefix'] } else { '' }
        $prefixWidth = [Math]::Max($prefixWidth, $prefix.Length)
        $cells = @($row['Cells'])
        if ($cells.Count -eq 0) { throw 'New-GridPlan: a row has empty Cells; use a Blank row for separators.' }
        $fullWidth = $row.ContainsKey('FullWidth') -and [bool]$row['FullWidth']
        if ($fullWidth -and $cells.Count -ne 1) {
            throw 'New-GridPlan: FullWidth rows require exactly one cell.'
        }

        $columnOffset = if ($row.ContainsKey('ColumnOffset')) { [int]$row['ColumnOffset'] } else { 0 }
        for ($cellIndex = 0; $cellIndex -lt $cells.Count; $cellIndex++) {
            $column = $columnOffset + $cellIndex
            $labelWidth = ([string]$cells[$cellIndex].Label).Length + 2
            if (-not $columnLabels.ContainsKey($column) -or $labelWidth -gt $columnLabels[$column]) {
                $columnLabels[$column] = $labelWidth
            }
        }

        $trailingCell = if ($row.ContainsKey('TrailingCell')) { $row['TrailingCell'] } else { $null }
        if ($trailingCell) {
            $trailingWidth = [Math]::Max($trailingWidth, ([string]$trailingCell.Label).Length)
        }

        $body += @{
            Kind = 'row'; Prefix = $prefix; Cells = $cells; ColumnOffset = $columnOffset
            TrailingCell = $trailingCell; FullWidth = $fullWidth
        }
        $selectableRows += @{
            BodyIndex = $body.Count - 1; CellCount = $cells.Count; ColumnOffset = $columnOffset; HasTrailing = [bool]$trailingCell
        }
    }

    if ($CategoryWidth -gt 0) { $prefixWidth = $CategoryWidth }
    if ($prefixWidth -gt 0) { $prefixWidth += $script:MenuGridGap }
    $occupiedColumnCount = if ($columnLabels.Count -gt 0) {
        [int](@($columnLabels.Keys | Measure-Object -Maximum).Maximum) + 1
    } else {
        1
    }
    $columnCount = Get-GridColumnCount -OccupiedColumnCount $occupiedColumnCount
    foreach ($selection in $selectableRows) {
        if ($selection.HasTrailing -and ($selection.ColumnOffset + $selection.CellCount) -ne $columnCount) {
            throw 'New-GridPlan: TrailingCell requires regular cells through the final grid column.'
        }
    }

    return [pscustomobject]@{
        Body           = @($body)
        SelectableRows = @($selectableRows)
        PrefixWidth    = $prefixWidth
        ColumnCount    = $columnCount
        TrailingWidth  = $trailingWidth
        TrailingGap    = $(if ($trailingWidth -gt 0) { $script:MenuGridGap } else { 0 })
    }
}

function Get-GridColumnWidths {
    param(
        [Parameter(Mandatory)][int]$ContentWidth,
        [Parameter(Mandatory)][int]$PrefixWidth,
        [Parameter(Mandatory)][int]$ColumnCount
    )

    $available = [Math]::Max($ColumnCount, $ContentWidth - $script:MenuRowInset - $PrefixWidth)
    $baseWidth = [Math]::Max(1, [Math]::Floor($available / $ColumnCount))
    $remainder = $available - ($baseWidth * $ColumnCount)
    $widths = @{}
    for ($column = 0; $column -lt $ColumnCount; $column++) {
        $widths[$column] = $baseWidth + $(if ($column -lt $remainder) { 1 } else { 0 })
    }
    return $widths
}

function Get-GridColumnCount {
    param(
        [Parameter(Mandatory)][ValidateRange(1, 40)]
        [int]$OccupiedColumnCount
    )

    return [Math]::Max($script:MenuActionColumnCount, $OccupiedColumnCount)
}

function Get-GridColumnForRow {
    param(
        [Parameter(Mandatory)][int]$PreferredColumn,
        [Parameter(Mandatory)][int]$ColumnOffset,
        [Parameter(Mandatory)][int]$CellCount,
        [bool]$HasTrailing = $false,
        [int]$TrailingColumn = -1
    )

    $lastColumn = if ($HasTrailing) { $TrailingColumn } else { $ColumnOffset + $CellCount - 1 }
    return [Math]::Min($lastColumn, [Math]::Max($ColumnOffset, $PreferredColumn))
}

function Get-GridResultOffset {
    param(
        [Parameter(Mandatory)][int]$Current,
        [Parameter(Mandatory)][int]$PageSize,
        [Parameter(Mandatory)][int]$LineCount,
        [ValidateSet('Previous', 'Next', 'Current', 'First', 'Last')]
        [string]$Direction
    )

    if ($PageSize -le 0 -or $LineCount -le 0) { return 0 }
    $maximum = [Math]::Floor(($LineCount - 1) / $PageSize) * $PageSize
    $currentPage = [Math]::Floor([Math]::Max(0, $Current) / $PageSize) * $PageSize
    $candidate = switch ($Direction) {
        'Previous' { $currentPage - $PageSize }
        'Next' { $currentPage + $PageSize }
        'First' { 0 }
        'Last' { $maximum }
        default { $currentPage }
    }
    return [Math]::Min($maximum, [Math]::Max(0, $candidate))
}

function Get-GridResultPage {
    param(
        [Parameter(Mandatory)][int]$Offset,
        [Parameter(Mandatory)][int]$PageSize,
        [Parameter(Mandatory)][int]$LineCount
    )

    if ($PageSize -le 0 -or $LineCount -le 0) {
        return [pscustomobject]@{ Number = 1; Count = 1 }
    }
    return [pscustomobject]@{
        Number = [Math]::Floor([Math]::Max(0, $Offset) / $PageSize) + 1
        Count = [Math]::Ceiling($LineCount / [double]$PageSize)
    }
}

function New-GridBodyLayout {
    param(
        [Parameter(Mandatory)][object[]]$CommandBody,
        [string]$ResultTitle,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    $body = @($CommandBody)
    $resultRowCount = 0
    if ($ResultTitle) {
        $bodyCapacity = Get-MenuBodyCapacity -BannerStyle $BannerStyle
        $minimumBodyCount = $body.Count + 3
        $effectiveCapacity = [Math]::Max($minimumBodyCount, $bodyCapacity)
        $resultRowCount = [Math]::Max(1, $effectiveCapacity - $body.Count - 2)
        $body += @{ Kind = 'blank' }
        $body += @{ Kind = 'result-title'; Text = $ResultTitle }
        for ($slot = 0; $slot -lt $resultRowCount; $slot++) {
            $body += @{ Kind = 'result'; Slot = $slot }
        }
    }

    return [pscustomobject]@{ Body = @($body); ResultRowCount = $resultRowCount }
}

function Get-GridNavigationCommands {
    param(
        [ValidateSet('Run', 'Select', 'Confirm')]
        [string]$Interaction = 'Run',
        [ValidateSet('Cancel', 'Ignore')]
        [string]$EscapeAction = 'Cancel'
    )

    $parts = switch ($Interaction) {
        'Confirm' { @('←→ move', 'Enter confirm') }
        'Select'  { @('↑↓←→ move', 'Enter select') }
        default   { @('↑↓←→ move', 'Enter run') }
    }
    if ($EscapeAction -eq 'Cancel') {
        $parts += $(if ($Interaction -eq 'Confirm') { 'Esc cancel' } else { 'Esc back' })
    } else {
        $parts += 'Ctrl+C quit'
    }
    return $parts -join '   '
}

function Get-GridPagingFooter {
    param([switch]$HasPages)

    if (-not $HasPages) { return '' }
    return 'Wheel/PgUp/PgDn pages   Home/End first/latest'
}
