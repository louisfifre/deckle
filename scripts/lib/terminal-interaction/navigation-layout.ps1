# Shared layout for visible navigation controls and paging chrome.

function Get-TerminalNavigationGrid {
    param(
        [Parameter(Mandatory)][object]$OwnerActionMenu,
        [Parameter(Mandatory)][int]$Width
    )

    if ($OwnerActionMenu.Kind -ne 'ActionMenu') {
        throw "Navigation layout owner '$($OwnerActionMenu.ViewId)' is not an Action Menu."
    }
    $layoutSections = @(ConvertTo-TerminalMenuLayoutSections -Sections $OwnerActionMenu.Sections)
    return Get-TerminalMenuGrid -LayoutSections $layoutSections -Width $Width
}

function Add-TerminalNavigationTarget {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$Target,
        [Parameter(Mandatory)][int]$LineIndex,
        [Parameter(Mandatory)][object]$Grid,
        [string]$FocusedTargetId
    )

    if ($Frame.Width -ge 72) {
        Add-TerminalTarget `
            -Frame $Frame `
            -Target $Target `
            -LineIndex $LineIndex `
            -X $grid.TargetX `
            -Width $grid.ColumnWidth `
            -FocusedTargetId $FocusedTargetId
    } else {
        Add-TerminalTarget `
            -Frame $Frame `
            -Target $Target `
            -LineIndex $LineIndex `
            -X 4 `
            -Width ([Math]::Max(1, $Frame.Width - 6)) `
            -FocusedTargetId $FocusedTargetId
    }
}

function Get-TerminalPagingFooterHeight {
    return 2
}

function Add-TerminalPagingFooter {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][int]$Offset,
        [Parameter(Mandatory)][int]$PageSize,
        [Parameter(Mandatory)][int]$LineCount,
        [string]$FocusedTargetId,
        [int]$X = 2,
        [int]$Width = -1,
        [int]$BoundaryX = -1
    )

    if ($Width -lt 0) { $Width = [Math]::Max(1, $Frame.Width - ($X * 2)) }

    $separatorLine = Add-TerminalFrameLine -Frame $Frame
    Add-TerminalFrameSegment `
        -Frame $Frame `
        -LineIndex $separatorLine `
        -X $X `
        -Text ([string]::new([char]0x2500, $Width)) `
        -PresentationRole PagingSeparator
    if ($BoundaryX -ge 0) {
        Add-TerminalFrameSegment `
            -Frame $Frame `
            -LineIndex $separatorLine `
            -X $BoundaryX `
            -Text ([string][char]0x2502) `
            -PresentationRole PanelSeparator
    }

    $line = Add-TerminalFrameLine -Frame $Frame
    $lastOffset = [Math]::Max(0, $LineCount - $PageSize)
    $previous = New-TerminalTarget `
        -TargetId navigation.page.previous `
        -Label Previous `
        -IntentKind Navigation `
        -Payload ([pscustomobject]@{ Command = 'Page'; PageDirection = 'Previous' }) `
        -PresentationRole Navigation `
        -Enabled ($Offset -gt 0) `
        -DisabledReason $(if ($Offset -gt 0) { $null } else { 'First page.' })
    $next = New-TerminalTarget `
        -TargetId navigation.page.next `
        -Label Next `
        -IntentKind Navigation `
        -Payload ([pscustomobject]@{ Command = 'Page'; PageDirection = 'Next' }) `
        -PresentationRole Navigation `
        -Enabled ($Offset -lt $lastOffset) `
        -DisabledReason $(if ($Offset -lt $lastOffset) { $null } else { 'Latest page.' })
    $targetWidth = if ($Width -ge 50) { 14 } else { 11 }
    Add-TerminalTarget `
        -Frame $Frame `
        -Target $previous `
        -LineIndex $line `
        -X $X `
        -Width $targetWidth `
        -FocusedTargetId $FocusedTargetId `
        -DisabledMarker '-'
    Add-TerminalTarget `
        -Frame $Frame `
        -Target $next `
        -LineIndex $line `
        -X ($X + 2 + $targetWidth) `
        -Width $targetWidth `
        -FocusedTargetId $FocusedTargetId `
        -DisabledMarker '-'

    $wheel = 'Wheel'
    $label = 'Scroll'
    $textLength = $wheel.Length + 1 + $label.Length
    $right = $X + $Width
    $wheelX = $right - $textLength
    if ($wheelX -gt $X + 4 + ($targetWidth * 2)) {
        Add-TerminalFrameSegment -Frame $Frame -LineIndex $line -X $wheelX -Text $wheel -PresentationRole CommandKey
        Add-TerminalFrameSegment -Frame $Frame -LineIndex $line -X ($wheelX + $wheel.Length + 1) -Text $label -PresentationRole CommandLabel
    }
    if ($BoundaryX -ge 0) {
        Add-TerminalFrameSegment `
            -Frame $Frame `
            -LineIndex $line `
            -X $BoundaryX `
            -Text ([string][char]0x2502) `
            -PresentationRole PanelSeparator
    }
}
