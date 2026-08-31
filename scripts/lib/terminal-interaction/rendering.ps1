# Frame-plan serialization and terminal drawing operations.

function ConvertTo-TerminalFrameCharacters {
    param([Parameter(Mandatory)][object]$Line)

    $characters = New-Object char[] $Line.Width
    for ($index = 0; $index -lt $characters.Length; $index++) { $characters[$index] = ' ' }
    foreach ($segment in $Line.Segments) {
        for ($index = 0; $index -lt $segment.Text.Length; $index++) {
            $position = $segment.X + $index
            if ($position -ge 0 -and $position -lt $characters.Length) {
                $characters[$position] = $segment.Text[$index]
            }
        }
    }
    return ,$characters
}

function ConvertTo-TerminalFrameText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Frame,
        [switch]$PreserveWidth
    )

    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $Frame.Lines) {
        $text = -join (ConvertTo-TerminalFrameCharacters -Line $line)
        if (-not $PreserveWidth) { $text = $text.TrimEnd() }
        $result.Add($text)
    }
    return @($result)
}

function Get-TerminalChangedLineIndexes {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [AllowNull()][object]$PreviousFrame
    )

    $changed = [System.Collections.Generic.List[int]]::new()
    $requiresCompleteRedraw = $null -eq $PreviousFrame `
        -or $Frame.Width -ne $PreviousFrame.Width `
        -or $Frame.Height -ne $PreviousFrame.Height `
        -or $Frame.ColorCapability -ne $PreviousFrame.ColorCapability
    if ($requiresCompleteRedraw) {
        for ($lineIndex = 0; $lineIndex -lt $Frame.Lines.Count; $lineIndex++) {
            $changed.Add($lineIndex)
        }
        return @($changed)
    }

    $lineCount = [Math]::Max($Frame.Lines.Count, $PreviousFrame.Lines.Count)
    for ($lineIndex = 0; $lineIndex -lt $lineCount; $lineIndex++) {
        if ($lineIndex -ge $Frame.Lines.Count -or $lineIndex -ge $PreviousFrame.Lines.Count) {
            $changed.Add($lineIndex)
            continue
        }

        $line = $Frame.Lines[$lineIndex]
        $previousLine = $PreviousFrame.Lines[$lineIndex]
        $lineChanged = $line.Width -ne $previousLine.Width -or $line.Segments.Count -ne $previousLine.Segments.Count
        if (-not $lineChanged) {
            for ($segmentIndex = 0; $segmentIndex -lt $line.Segments.Count; $segmentIndex++) {
                $segment = $line.Segments[$segmentIndex]
                $previousSegment = $previousLine.Segments[$segmentIndex]
                if ($segment.X -ne $previousSegment.X `
                    -or $segment.Text -ne $previousSegment.Text `
                    -or $segment.PresentationRole -ne $previousSegment.PresentationRole `
                    -or $segment.State -ne $previousSegment.State) {
                    $lineChanged = $true
                    break
                }
            }
        }
        if ($lineChanged) { $changed.Add($lineIndex) }
    }
    return @($changed)
}

function Get-TerminalSegmentColors {
    param(
        [Parameter(Mandatory)][object]$Segment,
        [Parameter(Mandatory)][object]$HostState
    )

    $colorCapability = 'Supported'
    $colorProperty = $HostState.PSObject.Properties['Color']
    if ($null -ne $colorProperty) { $colorCapability = [string]$colorProperty.Value }
    $themeVariantProperty = $HostState.PSObject.Properties['ThemeVariant']
    $themeVariant = if ($null -ne $themeVariantProperty -and $themeVariantProperty.Value -in @('Dark', 'Light')) {
        [string]$themeVariantProperty.Value
    } else {
        Resolve-TerminalThemeVariant -BackgroundColor $HostState.OriginalBackground
    }
    $style = Get-TerminalPresentationStyle `
        -Role $Segment.PresentationRole `
        -State $Segment.State `
        -ColorCapability $colorCapability `
        -ThemeVariant $themeVariant
    $foreground = if ($null -eq $style.Foreground) { $HostState.OriginalForeground } else { $style.Foreground }
    $background = if ($null -eq $style.Background) { $HostState.OriginalBackground } else { $style.Background }
    return [pscustomobject]@{
        Foreground = $foreground
        Background = $background
        ForegroundRgb = $style.ForegroundRgb
        BackgroundRgb = $style.BackgroundRgb
    }
}

