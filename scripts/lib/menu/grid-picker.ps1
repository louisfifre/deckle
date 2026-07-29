# Two-dimensional grid picker.
function Write-GridLine {
    param(
        [int]$Top, [int]$Index, [object[]]$Body, [hashtable]$ColW, [int]$PrefixW,
        [int]$InnerWidth, [int]$ContentWidth,
        [int]$ActiveBodyIndex, [int]$ActiveCol,
        [int]$TrailingWidth = 0, [int]$TrailingGap = 0, [int]$TrailingColumn = -1,
        [string[]]$ResultLines = @(), [int]$ResultOffset = 0
    )
    $entry = $Body[$Index]
    Write-MenuLinePrefix -Row ($Top + $Index)
    $written = 0

    if ($entry.Kind -eq 'title') {
        $label = ' ' + ([string]$entry.Text).ToUpperInvariant() + ' '
        Write-MenuContentSegment -Text $label -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor Magenta -BackgroundColor $null
        $rule = New-MenuRule -MaxWidth ($ContentWidth - $written) -Style Section
        Write-MenuContentSegment -Text $rule -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor Gray -BackgroundColor $null
    } elseif ($entry.Kind -eq 'blank') {
        # Keep the row inside the frame intentionally empty.
    } elseif ($entry.Kind -eq 'result') {
        $resultIndex = $ResultOffset + [int]$entry.Slot
        $text = if ($resultIndex -lt $ResultLines.Count) { [string]$ResultLines[$resultIndex] } else { '' }
        Write-MenuContentSegment -Text ('  ' + $text) -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor Gray -BackgroundColor $null
    } else {
        # 'row'
        Write-MenuContentSegment -Text '  ' -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor $null -BackgroundColor $null
        if ($PrefixW -gt 0) {
            $p = ([string]$entry.Prefix).PadRight($PrefixW)
            Write-MenuContentSegment -Text $p -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor Cyan -BackgroundColor $null
        }
        $columnOffset = [int]$entry.ColumnOffset
        for ($column = 0; $column -lt $columnOffset; $column++) {
            Write-MenuContentSegment -Text (' ' * $ColW[$column]) -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor $null -BackgroundColor $null
        }
        for ($c = 0; $c -lt $entry.Cells.Count; $c++) {
            $cell = $entry.Cells[$c]
            $column = $columnOffset + $c
            $selected = (($Index -eq $ActiveBodyIndex) -and ($column -eq $ActiveCol))
            $label = [string]$cell.Label
            $cellWidth = $ColW[$column]
            if ($entry.TrailingCell -and $c -eq ($entry.Cells.Count - 1)) {
                $cellWidth = [Math]::Max(1, $cellWidth - $TrailingGap - $TrailingWidth)
            }
            $txt = Limit-MenuText -Text "  $label" -Width $cellWidth
            $alignment = if ($cell -is [hashtable] -and $cell.ContainsKey('Align')) { [string]$cell['Align'] } else { '' }
            $txt = if ($alignment -eq 'Right') { $txt.PadLeft($cellWidth) } else { $txt.PadRight($cellWidth) }
            $role = Get-MenuCellRole -Cell $cell
            $colors = Get-MenuRoleColor -Role $role -Selected:$selected
            Write-MenuContentSegment -Text $txt -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor $colors.Foreground -BackgroundColor $colors.Background
        }
        if ($entry.TrailingCell) {
            Write-MenuContentSegment -Text (' ' * $TrailingGap) -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor $null -BackgroundColor $null
            $cell = $entry.TrailingCell
            $selected = (($Index -eq $ActiveBodyIndex) -and ($ActiveCol -eq $TrailingColumn))
            $txt = (Limit-MenuText -Text ([string]$cell.Label) -Width $TrailingWidth).PadLeft($TrailingWidth)
            $role = Get-MenuCellRole -Cell $cell
            $colors = Get-MenuRoleColor -Role $role -Selected:$selected
            Write-MenuContentSegment -Text $txt -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor $colors.Foreground -BackgroundColor $colors.Background
        }
    }
    Write-MenuLineRemainder -InnerWidth $InnerWidth -Written $written
}

