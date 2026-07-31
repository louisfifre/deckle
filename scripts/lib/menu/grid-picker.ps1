# Two-dimensional grid picker.
function Get-MenuResultLinePresentation {
    param([AllowNull()]$Line)

    $textProperty = if ($null -ne $Line) { $Line.PSObject.Properties['Text'] } else { $null }
    $colorProperty = if ($null -ne $Line) { $Line.PSObject.Properties['ForegroundColor'] } else { $null }
    $segmentsProperty = if ($null -ne $Line) { $Line.PSObject.Properties['Segments'] } else { $null }
    $text = if ($null -ne $textProperty) { [string]$textProperty.Value } else { [string]$Line }
    $color = if ($null -ne $colorProperty) { $colorProperty.Value } else { $null }
    $segments = if ($null -ne $segmentsProperty -and @($segmentsProperty.Value).Count -gt 0) {
        @($segmentsProperty.Value)
    } else {
        @([pscustomobject]@{ Text = $text; ForegroundColor = $color })
    }
    return [pscustomobject]@{
        Text            = $text
        ForegroundColor = $color
        Segments        = $segments
    }
}

function Set-GridSelectionState {
    param(
        [hashtable]$State,
        [Parameter(Mandatory)][int]$Index,
        [Parameter(Mandatory)][int]$PreferredColumn
    )

    if (-not $State) { return }
    $State.Index = $Index
    $State.PreferredColumn = $PreferredColumn
}

function Get-GridSelectionPosition {
    param(
        [Parameter(Mandatory)][object[]]$SelectableRows,
        [int]$StartIndex = 0,
        [int]$StartColumn = 0,
        [Parameter(Mandatory)][int]$TrailingColumn,
        [hashtable]$State
    )

    $selectionIndex = if ($State -and $State.ContainsKey('Index')) { [int]$State.Index } else { $StartIndex }
    $preferredColumn = if ($State -and $State.ContainsKey('PreferredColumn')) { [int]$State.PreferredColumn } else { $StartColumn }
    $selectionIndex = [Math]::Min([Math]::Max($selectionIndex, 0), $SelectableRows.Count - 1)
    $row = $SelectableRows[$selectionIndex]
    $activeColumn = Get-GridColumnForRow -PreferredColumn $preferredColumn -ColumnOffset $row.ColumnOffset -CellCount $row.CellCount -HasTrailing $row.HasTrailing -TrailingColumn $TrailingColumn
    return [pscustomobject]@{
        Index = $selectionIndex
        PreferredColumn = $preferredColumn
        ActiveColumn = $activeColumn
    }
}

