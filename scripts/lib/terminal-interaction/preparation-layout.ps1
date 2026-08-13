# Pure layout of Preparation Selectors, scope, review, and confirmation.

function Get-TerminalPreparationGrid {
    param(
        [Parameter(Mandatory)][object[]]$Selectors,
        [Parameter(Mandatory)][int]$Width
    )

    $labelWidth = 12
    $maximumOptionCount = 1
    $preferredOptionWidth = 14
    foreach ($selector in $Selectors) {
        $labelWidth = [Math]::Max($labelWidth, $selector.FilterLabel.Length)
        $maximumOptionCount = [Math]::Max($maximumOptionCount, $selector.Targets.Count)
        foreach ($target in $selector.Targets) {
            $preferredOptionWidth = [Math]::Max($preferredOptionWidth, $target.Label.Length + 6)
        }
    }
    $labelWidth = [Math]::Min(20, $labelWidth)
    $preferredOptionWidth = [Math]::Min(30, $preferredOptionWidth)

    $left = 2
    $right = $Width - 2
    $targetX = $left + $labelWidth + 3
    $gap = 2
    $targetAreaWidth = [Math]::Max(1, $right - $targetX)
    $columnCount = [Math]::Max(1, [Math]::Floor(($targetAreaWidth + $gap) / ($preferredOptionWidth + $gap)))
    $columnCount = [Math]::Min($maximumOptionCount, $columnCount)
    $columnWidth = [Math]::Max(1, [Math]::Floor(($targetAreaWidth - (($columnCount - 1) * $gap)) / $columnCount))

    return [pscustomobject][ordered]@{
        Left = $left
        Right = $right
        LabelWidth = $labelWidth
        TargetX = $targetX
        Gap = $gap
        ColumnCount = $columnCount
        ColumnWidth = $columnWidth
    }
}

function Add-TerminalSelector {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$Selector,
        [Parameter(Mandatory)][object]$Grid,
        [string]$FocusedTargetId
    )

    if ($Grid.ColumnCount -eq 1) {
        $labelLine = Add-TerminalFrameLine -Frame $Frame
        Add-TerminalFrameSegment `
            -Frame $Frame `
            -LineIndex $labelLine `
            -X $Grid.Left `
            -Text (Limit-TerminalText -Text $Selector.FilterLabel -Width $Grid.LabelWidth) `
            -PresentationRole Action
        foreach ($target in $Selector.Targets) {
            $line = Add-TerminalFrameLine -Frame $Frame
            Add-TerminalTarget `
                -Frame $Frame `
                -Target $target `
                -LineIndex $line `
                -X 4 `
                -Width ([Math]::Max(1, $Frame.Width - 6)) `
                -FocusedTargetId $FocusedTargetId
        }
        return
    }

    $rowCount = [Math]::Ceiling($Selector.Targets.Count / [double]$Grid.ColumnCount)
    for ($rowIndex = 0; $rowIndex -lt $rowCount; $rowIndex++) {
        $line = Add-TerminalFrameLine -Frame $Frame
        if ($rowIndex -eq 0) {
            Add-TerminalFrameSegment `
                -Frame $Frame `
                -LineIndex $line `
                -X $Grid.Left `
                -Text (Limit-TerminalText -Text $Selector.FilterLabel -Width $Grid.LabelWidth) `
                -PresentationRole Action
        }
        for ($columnIndex = 0; $columnIndex -lt $Grid.ColumnCount; $columnIndex++) {
            $targetIndex = ($rowIndex * $Grid.ColumnCount) + $columnIndex
            if ($targetIndex -ge $Selector.Targets.Count) { break }
            Add-TerminalTarget `
                -Frame $Frame `
                -Target $Selector.Targets[$targetIndex] `
                -LineIndex $line `
                -X ($Grid.TargetX + ($columnIndex * ($Grid.ColumnWidth + $Grid.Gap))) `
                -Width $Grid.ColumnWidth `
                -FocusedTargetId $FocusedTargetId
        }
    }
}

function Add-TerminalPreparationLines {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Lines,
        [ValidateSet('Body', 'Supporting', 'Warning', 'Error')][string]$PresentationRole = 'Body'
    )

    foreach ($text in $Lines) {
        $line = Add-TerminalFrameLine -Frame $Frame
        Add-TerminalFrameSegment `
            -Frame $Frame `
            -LineIndex $line `
            -X 4 `
            -Text (Limit-TerminalText -Text $text -Width ([Math]::Max(1, $Frame.Width - 6))) `
            -PresentationRole $PresentationRole
    }
}

function Add-TerminalPreparationBody {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$View,
        [string]$FocusedTargetId
    )

    $grid = Get-TerminalPreparationGrid -Selectors $View.Selectors -Width $Frame.Width
    [void](Add-TerminalFrameLine -Frame $Frame)
    if ($null -ne $View.BackTarget) {
        $backLine = Add-TerminalFrameLine -Frame $Frame
        Add-TerminalNavigationTarget `
            -Frame $Frame `
            -Target $View.BackTarget `
            -LineIndex $backLine `
            -Grid $grid `
            -FocusedTargetId $FocusedTargetId
        [void](Add-TerminalFrameLine -Frame $Frame)
    }

    Add-TerminalSectionHeading -Frame $Frame -Label Filters
    foreach ($selector in $View.Selectors) {
        Add-TerminalSelector -Frame $Frame -Selector $selector -Grid $grid -FocusedTargetId $FocusedTargetId
    }

    [void](Add-TerminalFrameLine -Frame $Frame)
    Add-TerminalSectionHeading -Frame $Frame -Label 'Effective Scope'
    $scopeRole = switch ($View.EffectiveScope.State) {
        'Resolving' { 'Warning' }
        'Failed' { 'Error' }
        default { 'Body' }
    }
    $scopeLines = @($View.EffectiveScope.Lines)
    if ($View.EffectiveScope.State -eq 'Failed') { $scopeLines += $View.EffectiveScope.FailureReason }
    Add-TerminalPreparationLines -Frame $Frame -Lines $scopeLines -PresentationRole $scopeRole

    [void](Add-TerminalFrameLine -Frame $Frame)
    Add-TerminalSectionHeading -Frame $Frame -Label Review
    Add-TerminalPreparationLines -Frame $Frame -Lines $View.Review.Lines

    [void](Add-TerminalFrameLine -Frame $Frame)
    Add-TerminalSectionHeading -Frame $Frame -Label Confirmation
    $confirmationLine = Add-TerminalFrameLine -Frame $Frame
    $confirmationWidth = [Math]::Max($grid.ColumnWidth, [Math]::Min(30, $grid.Right - $grid.TargetX))
    Add-TerminalTarget `
        -Frame $Frame `
        -Target $View.ConfirmationTarget `
        -LineIndex $confirmationLine `
        -X $grid.TargetX `
        -Width $confirmationWidth `
        -FocusedTargetId $FocusedTargetId `
        -ShowDisabledReason $false
    if (-not $View.ConfirmationTarget.Enabled) {
        $reasonLine = Add-TerminalFrameLine -Frame $Frame
        Add-TerminalFrameSegment `
            -Frame $Frame `
            -LineIndex $reasonLine `
            -X $grid.TargetX `
            -Text (Limit-TerminalText -Text $View.ConfirmationTarget.DisabledReason -Width ([Math]::Max(1, $grid.Right - $grid.TargetX))) `
            -PresentationRole Supporting
    }
}