function Invoke-GridLoop {
    param(
        [string]$Header,
        [object[]]$Rows,
        [string]$Footer,
        [int]$StartSel = 0,
        [int]$StartCol = 0,
        [ValidateSet('Cancel', 'Ignore')]
        [string]$EscapeAction = 'Cancel',
        [switch]$ClearScreen,
        [ValidateSet('Full', 'Compact')]
        [string]$BannerStyle = 'Full',
        [string]$ResultTitle,
        [string[]]$ResultLines = @()
    )
    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
        throw 'Invoke-GridLoop requires an interactive console (input or output is redirected).'
    }

    $GAP = 3
    $body = @()
    $sel  = @()          # selectable rows: @{ BodyIndex; NCells; ColumnOffset }
    $prefixW = 0
    $colW = @{}
    $trailingW = 0

    foreach ($r in $Rows) {
        if ($r.ContainsKey('Title')) {
            $body += @{ Kind = 'title'; Text = [string]$r['Title'] }
        } elseif ($r.ContainsKey('Cells')) {
            $prefix = if ($r.ContainsKey('Prefix') -and $r['Prefix']) { [string]$r['Prefix'] } else { '' }
            if ($prefix.Length -gt $prefixW) { $prefixW = $prefix.Length }
            $cells = @($r['Cells'])
            $columnOffset = if ($r.ContainsKey('ColumnOffset')) { [int]$r['ColumnOffset'] } else { 0 }
            if ($cells.Count -eq 0) { throw 'Invoke-GridLoop: a row has empty Cells; use a Blank row for separators.' }
            for ($c = 0; $c -lt $cells.Count; $c++) {
                $column = $columnOffset + $c
                $len = ([string]$cells[$c].Label).Length + 2
                if (-not $colW.ContainsKey($column) -or $len -gt $colW[$column]) { $colW[$column] = $len }
            }
            $trailingCell = if ($r.ContainsKey('TrailingCell')) { $r['TrailingCell'] } else { $null }
            if ($trailingCell) {
                $trailingW = [Math]::Max($trailingW, ([string]$trailingCell.Label).Length)
            }
            $body += @{ Kind = 'row'; Prefix = $prefix; Cells = $cells; ColumnOffset = $columnOffset; TrailingCell = $trailingCell }
            $sel  += @{ BodyIndex = ($body.Count - 1); NCells = $cells.Count; ColumnOffset = $columnOffset; HasTrailing = [bool]$trailingCell }
        } else {
            $body += @{ Kind = 'blank' }
        }
    }
    if ($sel.Count -eq 0) { return $null }
    if ($prefixW -gt 0) { $prefixW += $GAP }
    $columnCount = (@($colW.Keys | Measure-Object -Maximum).Maximum + 1)
    foreach ($selection in $sel) {
        if ($selection.HasTrailing -and ($selection.ColumnOffset + $selection.NCells) -ne $columnCount) {
            throw 'Invoke-GridLoop: TrailingCell requires regular cells through the final grid column.'
        }
    }
    $trailingGap = if ($trailingW -gt 0) { $GAP } else { 0 }
    $metrics = Get-MenuMetrics
    $colW = Get-GridColumnWidths -ContentWidth $metrics.ContentWidth -PrefixWidth $prefixW -ColumnCount $columnCount

    $commandBody = @($body)
    $resultLinesArray = @($ResultLines)
    $layout = New-GridBodyLayout -CommandBody $commandBody -ResultTitle $ResultTitle -BannerStyle $BannerStyle
    $body = $layout.Body
    $resultRowCount = $layout.ResultRowCount

    $resultOffset = 0
    $selIdx = [Math]::Min([Math]::Max($StartSel, 0), $sel.Count - 1)
    $colIdx = Get-GridColumnForRow -CurrentColumn $StartCol -ColumnOffset $sel[$selIdx].ColumnOffset -CellCount $sel[$selIdx].NCells -HasTrailing $sel[$selIdx].HasTrailing -TrailingColumn $columnCount

    $viewport = New-MenuViewport -Header $Header -Footer $Footer -BodyCount $body.Count -ClearScreen:$ClearScreen -BannerStyle $BannerStyle
    $metrics = Get-MenuMetrics
    $colW = Get-GridColumnWidths -ContentWidth $metrics.ContentWidth -PrefixWidth $prefixW -ColumnCount $columnCount
    $top = $viewport.BodyTop

    $render = {
        for ($i = 0; $i -lt $body.Count; $i++) {
            Write-GridLine -Top $top -Index $i -Body $body -ColW $colW -PrefixW $prefixW `
                -InnerWidth $viewport.InnerWidth -ContentWidth $viewport.ContentWidth `
                -ActiveBodyIndex $sel[$selIdx].BodyIndex -ActiveCol $colIdx `
                -TrailingWidth $trailingW -TrailingGap $trailingGap -TrailingColumn $columnCount `
                -ResultLines $resultLinesArray -ResultOffset $resultOffset
        }
    }

    [Console]::CursorVisible = $false
    try {
        & $render
        while ($true) {
            $key = [Console]::ReadKey($true)
            $geometryChanged = $false
            $currentMetrics = Get-MenuMetrics
            if ($currentMetrics.TerminalWidth -ne $metrics.TerminalWidth -or $currentMetrics.WindowHeight -ne $metrics.WindowHeight) {
                $layout = New-GridBodyLayout -CommandBody $commandBody -ResultTitle $ResultTitle -BannerStyle $BannerStyle
                $body = $layout.Body
                $resultRowCount = $layout.ResultRowCount
                $resultOffset = Get-GridResultOffset -Current $resultOffset -PageSize $resultRowCount -LineCount $resultLinesArray.Count -Direction Current
                $metrics = Get-MenuMetrics
                $colW = Get-GridColumnWidths -ContentWidth $metrics.ContentWidth -PrefixWidth $prefixW -ColumnCount $columnCount
                $viewport = New-MenuViewport -Header $Header -Footer $Footer -BodyCount $body.Count -ClearScreen -BannerStyle $BannerStyle
                $metrics = Get-MenuMetrics
                $colW = Get-GridColumnWidths -ContentWidth $metrics.ContentWidth -PrefixWidth $prefixW -ColumnCount $columnCount
                $top = $viewport.BodyTop
                $geometryChanged = $true
            }
            $prevSelIdx = $selIdx
            $prevColIdx = $colIdx
            switch ($key.Key) {
                'UpArrow' {
                    if ($selIdx -gt 0) {
                        $selIdx--
                        $colIdx = Get-GridColumnForRow -CurrentColumn $colIdx -ColumnOffset $sel[$selIdx].ColumnOffset -CellCount $sel[$selIdx].NCells -HasTrailing $sel[$selIdx].HasTrailing -TrailingColumn $columnCount
                    }
                }
                'DownArrow' {
                    if ($selIdx -lt $sel.Count - 1) {
                        $selIdx++
                        $colIdx = Get-GridColumnForRow -CurrentColumn $colIdx -ColumnOffset $sel[$selIdx].ColumnOffset -CellCount $sel[$selIdx].NCells -HasTrailing $sel[$selIdx].HasTrailing -TrailingColumn $columnCount
                    }
                }
                'LeftArrow'  { if ($colIdx -gt $sel[$selIdx].ColumnOffset) { $colIdx-- } }
                'RightArrow' {
                    $lastColumn = if ($sel[$selIdx].HasTrailing) { $columnCount } else { $sel[$selIdx].ColumnOffset + $sel[$selIdx].NCells - 1 }
                    if ($colIdx -lt $lastColumn) { $colIdx++ }
                }
                'PageUp' {
                    $resultOffset = Get-GridResultOffset -Current $resultOffset -PageSize $resultRowCount -LineCount $resultLinesArray.Count -Direction Previous
                }
                'PageDown' {
                    $resultOffset = Get-GridResultOffset -Current $resultOffset -PageSize $resultRowCount -LineCount $resultLinesArray.Count -Direction Next
                }
                'Enter' {
                    Set-MenuCursorPosition -Left 0 -Top $viewport.Bottom
                    $selectedRow = $body[$sel[$selIdx].BodyIndex]
                    if ($selectedRow.TrailingCell -and $colIdx -eq $columnCount) {
                        return $selectedRow.TrailingCell.Value
                    }
                    $localColumn = $colIdx - $sel[$selIdx].ColumnOffset
                    return $selectedRow.Cells[$localColumn].Value
                }
                'Escape' {
                    if ($EscapeAction -eq 'Ignore') { continue }
                    Set-MenuCursorPosition -Left 0 -Top $viewport.Bottom
                    return $null
                }
            }
            if ($geometryChanged -or $selIdx -ne $prevSelIdx -or $colIdx -ne $prevColIdx -or $key.Key -in @('PageUp', 'PageDown')) { & $render }
        }
    } finally {
        [Console]::CursorVisible = $true
    }
}