function Set-TerminalFrameCursorPosition {
    param(
        [Parameter(Mandatory)][int]$X,
        [Parameter(Mandatory)][int]$Y,
        [Parameter(Mandatory)][object]$HostState
    )

    if ($HostState.VirtualTerminal -eq 'Supported') {
        [Console]::Write((Get-TerminalCursorPositionSequence -X $X -Y $Y))
    } else {
        [Console]::SetCursorPosition($X, $Y)
    }
}

function Get-TerminalCursorPositionSequence {
    param(
        [Parameter(Mandatory)][ValidateRange(0, 1000000)][int]$X,
        [Parameter(Mandatory)][ValidateRange(0, 1000000)][int]$Y
    )

    $escape = [char]27
    return "$escape[$($Y + 1);$($X + 1)H"
}

function Get-TerminalColorSequenceCode {
    param(
        [Parameter(Mandatory)][ConsoleColor]$Color,
        [switch]$Background
    )

    if ($Background) {
        $code = switch ($Color) {
            Black { 40 }
            DarkBlue { 44 }
            DarkGreen { 42 }
            DarkCyan { 46 }
            DarkRed { 41 }
            DarkMagenta { 45 }
            DarkYellow { 43 }
            Gray { 47 }
            DarkGray { 100 }
            Blue { 104 }
            Green { 102 }
            Cyan { 106 }
            Red { 101 }
            Magenta { 105 }
            Yellow { 103 }
            White { 107 }
        }
        return $code
    }

    $code = switch ($Color) {
        Black { 30 }
        DarkBlue { 34 }
        DarkGreen { 32 }
        DarkCyan { 36 }
        DarkRed { 31 }
        DarkMagenta { 35 }
        DarkYellow { 33 }
        Gray { 37 }
        DarkGray { 90 }
        Blue { 94 }
        Green { 92 }
        Cyan { 96 }
        Red { 91 }
        Magenta { 95 }
        Yellow { 93 }
        White { 97 }
    }
    return $code
}

function Get-TerminalTextAttributeSequence {
    param(
        [Parameter(Mandatory)][ConsoleColor]$Foreground,
        [Parameter(Mandatory)][ConsoleColor]$Background,
        [AllowNull()][int[]]$ForegroundRgb,
        [AllowNull()][int[]]$BackgroundRgb
    )

    $escape = [char]27
    $foregroundCode = if ($null -eq $ForegroundRgb) {
        Get-TerminalColorSequenceCode -Color $Foreground
    } else {
        Get-TerminalRgbSequenceCode -Color $ForegroundRgb
    }
    $backgroundCode = if ($null -eq $BackgroundRgb) {
        Get-TerminalColorSequenceCode -Color $Background -Background
    } else {
        Get-TerminalRgbSequenceCode -Color $BackgroundRgb -Background
    }
    return "$escape[$foregroundCode;${backgroundCode}m"
}

function Get-TerminalRgbSequenceCode {
    param(
        [Parameter(Mandatory)][ValidateCount(3, 3)][int[]]$Color,
        [switch]$Background
    )

    foreach ($channel in $Color) {
        if ($channel -lt 0 -or $channel -gt 255) { throw "RGB channel '$channel' is outside 0..255." }
    }
    $prefix = if ($Background) { 48 } else { 38 }
    return "$prefix;2;$($Color[0]);$($Color[1]);$($Color[2])"
}

function Get-TerminalSegmentSequence {
    param(
        [Parameter(Mandatory)][ValidateRange(0, 1000000)][int]$X,
        [Parameter(Mandatory)][ValidateRange(0, 1000000)][int]$Y,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][ConsoleColor]$Foreground,
        [Parameter(Mandatory)][ConsoleColor]$Background,
        [AllowNull()][int[]]$ForegroundRgb,
        [AllowNull()][int[]]$BackgroundRgb
    )

    $position = Get-TerminalCursorPositionSequence -X $X -Y $Y
    $attributes = Get-TerminalTextAttributeSequence `
        -Foreground $Foreground `
        -Background $Background `
        -ForegroundRgb $ForegroundRgb `
        -BackgroundRgb $BackgroundRgb
    return "$position$attributes$Text"
}

