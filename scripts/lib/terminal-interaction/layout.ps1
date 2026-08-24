# Pure projection from semantic descriptors and terminal metrics to a frame plan.

function New-TerminalFramePlan {
    param(
        [Parameter(Mandatory)][object]$View,
        [Parameter(Mandatory)][ValidateRange(20, 1000)][int]$Width,
        [Parameter(Mandatory)][ValidateRange(8, 1000)][int]$Height
    )

    return [pscustomobject][ordered]@{
        PSTypeName = 'Deckle.TerminalInteraction.Frame'
        ViewId = $View.ViewId
        Width = $Width
        Height = $Height
        Lines = [System.Collections.Generic.List[object]]::new()
        Targets = [System.Collections.Generic.List[object]]::new()
        BodyPageSize = 0
        BodyLineCount = 0
        JournalPageSize = 0
        JournalLineCount = 0
        DefaultTargetId = if ($View.PSObject.Properties['DefaultTargetId']) { $View.DefaultTargetId } else { $null }
    }
}

function New-TerminalFrameLine {
    param([Parameter(Mandatory)][int]$Width)

    return [pscustomobject][ordered]@{
        Width = $Width
        Segments = [System.Collections.Generic.List[object]]::new()
    }
}

function Add-TerminalFrameLine {
    param([Parameter(Mandatory)][object]$Frame)

    if ($Frame.Lines.Count -ge $Frame.Height) { return -1 }
    $line = New-TerminalFrameLine -Width $Frame.Width
    $Frame.Lines.Add($line)
    return $Frame.Lines.Count - 1
}

function Add-TerminalFrameSegment {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][int]$LineIndex,
        [Parameter(Mandatory)][int]$X,
        [AllowEmptyString()][string]$Text,
        [ValidateSet(
            'Banner', 'Context', 'Section', 'SectionSeparator',
            'Action', 'ActionVariant', 'Access', 'Adjust', 'Navigation', 'Exit', 'Danger',
            'PanelTitle', 'Body', 'Supporting', 'PrimarySeparator', 'PanelSeparator', 'PagingSeparator', 'Separator',
            'CommandKey', 'CommandLabel', 'Success', 'Warning', 'Error'
        )]
        [string]$PresentationRole = 'Body',
        [ValidateSet('Normal', 'Focused', 'Disabled')][string]$State = 'Normal'
    )

    if ($LineIndex -lt 0 -or $LineIndex -ge $Frame.Lines.Count) { return }
    if ($X -ge $Frame.Width -or [string]::IsNullOrEmpty($Text)) { return }
    if ($X -lt 0) {
        $skip = -$X
        if ($skip -ge $Text.Length) { return }
        $Text = $Text.Substring($skip)
        $X = 0
    }
    $available = $Frame.Width - $X
    if ($Text.Length -gt $available) { $Text = $Text.Substring(0, $available) }
    if ($Text.Length -eq 0) { return }
    $Frame.Lines[$LineIndex].Segments.Add([pscustomobject][ordered]@{
        X = $X
        Text = $Text
        PresentationRole = $PresentationRole
        State = $State
    })
}

function Add-TerminalTargetPlacement {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$Target,
        [Parameter(Mandatory)][int]$X,
        [Parameter(Mandatory)][int]$Y,
        [Parameter(Mandatory)][ValidateRange(1, 1000)][int]$Width
    )

    if ($Y -lt 0 -or $Y -ge $Frame.Height) { return }
    $Frame.Targets.Add([pscustomobject][ordered]@{
        TargetId = $Target.TargetId
        Target = $Target
        X = $X
        Y = $Y
        Width = $Width
        Height = 1
        CenterX = $X + (($Width - 1) / 2.0)
    })
}

function Limit-TerminalText {
    param(
        [AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][int]$Width
    )

    if ($Width -le 0) { return '' }
    if ($null -eq $Text) { return '' }
    if ($Text.Length -le $Width) { return $Text }
    if ($Width -eq 1) { return [string][char]0x2026 }
    return $Text.Substring(0, $Width - 1) + [char]0x2026
}

function Split-TerminalText {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Width
    )

    if ($Text.Length -le $Width) { return @($Text) }

    $lines = [System.Collections.Generic.List[string]]::new()
    $current = ''
    foreach ($wordValue in @($Text -split '\s+')) {
        $word = [string]$wordValue
        if ([string]::IsNullOrEmpty($word)) { continue }
        while ($word.Length -gt $Width) {
            if ($current.Length -gt 0) {
                $lines.Add($current)
                $current = ''
            }
            $lines.Add($word.Substring(0, $Width))
            $word = $word.Substring($Width)
        }
        if ($word.Length -eq 0) { continue }
        if ($current.Length -eq 0) {
            $current = $word
        } elseif (($current.Length + 1 + $word.Length) -le $Width) {
            $current += " $word"
        } else {
            $lines.Add($current)
            $current = $word
        }
    }
    if ($current.Length -gt 0) { $lines.Add($current) }
    if ($lines.Count -eq 0) { $lines.Add('') }
    return @($lines)
}

