# Persistent Header layout and command-indication prioritization.

function Get-TerminalHeaderCommands {
    param(
        [Parameter(Mandatory)][object]$View,
        [bool]$SupportsUnicode = $true
    )

    $arrows = if ($SupportsUnicode) {
        -join @([char]0x2191, [char]0x2193, [char]0x2190, [char]0x2192)
    } else {
        'Arrows'
    }
    $commands = [System.Collections.Generic.List[object]]::new()
    $displayOrder = 0
    $quitPriority = if ($View.Kind -eq 'Execution' -and $View.State -eq 'Running') { 1 } else { 60 }
    $quitCommand = [pscustomobject]@{
        Key = 'Ctrl+C'
        Label = 'Quit'
        Priority = $quitPriority
        DisplayOrder = 100
    }
    if ($View.Kind -ne 'Execution' -or $View.State -ne 'Running') {
        $commands.Add([pscustomobject]@{
            Key = $arrows
            Label = 'Move'
            Priority = 50
            DisplayOrder = $displayOrder++
        })
        $activationLabel = if ($View.Kind -eq 'Preparation') { 'Select' } else { 'Open' }
        $commands.Add([pscustomobject]@{
            Key = 'Enter'
            Label = $activationLabel
            Priority = 10
            DisplayOrder = $displayOrder++
        })
        if ($View.Kind -eq 'Preparation' -and @($View.Selectors | Where-Object { $_.SelectionMode -eq 'Multiple' }).Count -gt 0) {
            $commands.Add([pscustomobject]@{
                Key = 'Space'
                Label = 'Toggle'
                Priority = 20
                DisplayOrder = $displayOrder++
            })
        }
        if ($null -ne $View.BackTarget) {
            $commands.Add([pscustomobject]@{
                Key = 'Backspace'
                Label = 'Back'
                Priority = 40
                DisplayOrder = $displayOrder++
            })
            $commands.Add([pscustomobject]@{
                Key = 'Escape'
                Label = 'Back'
                Priority = 30
                DisplayOrder = $displayOrder++
            })
        } elseif ($View.Kind -eq 'ActionMenu') {
            $commands.Add([pscustomobject]@{
                Key = 'Escape'
                Label = $quitCommand.Label
                Priority = 30
                DisplayOrder = $displayOrder++
            })
        }
    }
    $commands.Add($quitCommand)
    return @($commands)
}

function Get-TerminalCommandLength {
    param([Parameter(Mandatory)][object]$Command)

    return $Command.Key.Length + 1 + $Command.Label.Length
}

function Get-TerminalCommandRowLength {
    param([Parameter(Mandatory)][object[]]$Commands)

    $length = 0
    for ($index = 0; $index -lt $Commands.Count; $index++) {
        if ($index -gt 0) { $length += 3 }
        $length += Get-TerminalCommandLength -Command $Commands[$index]
    }
    return $length
}

function Select-TerminalHeaderCommands {
    param(
        [Parameter(Mandatory)][object[]]$Commands,
        [Parameter(Mandatory)][int]$Width
    )

    # Commands enter by priority while they fit, then show in display order.
    $selected = [System.Collections.Generic.List[object]]::new()
    $length = 0
    foreach ($command in @($Commands | Sort-Object Priority, DisplayOrder)) {
        $commandLength = Get-TerminalCommandLength -Command $command
        $nextLength = if ($selected.Count -eq 0) { $commandLength } else { $length + 3 + $commandLength }
        if ($nextLength -le $Width) {
            $selected.Add($command)
            $length = $nextLength
        }
    }

    return @($selected | Sort-Object DisplayOrder)
}

function Add-TerminalCommandRow {
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.TerminalInteraction.Frame')][object]$Frame,
        [Parameter(Mandatory)][int]$LineIndex,
        [Parameter(Mandatory)][object[]]$Commands,
        [Parameter(Mandatory)][int]$StartX
    )

    $x = $StartX
    for ($index = 0; $index -lt $Commands.Count; $index++) {
        if ($index -gt 0) { $x += 3 }
        $command = $Commands[$index]
        Add-TerminalFrameSegment -Frame $Frame -LineIndex $LineIndex -X $x -Text $command.Key -PresentationRole CommandKey
        $x += $command.Key.Length + 1
        Add-TerminalFrameSegment -Frame $Frame -LineIndex $LineIndex -X $x -Text $command.Label -PresentationRole CommandLabel
        $x += $command.Label.Length
    }
}