function Select-Grid {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Rows,
        [string]$Footer,
        [int]$StartSel = 0,
        [int]$StartCol = 0,
        [ValidateSet('Cancel', 'Ignore')]
        [string]$EscapeAction = 'Cancel',
        [switch]$ClearScreen,
        [ValidateSet('Full', 'Compact')]
        [string]$BannerStyle = 'Full',
        [string]$ResultTitle,
        [string[]]$ResultLines = @()
    )
    return Invoke-GridLoop -Header $Header -Rows $Rows -Footer $Footer -StartSel $StartSel -StartCol $StartCol -EscapeAction $EscapeAction -ClearScreen:$ClearScreen -BannerStyle $BannerStyle -ResultTitle $ResultTitle -ResultLines $ResultLines
}

function Get-GridColumnWidths {
    param(
        [Parameter(Mandatory)][int]$ContentWidth,
        [Parameter(Mandatory)][int]$PrefixWidth,
        [Parameter(Mandatory)][int]$ColumnCount
    )

    $available = [Math]::Max($ColumnCount, $ContentWidth - 2 - $PrefixWidth)
    $baseWidth = [Math]::Max(1, [Math]::Floor($available / $ColumnCount))
    $remainder = $available - ($baseWidth * $ColumnCount)
    $widths = @{}
    for ($column = 0; $column -lt $ColumnCount; $column++) {
        $widths[$column] = $baseWidth + $(if ($column -lt $remainder) { 1 } else { 0 })
    }
    return $widths
}