function Get-TerminalTargetPresentationRole {
    param(
        [Parameter(Mandatory)][object]$Target,
        [switch]$AsActionVariant
    )

    if ($Target.PresentationRole -eq 'Action' -and $AsActionVariant) { return 'ActionVariant' }
    return $Target.PresentationRole
}

function Add-TerminalTarget {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$Target,
        [Parameter(Mandatory)][int]$LineIndex,
        [Parameter(Mandatory)][int]$X,
        [Parameter(Mandatory)][int]$Width,
        [string]$FocusedTargetId,
        [switch]$AsActionVariant,
        [bool]$ShowDisabledReason = $true,
        [ValidateLength(1, 1)][string]$DisabledMarker = 'x'
    )

    if ($LineIndex -lt 0) { return }
    $focused = $Target.TargetId -eq $FocusedTargetId
    $focusMarker = if (-not $Target.Enabled) { $DisabledMarker } elseif ($focused) { '>' } else { ' ' }
    $selectionMarker = switch ($Target.SelectionMode) {
        'Single' { if ($Target.Selected) { '(*)' } else { '( )' } }
        'Multiple' { if ($Target.Selected) { '[x]' } else { '[ ]' } }
        default { '' }
    }
    $prefix = if ($selectionMarker) { "$focusMarker $selectionMarker " } else { "$focusMarker " }
    $labelWidth = [Math]::Max(0, $Width - $prefix.Length)
    $label = Limit-TerminalText -Text $Target.Label -Width $labelWidth
    $text = ("$prefix$label").PadRight($Width)
    $state = if (-not $Target.Enabled) { 'Disabled' } elseif ($focused) { 'Focused' } else { 'Normal' }
    $role = Get-TerminalTargetPresentationRole -Target $Target -AsActionVariant:$AsActionVariant
    Add-TerminalFrameSegment -Frame $Frame -LineIndex $LineIndex -X $X -Text $text -PresentationRole $role -State $state
    Add-TerminalTargetPlacement -Frame $Frame -Target $Target -X $X -Y $LineIndex -Width $Width

    if (-not $Target.Enabled -and $ShowDisabledReason -and $Width -ge 24) {
        $reasonStart = $X + 2 + $label.Length + 2
        $reasonWidth = $X + $Width - $reasonStart
        if ($reasonWidth -ge 8) {
            $reason = Limit-TerminalText -Text $Target.DisabledReason -Width $reasonWidth
            Add-TerminalFrameSegment -Frame $Frame -LineIndex $LineIndex -X $reasonStart -Text $reason -PresentationRole Supporting -State Disabled
        }
    }
}

