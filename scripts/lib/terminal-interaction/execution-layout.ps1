# Execution Journal and Tracking Panel layout.

function Get-TerminalTrackingLines {
    param([Parameter(Mandatory)][object]$View)

    $lines = [System.Collections.Generic.List[object]]::new()
    foreach ($step in $View.TrackingSteps) {
        $mark = switch ($step.State) {
            'Completed' { '[ok]' }
            'Running' { '[..]' }
            'Failed' { '[x]' }
            default { '[ ]' }
        }
        $role = switch ($step.State) {
            'Running' { 'Warning' }
            'Failed' { 'Error' }
            default { 'Supporting' }
        }
        $lines.Add([pscustomobject]@{
            Text = "$mark $($step.Label)"
            PresentationRole = $role
        })
    }
    if ($View.Result) {
        $resultRole = if ($View.State -eq 'Failed') { 'Error' } else { 'Success' }
        $lines.Add([pscustomobject]@{
            Text = "Result: $($View.Result)"
            PresentationRole = $resultRole
        })
    }
    return @($lines)
}

function Get-TerminalTrackingVisualLines {
    param(
        [Parameter(Mandatory)][object]$View,
        [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Width
    )

    $visualLines = [System.Collections.Generic.List[object]]::new()
    foreach ($trackingLine in @(Get-TerminalTrackingLines -View $View)) {
        foreach ($text in @(Split-TerminalText -Text $trackingLine.Text -Width $Width)) {
            $visualLines.Add([pscustomobject]@{
                Text = $text
                PresentationRole = $trackingLine.PresentationRole
            })
        }
    }
    return @($visualLines)
}

function Add-TerminalExecutionBody {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$View,
        [Parameter(Mandatory)][object]$NavigationGrid,
        [string]$FocusedTargetId,
        [int]$JournalOffset
    )

    [void](Add-TerminalFrameLine -Frame $Frame)
    if ($null -ne $View.BackTarget) {
        $backLine = Add-TerminalFrameLine -Frame $Frame
        Add-TerminalNavigationTarget -Frame $Frame -Target $View.BackTarget -LineIndex $backLine -Grid $NavigationGrid -FocusedTargetId $FocusedTargetId
        [void](Add-TerminalFrameLine -Frame $Frame)
    }

    $remaining = $Frame.Height - $Frame.Lines.Count
    $wide = $Frame.Width -ge 96 -and $remaining -ge 7
    if ($wide) {
        $contentWidth = $Frame.Width - 4
        $gap = 3
        $trackingWidth = [Math]::Max('Execution Tracking'.Length, [Math]::Floor($contentWidth / 6))
        $journalWidth = $contentWidth - $trackingWidth - $gap
        if ($journalWidth -lt 40) { $wide = $false }
    }

    if ($wide) {
        $trackingLines = @(Get-TerminalTrackingVisualLines -View $View -Width $trackingWidth)
        $titleHeight = 1
        $pageSize = [Math]::Max(1, $remaining - $titleHeight)
        $hasPages = $View.JournalLines.Count -gt $pageSize
        if ($hasPages) {
            $pageSize = [Math]::Max(
                1,
                $remaining - $titleHeight - (Get-TerminalPagingFooterHeight)
            )
        }
        $maximumOffset = [Math]::Max(0, $View.JournalLines.Count - $pageSize)
        $offset = [Math]::Max(0, [Math]::Min($JournalOffset, $maximumOffset))
        $Frame.JournalPageSize = $pageSize
        $Frame.JournalLineCount = $View.JournalLines.Count

        $titleLine = Add-TerminalFrameLine -Frame $Frame
        Add-TerminalFrameSegment -Frame $Frame -LineIndex $titleLine -X 2 -Text 'Execution Journal' -PresentationRole PanelTitle
        $separatorX = 2 + $journalWidth + 1
        Add-TerminalFrameSegment `
            -Frame $Frame `
            -LineIndex $titleLine `
            -X $separatorX `
            -Text ([string][char]0x2502) `
            -PresentationRole PanelSeparator
        $trackingX = 2 + $journalWidth + $gap
        Add-TerminalFrameSegment -Frame $Frame -LineIndex $titleLine -X $trackingX -Text 'Execution Tracking' -PresentationRole PanelTitle
        for ($row = 0; $row -lt $pageSize; $row++) {
            $line = Add-TerminalFrameLine -Frame $Frame
            $journalIndex = $offset + $row
            if ($journalIndex -lt $View.JournalLines.Count) {
                Add-TerminalFrameSegment -Frame $Frame -LineIndex $line -X 2 -Text (Limit-TerminalText -Text ([string]$View.JournalLines[$journalIndex]) -Width $journalWidth) -PresentationRole Body
            }
            Add-TerminalFrameSegment `
                -Frame $Frame `
                -LineIndex $line `
                -X $separatorX `
                -Text ([string][char]0x2502) `
                -PresentationRole PanelSeparator
            if ($row -lt $trackingLines.Count) {
                Add-TerminalFrameSegment `
                    -Frame $Frame `
                    -LineIndex $line `
                    -X $trackingX `
                    -Text $trackingLines[$row].Text `
                    -PresentationRole $trackingLines[$row].PresentationRole
            }
        }
        if ($hasPages) {
            Add-TerminalPagingFooter `
                -Frame $Frame `
                -Offset $offset `
                -PageSize $pageSize `
                -LineCount $View.JournalLines.Count `
                -FocusedTargetId $FocusedTargetId `
                -X 2 `
                -Width $journalWidth `
                -BoundaryX $separatorX
        }
        return
    }

    $trackingWidth = [Math]::Max(1, $Frame.Width - 4)
    $trackingLines = @(Get-TerminalTrackingVisualLines -View $View -Width $trackingWidth)
    $trackingBudget = [Math]::Min([Math]::Max(4, $trackingLines.Count), [Math]::Max(4, [Math]::Floor($remaining / 3)))
    $journalBudget = [Math]::Max(1, $remaining - $trackingBudget - 3)
    $hasNarrowPages = $View.JournalLines.Count -gt $journalBudget
    if ($hasNarrowPages) {
        $journalBudget = [Math]::Max(
            1,
            $remaining - $trackingBudget - 3 - (Get-TerminalPagingFooterHeight)
        )
    }
    $maximumNarrowOffset = [Math]::Max(0, $View.JournalLines.Count - $journalBudget)
    $narrowOffset = [Math]::Max(0, [Math]::Min($JournalOffset, $maximumNarrowOffset))
    $Frame.JournalPageSize = $journalBudget
    $Frame.JournalLineCount = $View.JournalLines.Count

    $journalTitle = Add-TerminalFrameLine -Frame $Frame
    Add-TerminalFrameSegment -Frame $Frame -LineIndex $journalTitle -X 2 -Text 'Execution Journal' -PresentationRole PanelTitle
    for ($row = 0; $row -lt $journalBudget; $row++) {
        $line = Add-TerminalFrameLine -Frame $Frame
        $journalIndex = $narrowOffset + $row
        if ($journalIndex -lt $View.JournalLines.Count) {
            Add-TerminalFrameSegment `
                -Frame $Frame `
                -LineIndex $line `
                -X 2 `
                -Text (Limit-TerminalText -Text ([string]$View.JournalLines[$journalIndex]) -Width $trackingWidth) `
                -PresentationRole Body
        }
    }
    if ($hasNarrowPages) {
        Add-TerminalPagingFooter `
            -Frame $Frame `
            -Offset $narrowOffset `
            -PageSize $journalBudget `
            -LineCount $View.JournalLines.Count `
            -FocusedTargetId $FocusedTargetId
    }
    $separator = Add-TerminalFrameLine -Frame $Frame
    Add-TerminalFrameSegment `
        -Frame $Frame `
        -LineIndex $separator `
        -X 2 `
        -Text ([string]::new([char]0x2500, $trackingWidth)) `
        -PresentationRole PanelSeparator
    $trackingTitle = Add-TerminalFrameLine -Frame $Frame
    Add-TerminalFrameSegment -Frame $Frame -LineIndex $trackingTitle -X 2 -Text 'Execution Tracking' -PresentationRole PanelTitle
    for ($row = 0; $row -lt $trackingBudget; $row++) {
        $line = Add-TerminalFrameLine -Frame $Frame
        if ($row -lt $trackingLines.Count) {
            Add-TerminalFrameSegment `
                -Frame $Frame `
                -LineIndex $line `
                -X 2 `
                -Text $trackingLines[$row].Text `
                -PresentationRole $trackingLines[$row].PresentationRole
        }
    }
}