function Write-GridLine {
    param(
        [int]$Top, [int]$Index, [object[]]$Body, [hashtable]$ColW, [int]$PrefixW,
        [int]$InnerWidth, [int]$ContentWidth,
        [int]$ActiveBodyIndex, [int]$ActiveCol,
        [int]$TrailingWidth = 0, [int]$TrailingGap = 0, [int]$TrailingColumn = -1,
        [object[]]$ResultLines = @(), [int]$ResultOffset = 0,
        [int]$ResultPage = 1, [int]$ResultPageCount = 1
    )
    $entry = $Body[$Index]
    Write-MenuLinePrefix -Row ($Top + $Index)
    $written = 0

    if ($entry.Kind -in @('title', 'result-title')) {
        $titleText = [string]$entry.Text
        if ($entry.Kind -eq 'result-title' -and $ResultPageCount -gt 1) {
            $titleText += "  ·  Page $ResultPage/$ResultPageCount"
        }
        $label = $titleText.ToUpperInvariant() + ' '
        Write-MenuContentSegment -Text $label -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor Magenta -BackgroundColor $null
        $rule = New-MenuRule -MaxWidth ($ContentWidth - $written) -Style Section
        Write-MenuContentSegment -Text $rule -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor Gray -BackgroundColor $null
    } elseif ($entry.Kind -eq 'blank') {
        # Keep the row inside the frame intentionally empty.
    } elseif ($entry.Kind -eq 'text') {
        Write-MenuContentSegment -Text ('  ' + [string]$entry.Text) -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor DarkGray -BackgroundColor $null
    } elseif ($entry.Kind -eq 'result') {
        $resultIndex = $ResultOffset + [int]$entry.Slot
        $line = if ($resultIndex -lt $ResultLines.Count) { $ResultLines[$resultIndex] } else { '' }
        $presentation = Get-MenuResultLinePresentation -Line $line
        for ($segmentIndex = 0; $segmentIndex -lt $presentation.Segments.Count; $segmentIndex++) {
            $segment = $presentation.Segments[$segmentIndex]
            $segmentText = if ($segmentIndex -eq 0) { '  ' + [string]$segment.Text } else { [string]$segment.Text }
            Write-MenuContentSegment -Text $segmentText -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor $segment.ForegroundColor -BackgroundColor $null
        }
    } else {
        # 'row'
        Write-MenuContentSegment -Text (' ' * $script:MenuRowInset) -Written ([ref]$written) -InnerWidth $ContentWidth -ForegroundColor $null -BackgroundColor $null
        if ($PrefixW -gt 0) {
            $prefixContentWidth = [Math]::Max(0, $PrefixW - $script:MenuGridGap)
            $p = (Limit-MenuText -Text ([string]$entry.Prefix) -Width $prefixContentWidth).PadRight($PrefixW)
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
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact',
        [ValidateRange(0, 40)]
        [int]$CategoryWidth = $script:MenuCategoryWidth,
        [string]$ResultTitle,
        [object[]]$ResultLines = @(),
        [switch]$ResultFollowTail,
        [ValidateSet('Run', 'Select', 'Confirm')]
        [string]$Interaction = 'Run',
        [hashtable]$SelectionState
    )
    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
        throw 'Invoke-GridLoop requires an interactive console (input or output is redirected).'
    }

    $grid = New-GridPlan -Rows $Rows -CategoryWidth $CategoryWidth
    $body = @($grid.Body)
    $sel = @($grid.SelectableRows)
    if ($sel.Count -eq 0) { return $null }
    $prefixW = $grid.PrefixWidth
    $columnCount = $grid.ColumnCount
    $trailingW = $grid.TrailingWidth
    $trailingGap = $grid.TrailingGap
    $metrics = Get-MenuMetrics
    $colW = Get-GridColumnWidths -ContentWidth $metrics.ContentWidth -PrefixWidth $prefixW -ColumnCount $columnCount

    $commandBody = @($body)
    $resultLinesArray = @($ResultLines)
    $layout = New-GridBodyLayout -CommandBody $commandBody -ResultTitle $ResultTitle -BannerStyle $BannerStyle
    $body = $layout.Body
    $resultRowCount = $layout.ResultRowCount

    $resultOffset = if ($ResultFollowTail) {
        Get-GridResultOffset -Current 0 -PageSize $resultRowCount -LineCount $resultLinesArray.Count -Direction Last
    } else {
        0
    }
    $selection = Get-GridSelectionPosition -SelectableRows $sel -StartIndex $StartSel -StartColumn $StartCol -TrailingColumn $columnCount -State $SelectionState
    $selIdx = $selection.Index
    $preferredColIdx = $selection.PreferredColumn
    $colIdx = $selection.ActiveColumn
    Set-GridSelectionState -State $SelectionState -Index $selIdx -PreferredColumn $preferredColIdx
    $hasPages = $resultLinesArray.Count -gt $resultRowCount
    $headerCommands = Get-GridNavigationCommands -Interaction $Interaction -EscapeAction $EscapeAction
    if (-not $Footer) { $Footer = Get-GridPagingFooter -HasPages:$hasPages }

    $viewport = New-MenuViewport -Header $Header -HeaderCommands $headerCommands -Footer $Footer -BodyCount $body.Count -ClearScreen:$ClearScreen -BannerStyle $BannerStyle
    $metrics = Get-MenuMetrics
    $colW = Get-GridColumnWidths -ContentWidth $metrics.ContentWidth -PrefixWidth $prefixW -ColumnCount $columnCount
    $top = $viewport.BodyTop

    $render = {
        $page = Get-GridResultPage -Offset $resultOffset -PageSize $resultRowCount -LineCount $resultLinesArray.Count
        for ($i = 0; $i -lt $body.Count; $i++) {
            Write-GridLine -Top $top -Index $i -Body $body -ColW $colW -PrefixW $prefixW `
                -InnerWidth $viewport.InnerWidth -ContentWidth $viewport.ContentWidth `
                -ActiveBodyIndex $sel[$selIdx].BodyIndex -ActiveCol $colIdx `
                -TrailingWidth $trailingW -TrailingGap $trailingGap -TrailingColumn $columnCount `
                -ResultLines $resultLinesArray -ResultOffset $resultOffset `
                -ResultPage $page.Number -ResultPageCount $page.Count
        }
    }

    $usesPointerInput = $resultRowCount -gt 0 -and $resultLinesArray.Count -gt $resultRowCount -and (Start-MenuPointerInput)
    [Console]::CursorVisible = $false
    try {
        & $render
        while ($true) {
            $inputEvent = Read-MenuInputEvent
            $geometryChanged = $false
            $currentMetrics = Get-MenuMetrics
            if ($currentMetrics.TerminalWidth -ne $metrics.TerminalWidth -or $currentMetrics.WindowHeight -ne $metrics.WindowHeight) {
                $layout = New-GridBodyLayout -CommandBody $commandBody -ResultTitle $ResultTitle -BannerStyle $BannerStyle
                $body = $layout.Body
                $resultRowCount = $layout.ResultRowCount
                $resultOffset = Get-GridResultOffset -Current $resultOffset -PageSize $resultRowCount -LineCount $resultLinesArray.Count -Direction Current
                $metrics = Get-MenuMetrics
                $colW = Get-GridColumnWidths -ContentWidth $metrics.ContentWidth -PrefixWidth $prefixW -ColumnCount $columnCount
                $viewport = New-MenuViewport -Header $Header -HeaderCommands $headerCommands -Footer $Footer -BodyCount $body.Count -ClearScreen -BannerStyle $BannerStyle
                $metrics = Get-MenuMetrics
                $colW = Get-GridColumnWidths -ContentWidth $metrics.ContentWidth -PrefixWidth $prefixW -ColumnCount $columnCount
                $top = $viewport.BodyTop
                $geometryChanged = $true
            }
            $prevSelIdx = $selIdx
            $prevColIdx = $colIdx
            $previousResultOffset = $resultOffset
            if ([string]$inputEvent.Kind -eq 'Wheel') {
                $direction = Get-MenuWheelPageDirection -Delta $inputEvent.WheelDelta
                $resultOffset = Get-GridResultOffset -Current $resultOffset -PageSize $resultRowCount -LineCount $resultLinesArray.Count -Direction $direction
            } else {
                $key = $inputEvent.KeyInfo
                if ($key.Key -eq 'C' -and ($key.Modifiers -band [ConsoleModifiers]::Control)) {
                    Set-GridSelectionState -State $SelectionState -Index $selIdx -PreferredColumn $preferredColIdx
                    throw [DeckleMenuQuitException]::new()
                }
                switch ($key.Key) {
                    'UpArrow' {
                        if ($selIdx -gt 0) {
                            $selIdx--
                            $colIdx = Get-GridColumnForRow -PreferredColumn $preferredColIdx -ColumnOffset $sel[$selIdx].ColumnOffset -CellCount $sel[$selIdx].CellCount -HasTrailing $sel[$selIdx].HasTrailing -TrailingColumn $columnCount
                        }
                    }
                    'DownArrow' {
                        if ($selIdx -lt $sel.Count - 1) {
                            $selIdx++
                            $colIdx = Get-GridColumnForRow -PreferredColumn $preferredColIdx -ColumnOffset $sel[$selIdx].ColumnOffset -CellCount $sel[$selIdx].CellCount -HasTrailing $sel[$selIdx].HasTrailing -TrailingColumn $columnCount
                        }
                    }
                    'LeftArrow'  {
                        if ($colIdx -gt $sel[$selIdx].ColumnOffset) {
                            $colIdx--
                            $preferredColIdx = $colIdx
                        }
                    }
                    'RightArrow' {
                        $lastColumn = if ($sel[$selIdx].HasTrailing) { $columnCount } else { $sel[$selIdx].ColumnOffset + $sel[$selIdx].CellCount - 1 }
                        if ($colIdx -lt $lastColumn) {
                            $colIdx++
                            $preferredColIdx = $colIdx
                        }
                    }
                    'PageUp' {
                        $resultOffset = Get-GridResultOffset -Current $resultOffset -PageSize $resultRowCount -LineCount $resultLinesArray.Count -Direction Previous
                    }
                    'PageDown' {
                        $resultOffset = Get-GridResultOffset -Current $resultOffset -PageSize $resultRowCount -LineCount $resultLinesArray.Count -Direction Next
                    }
                    'Home' {
                        $resultOffset = Get-GridResultOffset -Current $resultOffset -PageSize $resultRowCount -LineCount $resultLinesArray.Count -Direction First
                    }
                    'End' {
                        $resultOffset = Get-GridResultOffset -Current $resultOffset -PageSize $resultRowCount -LineCount $resultLinesArray.Count -Direction Last
                    }
                    'Enter' {
                        Set-GridSelectionState -State $SelectionState -Index $selIdx -PreferredColumn $preferredColIdx
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
                        Set-GridSelectionState -State $SelectionState -Index $selIdx -PreferredColumn $preferredColIdx
                        Set-MenuCursorPosition -Left 0 -Top $viewport.Bottom
                        return $null
                    }
                }
            }
            Set-GridSelectionState -State $SelectionState -Index $selIdx -PreferredColumn $preferredColIdx
            if ($geometryChanged -or $selIdx -ne $prevSelIdx -or $colIdx -ne $prevColIdx -or $resultOffset -ne $previousResultOffset) { & $render }
        }
    } finally {
        if ($usesPointerInput) { Stop-MenuPointerInput }
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
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact',
        [ValidateRange(0, 40)]
        [int]$CategoryWidth = $script:MenuCategoryWidth,
        [string]$ResultTitle,
        [object[]]$ResultLines = @(),
        [switch]$ResultFollowTail,
        [ValidateSet('Run', 'Select', 'Confirm')]
        [string]$Interaction = 'Run',
        [hashtable]$SelectionState
    )
    return Invoke-GridLoop -Header $Header -Rows $Rows -Footer $Footer -StartSel $StartSel -StartCol $StartCol -EscapeAction $EscapeAction -ClearScreen:$ClearScreen -BannerStyle $BannerStyle -CategoryWidth $CategoryWidth -ResultTitle $ResultTitle -ResultLines $ResultLines -ResultFollowTail:$ResultFollowTail -Interaction $Interaction -SelectionState $SelectionState
}