function Add-TerminalSectionHeading {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Label
    )

    $line = Add-TerminalFrameLine -Frame $Frame
    if ($line -lt 0) { return }
    $title = $Label.ToUpperInvariant() + ' '
    Add-TerminalFrameSegment -Frame $Frame -LineIndex $line -X 2 -Text $title -PresentationRole Section
    $ruleWidth = [Math]::Max(0, $Frame.Width - 4 - $title.Length)
    if ($ruleWidth -le 0) { return }
    $rule = ('- ' * [Math]::Ceiling($ruleWidth / 2.0)).Substring(0, $ruleWidth)
    Add-TerminalFrameSegment `
        -Frame $Frame `
        -LineIndex $line `
        -X (2 + $title.Length) `
        -Text $rule `
        -PresentationRole SectionSeparator
}

function ConvertTo-TerminalMenuLayoutSections {
    param([Parameter(Mandatory)][object[]]$Sections)

    $layoutSections = [System.Collections.Generic.List[object]]::new()
    foreach ($section in $Sections) {
        $rows = [System.Collections.Generic.List[object]]::new()
        $pendingTargets = [System.Collections.Generic.List[object]]::new()
        $trailingTarget = $null

        for ($itemIndex = 0; $itemIndex -lt $section.Items.Count; $itemIndex++) {
            $item = $section.Items[$itemIndex]
            $isActionRow = $item.PSObject.Properties['Kind'] -and $item.Kind -eq 'ActionRow'
            if ($isActionRow) {
                if ($pendingTargets.Count -gt 0) {
                    $rows.Add([pscustomobject]@{ Kind = 'TargetRow'; Label = ''; Targets = @($pendingTargets); TrailingTarget = $null })
                    $pendingTargets.Clear()
                }
                $rows.Add([pscustomobject]@{
                    Kind = 'ActionRow'
                    Label = $item.Label
                    Variants = @($item.Variants)
                    TrailingTarget = $null
                })
                continue
            }

            if ($item.PresentationRole -eq 'Exit' -and $itemIndex -eq ($section.Items.Count - 1)) {
                $trailingTarget = $item
                continue
            }
            $pendingTargets.Add($item)
            if ($pendingTargets.Count -eq 2) {
                $rows.Add([pscustomobject]@{ Kind = 'TargetRow'; Label = ''; Targets = @($pendingTargets); TrailingTarget = $null })
                $pendingTargets.Clear()
            }
        }
        if ($pendingTargets.Count -gt 0) {
            $rows.Add([pscustomobject]@{ Kind = 'TargetRow'; Label = ''; Targets = @($pendingTargets); TrailingTarget = $null })
        }
        if ($null -ne $trailingTarget) {
            if ($rows.Count -eq 0 -or $null -ne $rows[$rows.Count - 1].TrailingTarget) {
                $rows.Add([pscustomobject]@{ Kind = 'TargetRow'; Label = ''; Targets = @(); TrailingTarget = $trailingTarget })
            } else {
                $rows[$rows.Count - 1].TrailingTarget = $trailingTarget
            }
        }
        $layoutSections.Add([pscustomobject]@{
            Label = $section.Label
            Rows = @($rows)
        })
    }
    return @($layoutSections)
}

function Get-TerminalActionLabelWidth {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$LayoutSections)

    $maximum = 0
    foreach ($section in $LayoutSections) {
        foreach ($row in $section.Rows) {
            if ($row.Label -and $row.Label.Length -gt $maximum) { $maximum = $row.Label.Length }
        }
    }
    return [Math]::Min(20, [Math]::Max(12, $maximum))
}

function Get-TerminalMenuGrid {
    param(
        [AllowEmptyCollection()][object[]]$LayoutSections = @(),
        [Parameter(Mandatory)][int]$Width
    )

    $columnCount = 2
    $trailingWidth = 0
    foreach ($section in $LayoutSections) {
        foreach ($row in $section.Rows) {
            $targets = @(if ($row.Kind -eq 'ActionRow') { $row.Variants } else { $row.Targets })
            $columnCount = [Math]::Max($columnCount, $targets.Count)
            if ($null -ne $row.TrailingTarget) {
                $trailingWidth = [Math]::Max($trailingWidth, [Math]::Max(8, $row.TrailingTarget.Label.Length + 2))
            }
        }
    }

    $left = 2
    $right = $Width - 2
    $labelWidth = Get-TerminalActionLabelWidth -LayoutSections $LayoutSections
    $targetX = $left + $labelWidth + 3
    $gap = 2
    $targetAreaWidth = [Math]::Max(1, $right - $targetX)
    if ($trailingWidth -gt 0) { $targetAreaWidth -= $trailingWidth + $gap }
    $columnWidth = [Math]::Max(1, [Math]::Floor(($targetAreaWidth - (($columnCount - 1) * $gap)) / $columnCount))

    return [pscustomobject][ordered]@{
        Left = $left
        Right = $right
        LabelWidth = $labelWidth
        TargetX = $targetX
        Gap = $gap
        ColumnCount = $columnCount
        ColumnWidth = $columnWidth
        TrailingWidth = $trailingWidth
    }
}

function Add-TerminalMenuRow {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$Row,
        [Parameter(Mandatory)][object]$Grid,
        [Parameter(Mandatory)][bool]$Wide,
        [string]$FocusedTargetId
    )

    $left = 2
    if ($Wide) {
        $line = Add-TerminalFrameLine -Frame $Frame
        if ($line -lt 0) { return }
        if ($Row.Label) {
            Add-TerminalFrameSegment -Frame $Frame -LineIndex $line -X $left -Text (Limit-TerminalText -Text $Row.Label -Width $Grid.LabelWidth) -PresentationRole Action
        }
        $targets = @(if ($Row.Kind -eq 'ActionRow') { $Row.Variants } else { $Row.Targets })
        $trailing = $Row.TrailingTarget
        for ($i = 0; $i -lt $targets.Count; $i++) {
            Add-TerminalTarget `
                -Frame $Frame `
                -Target $targets[$i] `
                -LineIndex $line `
                -X ($Grid.TargetX + ($i * ($Grid.ColumnWidth + $Grid.Gap))) `
                -Width $Grid.ColumnWidth `
                -FocusedTargetId $FocusedTargetId `
                -AsActionVariant:($Row.Kind -eq 'ActionRow')
        }
        if ($null -ne $trailing) {
            Add-TerminalTarget -Frame $Frame -Target $trailing -LineIndex $line -X ($Grid.Right - $Grid.TrailingWidth) -Width $Grid.TrailingWidth -FocusedTargetId $FocusedTargetId
        }
        return
    }

    if ($Row.Label) {
        $labelLine = Add-TerminalFrameLine -Frame $Frame
        Add-TerminalFrameSegment -Frame $Frame -LineIndex $labelLine -X $left -Text $Row.Label -PresentationRole Action
    }
    $targets = @(if ($Row.Kind -eq 'ActionRow') { $Row.Variants } else { $Row.Targets })
    if ($null -ne $Row.TrailingTarget) { $targets += $Row.TrailingTarget }
    foreach ($target in $targets) {
        $line = Add-TerminalFrameLine -Frame $Frame
        Add-TerminalTarget `
            -Frame $Frame `
            -Target $target `
            -LineIndex $line `
            -X 4 `
            -Width ([Math]::Max(1, $Frame.Width - 6)) `
            -FocusedTargetId $FocusedTargetId `
            -AsActionVariant:($Row.Kind -eq 'ActionRow')
    }
}

function Add-TerminalActionMenuBody {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$View,
        [Parameter(Mandatory)][object]$NavigationGrid,
        [string]$FocusedTargetId
    )

    $wide = $Frame.Width -ge 72
    $layoutSections = @(ConvertTo-TerminalMenuLayoutSections -Sections $View.Sections)
    $grid = Get-TerminalMenuGrid -LayoutSections $layoutSections -Width $Frame.Width

    [void](Add-TerminalFrameLine -Frame $Frame)
    if ($null -ne $View.BackTarget) {
        $backLine = Add-TerminalFrameLine -Frame $Frame
        Add-TerminalNavigationTarget -Frame $Frame -Target $View.BackTarget -LineIndex $backLine -Grid $NavigationGrid -FocusedTargetId $FocusedTargetId
        [void](Add-TerminalFrameLine -Frame $Frame)
    }

    for ($sectionIndex = 0; $sectionIndex -lt $layoutSections.Count; $sectionIndex++) {
        $section = $layoutSections[$sectionIndex]
        if ($sectionIndex -gt 0) { [void](Add-TerminalFrameLine -Frame $Frame) }
        Add-TerminalSectionHeading -Frame $Frame -Label $section.Label
        foreach ($row in $section.Rows) {
            Add-TerminalMenuRow -Frame $Frame -Row $row -Grid $grid -Wide $wide -FocusedTargetId $FocusedTargetId
        }
    }
}

function Add-TerminalContentBody {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$View,
        [Parameter(Mandatory)][object]$NavigationGrid,
        [string]$FocusedTargetId
    )

    [void](Add-TerminalFrameLine -Frame $Frame)
    if ($null -ne $View.BackTarget) {
        $backLine = Add-TerminalFrameLine -Frame $Frame
        Add-TerminalNavigationTarget -Frame $Frame -Target $View.BackTarget -LineIndex $backLine -Grid $NavigationGrid -FocusedTargetId $FocusedTargetId
        [void](Add-TerminalFrameLine -Frame $Frame)
    }
    foreach ($contentLine in $View.Lines) {
        $line = Add-TerminalFrameLine -Frame $Frame
        Add-TerminalFrameSegment -Frame $Frame -LineIndex $line -X 2 -Text (Limit-TerminalText -Text ([string]$contentLine) -Width ($Frame.Width - 4)) -PresentationRole Body
    }
    if ($View.Targets.Count -gt 0) { [void](Add-TerminalFrameLine -Frame $Frame) }
    foreach ($target in $View.Targets) {
        $line = Add-TerminalFrameLine -Frame $Frame
        Add-TerminalTarget -Frame $Frame -Target $target -LineIndex $line -X 2 -Width ([Math]::Max(1, $Frame.Width - 4)) -FocusedTargetId $FocusedTargetId
    }
}

function Add-TerminalPagedBody {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$BodyFrame,
        [Parameter(Mandatory)][int]$BodyOffset,
        [string]$FocusedTargetId
    )

    $available = [Math]::Max(0, $Frame.Height - $Frame.Lines.Count)
    $Frame.BodyLineCount = $BodyFrame.Lines.Count
    if ($available -eq 0) { return }

    $hasPages = $BodyFrame.Lines.Count -gt $available
    $pageSize = if ($hasPages) {
        [Math]::Max(1, $available - (Get-TerminalPagingFooterHeight))
    } else {
        $available
    }
    $lastOffset = [Math]::Max(0, $BodyFrame.Lines.Count - $pageSize)
    $offset = if ($hasPages) { [Math]::Max(0, [Math]::Min($BodyOffset, $lastOffset)) } else { 0 }
    $Frame.BodyPageSize = $pageSize

    $copyCount = [Math]::Min($pageSize, $BodyFrame.Lines.Count - $offset)
    $destinationTop = $Frame.Lines.Count
    for ($index = 0; $index -lt $copyCount; $index++) {
        $Frame.Lines.Add($BodyFrame.Lines[$offset + $index])
    }
    foreach ($placement in $BodyFrame.Targets) {
        if ($placement.Y -lt $offset -or $placement.Y -ge $offset + $copyCount) { continue }
        $Frame.Targets.Add([pscustomobject][ordered]@{
            TargetId = $placement.TargetId
            Target = $placement.Target
            X = $placement.X
            Y = $destinationTop + $placement.Y - $offset
            Width = $placement.Width
            Height = $placement.Height
            CenterX = $placement.CenterX
        })
    }
    if ($hasPages) {
        Add-TerminalPagingFooter `
            -Frame $Frame `
            -Offset $offset `
            -PageSize $pageSize `
            -LineCount $BodyFrame.Lines.Count `
            -FocusedTargetId $FocusedTargetId
    }
}