function Get-GridResultOffset {
    param(
        [Parameter(Mandatory)][int]$Current,
        [Parameter(Mandatory)][int]$PageSize,
        [Parameter(Mandatory)][int]$LineCount,
        [ValidateSet('Previous', 'Next', 'Current')]
        [string]$Direction
    )

    $maximum = [Math]::Max(0, $LineCount - $PageSize)
    $candidate = switch ($Direction) {
        'Previous' { $Current - $PageSize }
        'Next' { $Current + $PageSize }
        default { $Current }
    }
    return [Math]::Min($maximum, [Math]::Max(0, $candidate))
}

function Get-GridColumnForRow {
    param(
        [Parameter(Mandatory)][int]$CurrentColumn,
        [Parameter(Mandatory)][int]$ColumnOffset,
        [Parameter(Mandatory)][int]$CellCount,
        [bool]$HasTrailing = $false,
        [int]$TrailingColumn = -1
    )

    $lastColumn = if ($HasTrailing) { $TrailingColumn } else { $ColumnOffset + $CellCount - 1 }
    return [Math]::Min($lastColumn, [Math]::Max($ColumnOffset, $CurrentColumn))
}

function New-GridBodyLayout {
    param(
        [Parameter(Mandatory)][object[]]$CommandBody,
        [string]$ResultTitle,
        [ValidateSet('Full', 'Compact')]
        [string]$BannerStyle = 'Full'
    )

    $body = @($CommandBody)
    $resultRowCount = 0
    if ($ResultTitle) {
        $bodyCapacity = Get-MenuBodyCapacity -BannerStyle $BannerStyle
        $minimumBodyCount = $body.Count + 3
        $effectiveCapacity = [Math]::Max($minimumBodyCount, $bodyCapacity)
        $resultRowCount = [Math]::Max(1, $effectiveCapacity - $body.Count - 2)
        $body += @{ Kind = 'blank' }
        $body += @{ Kind = 'title'; Text = $ResultTitle }
        for ($slot = 0; $slot -lt $resultRowCount; $slot++) {
            $body += @{ Kind = 'result'; Slot = $slot }
        }
    }

    return [pscustomobject]@{ Body = @($body); ResultRowCount = $resultRowCount }
}