function ConvertTo-TerminalFrameRuns {
    param(
        [Parameter(Mandatory)][object]$Line,
        [Parameter(Mandatory)][ValidateRange(1, 1000000)][int]$PaintWidth
    )

    # One run per stretch of cells owned by the same segment, blanks included,
    # so a line paints in one left-to-right pass instead of a blank line
    # followed by its segments. A later segment wins a cell, as in text output.
    $owners = New-Object int[] $PaintWidth
    $characters = New-Object char[] $PaintWidth
    for ($index = 0; $index -lt $PaintWidth; $index++) {
        $owners[$index] = -1
        $characters[$index] = ' '
    }
    for ($segmentIndex = 0; $segmentIndex -lt $Line.Segments.Count; $segmentIndex++) {
        $segment = $Line.Segments[$segmentIndex]
        for ($index = 0; $index -lt $segment.Text.Length; $index++) {
            $position = $segment.X + $index
            if ($position -lt 0 -or $position -ge $PaintWidth) { continue }
            $owners[$position] = $segmentIndex
            $characters[$position] = $segment.Text[$index]
        }
    }

    $runs = [System.Collections.Generic.List[object]]::new()
    $start = 0
    for ($position = 1; $position -le $PaintWidth; $position++) {
        if ($position -lt $PaintWidth -and $owners[$position] -eq $owners[$start]) { continue }
        $runs.Add([pscustomobject]@{
            X = $start
            Text = [string]::new($characters, $start, $position - $start)
            Segment = if ($owners[$start] -ge 0) { $Line.Segments[$owners[$start]] } else { $null }
        })
        $start = $position
    }
    return @($runs)
}

function Write-TerminalInteractionFrame {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$HostState,
        [AllowNull()][object]$PreviousFrame
    )

    if ([Console]::IsOutputRedirected) {
        foreach ($line in @(ConvertTo-TerminalFrameText -Frame $Frame)) { [Console]::WriteLine($line) }
        return
    }

    $useVirtualTerminal = $HostState.VirtualTerminal -eq 'Supported'
    $paintWidth = [Math]::Max(1, $Frame.Width - 1)
    $changedLines = @(Get-TerminalChangedLineIndexes -Frame $Frame -PreviousFrame $PreviousFrame)
    if ($changedLines.Count -eq 0) { return }
    # Cells no segment owns take the theme surface, so a designed background
    # covers the whole line rather than only its text.
    $surfaceColors = Get-TerminalSegmentColors `
        -Segment ([pscustomobject]@{ PresentationRole = 'Body'; State = 'Normal' }) `
        -HostState $HostState
    $escape = [char]27
    # The changed lines go out in one write, bracketed as a synchronized
    # update, so the terminal never shows a half-painted frame: that is what
    # made the Header blink on every redraw.
    $builder = [System.Text.StringBuilder]::new()
    if ($useVirtualTerminal) { [void]$builder.Append("$escape[?2026h") }
    foreach ($lineIndex in $changedLines) {
        $line = if ($lineIndex -lt $Frame.Lines.Count) { $Frame.Lines[$lineIndex] } else { New-TerminalFrameLine -Width $Frame.Width }
        foreach ($run in @(ConvertTo-TerminalFrameRuns -Line $line -PaintWidth $paintWidth)) {
            $colors = if ($null -eq $run.Segment) {
                $surfaceColors
            } else {
                Get-TerminalSegmentColors -Segment $run.Segment -HostState $HostState
            }
            if ($useVirtualTerminal) {
                [void]$builder.Append((Get-TerminalSegmentSequence `
                    -X $run.X `
                    -Y $lineIndex `
                    -Text $run.Text `
                    -Foreground $colors.Foreground `
                    -Background $colors.Background `
                    -ForegroundRgb $colors.ForegroundRgb `
                    -BackgroundRgb $colors.BackgroundRgb))
            } else {
                Set-TerminalFrameCursorPosition -X $run.X -Y $lineIndex -HostState $HostState
                [Console]::ForegroundColor = $colors.Foreground
                [Console]::BackgroundColor = $colors.Background
                [Console]::Write($run.Text)
            }
        }
    }
    if ($useVirtualTerminal) {
        [void]$builder.Append((Get-TerminalTextAttributeSequence `
            -Foreground $HostState.OriginalForeground `
            -Background $HostState.OriginalBackground))
        [void]$builder.Append("$escape[?2026l")
        [Console]::Write($builder.ToString())
    } else {
        [Console]::ForegroundColor = $HostState.OriginalForeground
        [Console]::BackgroundColor = $HostState.OriginalBackground
    }
}