function Get-TerminalInteractionFrame {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$View,
        [Parameter(Mandatory)][ValidateRange(20, 1000)][int]$Width,
        [Parameter(Mandatory)][ValidateRange(8, 1000)][int]$Height,
        [object]$OwnerActionMenu,
        [string]$FocusedTargetId,
        [bool]$SupportsUnicode = $true,
        [ValidateRange(0, 2147483647)][int]$BodyOffset = 0,
        [ValidateRange(0, 2147483647)][int]$JournalOffset = 0
    )

    if ($View.Kind -eq 'ActionMenu') {
        $OwnerActionMenu = $View
    } elseif ($null -eq $OwnerActionMenu) {
        throw "View '$($View.ViewId)' requires its owning Action Menu for navigation layout."
    } elseif ($OwnerActionMenu.Kind -ne 'ActionMenu' -or $OwnerActionMenu.ViewId -ne $View.OwnerActionMenuId) {
        throw "View '$($View.ViewId)' does not belong to Action Menu '$($OwnerActionMenu.ViewId)'."
    }

    $navigationGrid = Get-TerminalNavigationGrid -OwnerActionMenu $OwnerActionMenu -Width $Width
    $frame = New-TerminalFramePlan -View $View -Width $Width -Height $Height
    Add-TerminalHeader -Frame $frame -View $View -SupportsUnicode $SupportsUnicode
    switch ($View.Kind) {
        'ActionMenu' {
            $bodyFrame = New-TerminalFramePlan -View $View -Width $Width -Height 1000
            Add-TerminalActionMenuBody -Frame $bodyFrame -View $View -NavigationGrid $navigationGrid -FocusedTargetId $FocusedTargetId
            Add-TerminalPagedBody -Frame $frame -BodyFrame $bodyFrame -BodyOffset $BodyOffset -FocusedTargetId $FocusedTargetId
        }
        'Content' {
            $bodyFrame = New-TerminalFramePlan -View $View -Width $Width -Height 1000
            Add-TerminalContentBody -Frame $bodyFrame -View $View -NavigationGrid $navigationGrid -FocusedTargetId $FocusedTargetId
            Add-TerminalPagedBody -Frame $frame -BodyFrame $bodyFrame -BodyOffset $BodyOffset -FocusedTargetId $FocusedTargetId
        }
        'Preparation' {
            $bodyFrame = New-TerminalFramePlan -View $View -Width $Width -Height 1000
            Add-TerminalPreparationBody -Frame $bodyFrame -View $View -NavigationGrid $navigationGrid -FocusedTargetId $FocusedTargetId
            Add-TerminalPagedBody -Frame $frame -BodyFrame $bodyFrame -BodyOffset $BodyOffset -FocusedTargetId $FocusedTargetId
        }
        'Execution' { Add-TerminalExecutionBody -Frame $frame -View $View -NavigationGrid $navigationGrid -FocusedTargetId $FocusedTargetId -JournalOffset $JournalOffset }
        default { throw "Unknown View kind '$($View.Kind)'." }
    }
    while ($frame.Lines.Count -lt $frame.Height) { [void](Add-TerminalFrameLine -Frame $frame) }
    return $frame
}
