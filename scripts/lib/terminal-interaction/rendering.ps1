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

function Write-TerminalInteractionFrame {
    param(
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$HostState
    )

    if ([Console]::IsOutputRedirected) {
        foreach ($line in @(ConvertTo-TerminalFrameText -Frame $Frame)) { [Console]::WriteLine($line) }
        return
    }

    $paintWidth = [Math]::Max(1, $Frame.Width - 1)
    for ($lineIndex = 0; $lineIndex -lt $Frame.Lines.Count; $lineIndex++) {
        Set-TerminalFrameCursorPosition -X 0 -Y $lineIndex -HostState $HostState
        [Console]::ForegroundColor = $HostState.OriginalForeground
        [Console]::BackgroundColor = $HostState.OriginalBackground
        [Console]::Write([string]::new(' ', $paintWidth))

        foreach ($segment in $Frame.Lines[$lineIndex].Segments) {
            if ($segment.X -ge $paintWidth) { continue }
            $text = $segment.Text
            $available = $paintWidth - $segment.X
            if ($text.Length -gt $available) { $text = $text.Substring(0, $available) }
            if ($text.Length -eq 0) { continue }
            $colors = Get-TerminalSegmentColors -Segment $segment -HostState $HostState
            Set-TerminalFrameCursorPosition -X $segment.X -Y $lineIndex -HostState $HostState
            [Console]::ForegroundColor = $colors.Foreground
            [Console]::BackgroundColor = $colors.Background
            [Console]::Write($text)
        }
    }
    [Console]::ForegroundColor = $HostState.OriginalForeground
    [Console]::BackgroundColor = $HostState.OriginalBackground
}
