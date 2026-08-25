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

function Get-TerminalSegmentColors {
    param(
        [Parameter(Mandatory)][object]$Segment,
        [Parameter(Mandatory)][object]$HostState
    )

    $colorCapability = 'Supported'
    $colorProperty = $HostState.PSObject.Properties['Color']
    if ($null -ne $colorProperty) { $colorCapability = [string]$colorProperty.Value }
    $style = Get-TerminalPresentationStyle `
        -Role $Segment.PresentationRole `
        -State $Segment.State `
        -ColorCapability $colorCapability
    $foreground = if ($null -eq $style.Foreground) { $HostState.OriginalForeground } else { $style.Foreground }
    $background = if ($null -eq $style.Background) { $HostState.OriginalBackground } else { $style.Background }
    return [pscustomobject]@{ Foreground = $foreground; Background = $background }
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
        [Parameter(Mandatory)][ConsoleColor]$Background
    )

    $escape = [char]27
    $foregroundCode = Get-TerminalColorSequenceCode -Color $Foreground
    $backgroundCode = Get-TerminalColorSequenceCode -Color $Background -Background
    return "$escape[$foregroundCode;${backgroundCode}m"
}

function Get-TerminalSegmentSequence {
    param(
        [Parameter(Mandatory)][ValidateRange(0, 1000000)][int]$X,
        [Parameter(Mandatory)][ValidateRange(0, 1000000)][int]$Y,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][ConsoleColor]$Foreground,
        [Parameter(Mandatory)][ConsoleColor]$Background
    )

    $position = Get-TerminalCursorPositionSequence -X $X -Y $Y
    $attributes = Get-TerminalTextAttributeSequence -Foreground $Foreground -Background $Background
    return "$position$attributes$Text"
}

function Write-TerminalInteractionFrame {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$HostState
    )

    if ([Console]::IsOutputRedirected) {
        foreach ($line in @(ConvertTo-TerminalFrameText -Frame $Frame)) { [Console]::WriteLine($line) }
        return
    }

    $useVirtualTerminal = $HostState.VirtualTerminal -eq 'Supported'
    $paintWidth = [Math]::Max(1, $Frame.Width - 1)
    for ($lineIndex = 0; $lineIndex -lt $Frame.Lines.Count; $lineIndex++) {
        $blankLine = [string]::new(' ', $paintWidth)
        if ($useVirtualTerminal) {
            [Console]::Write((Get-TerminalSegmentSequence `
                -X 0 `
                -Y $lineIndex `
                -Text $blankLine `
                -Foreground $HostState.OriginalForeground `
                -Background $HostState.OriginalBackground))
        } else {
            Set-TerminalFrameCursorPosition -X 0 -Y $lineIndex -HostState $HostState
            [Console]::ForegroundColor = $HostState.OriginalForeground
            [Console]::BackgroundColor = $HostState.OriginalBackground
            [Console]::Write($blankLine)
        }

        foreach ($segment in $Frame.Lines[$lineIndex].Segments) {
            if ($segment.X -ge $paintWidth) { continue }
            $text = $segment.Text
            $available = $paintWidth - $segment.X
            if ($text.Length -gt $available) { $text = $text.Substring(0, $available) }
            if ($text.Length -eq 0) { continue }
            $colors = Get-TerminalSegmentColors -Segment $segment -HostState $HostState
            if ($useVirtualTerminal) {
                [Console]::Write((Get-TerminalSegmentSequence `
                    -X $segment.X `
                    -Y $lineIndex `
                    -Text $text `
                    -Foreground $colors.Foreground `
                    -Background $colors.Background))
            } else {
                Set-TerminalFrameCursorPosition -X $segment.X -Y $lineIndex -HostState $HostState
                [Console]::ForegroundColor = $colors.Foreground
                [Console]::BackgroundColor = $colors.Background
                [Console]::Write($text)
            }
        }
    }
    if ($useVirtualTerminal) {
        [Console]::Write((Get-TerminalTextAttributeSequence `
            -Foreground $HostState.OriginalForeground `
            -Background $HostState.OriginalBackground))
    } else {
        [Console]::ForegroundColor = $HostState.OriginalForeground
        [Console]::BackgroundColor = $HostState.OriginalBackground
    }
}