function Add-TerminalHeader {
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.TerminalInteraction.Frame')][object]$Frame,
        [Parameter(Mandatory)][object]$View,
        [bool]$SupportsUnicode = $true,
        [AllowEmptyCollection()][string[]]$Logo = @()
    )

    $left = 2
    $right = [Math]::Max($left, $Frame.Width - 2)
    $contentWidth = [Math]::Max(1, $right - $left)

    # The logo opens the frame when the terminal affords it: it is drawn in
    # Unicode block glyphs, needs the width of its widest line, and must
    # leave the body a dozen lines under the two Header lines. A short,
    # narrow or ASCII-only terminal keeps the Header to its title and rule.
    $logoLines = @($Logo | Where-Object { -not [string]::IsNullOrEmpty($_) })
    $logoWidth = 0
    foreach ($logoLine in $logoLines) { $logoWidth = [Math]::Max($logoWidth, $logoLine.Length) }
    $minimumBodyHeight = 12
    $showLogo = $logoLines.Count -gt 0 -and $SupportsUnicode -and
        $Frame.Width -ge ($logoWidth + 2 * $left) -and
        ($Frame.Height - ($logoLines.Count + 1) - 2) -ge $minimumBodyHeight
    if ($showLogo) {
        foreach ($logoLine in $logoLines) {
            $line = Add-TerminalFrameLine -Frame $Frame
            Add-TerminalFrameSegment -Frame $Frame -LineIndex $line -X $left -Text $logoLine -PresentationRole Banner
        }
        [void](Add-TerminalFrameLine -Frame $Frame)
    }
    # One title line, the rule right under it: the banner, then the View's
    # place as a breadcrumb after a chevron, and the commands at the end of the
    # line. Commands keep the room the banner leaves them, by priority; the
    # breadcrumb takes what remains and shortens, the Header never grows.
    $banner = Limit-TerminalText -Text $View.Banner -Width $contentWidth
    $commandWidth = [Math]::Max(0, $right - ($left + $banner.Length + 4))
    $commands = @(Get-TerminalHeaderCommands -View $View -SupportsUnicode $SupportsUnicode)
    $commandRow = @(Select-TerminalHeaderCommands -Commands $commands -Width $commandWidth)
    $commandLength = if ($commandRow.Count -gt 0) { Get-TerminalCommandRowLength -Commands $commandRow } else { 0 }

    $titleLine = Add-TerminalFrameLine -Frame $Frame
    Add-TerminalFrameSegment -Frame $Frame -LineIndex $titleLine -X $left -Text $banner -PresentationRole Banner
    if ($View.Context) {
        $chevron = ' > '
        $contextX = $left + $banner.Length + $chevron.Length
        $commandGap = if ($commandRow.Count -gt 0) { 3 } else { 0 }
        $context = Limit-TerminalText -Text ([string]$View.Context) -Width ($right - $commandLength - $commandGap - $contextX)
        if ($context.Length -gt 0) {
            Add-TerminalFrameSegment -Frame $Frame -LineIndex $titleLine -X ($left + $banner.Length) -Text $chevron -PresentationRole Context
            Add-TerminalFrameSegment -Frame $Frame -LineIndex $titleLine -X $contextX -Text $context -PresentationRole Context
        }
    }
    if ($commandRow.Count -gt 0) {
        Add-TerminalCommandRow `
            -Frame $Frame `
            -LineIndex $titleLine `
            -Commands $commandRow `
            -StartX ($right - $commandLength)
    }

    $separatorLine = Add-TerminalFrameLine -Frame $Frame
    Add-TerminalFrameSegment `
        -Frame $Frame `
        -LineIndex $separatorLine `
        -X 0 `
        -Text ([string]::new([char]0x2500, $Frame.Width)) `
        -PresentationRole PrimarySeparator
}
