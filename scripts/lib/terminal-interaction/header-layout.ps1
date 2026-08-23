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

function Get-TerminalHeaderCommandRows {
    param(
        [Parameter(Mandatory)][object[]]$Commands,
        [Parameter(Mandatory)][int]$FirstWidth,
        [Parameter(Mandatory)][int]$SecondWidth
    )

    $first = [System.Collections.Generic.List[object]]::new()
    $second = [System.Collections.Generic.List[object]]::new()
    $firstLength = 0
    $secondLength = 0
    foreach ($command in @($Commands | Sort-Object Priority, DisplayOrder)) {
        $commandLength = Get-TerminalCommandLength -Command $command
        $nextFirstLength = if ($first.Count -eq 0) { $commandLength } else { $firstLength + 3 + $commandLength }
        if ($nextFirstLength -le $FirstWidth) {
            $first.Add($command)
            $firstLength = $nextFirstLength
            continue
        }

        $nextSecondLength = if ($second.Count -eq 0) { $commandLength } else { $secondLength + 3 + $commandLength }
        if ($nextSecondLength -le $SecondWidth) {
            $second.Add($command)
            $secondLength = $nextSecondLength
        }
    }

    return [pscustomobject]@{
        First = @($first | Sort-Object DisplayOrder)
        Second = @($second | Sort-Object DisplayOrder)
    }
}

function Add-TerminalCommandRow {
    param(
        [Parameter(Mandatory)][object]$Frame,
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
        [Parameter(Mandatory)][object]$Frame,
        [Parameter(Mandatory)][object]$View,
        [bool]$SupportsUnicode = $true
    )

    $left = 2
    $right = [Math]::Max($left, $Frame.Width - 2)
    $contentWidth = [Math]::Max(1, $right - $left)
    $banner = Limit-TerminalText -Text $View.Banner -Width $contentWidth
    $context = if ($View.Context) {
        Limit-TerminalText -Text ([string]$View.Context) -Width $contentWidth
    } else {
        ''
    }
    $firstCommandWidth = [Math]::Max(0, $right - ($left + $banner.Length + 4))
    $secondCommandWidth = if ($context.Length -gt 0) {
        [Math]::Max(0, $right - ($left + $context.Length + 4))
    } else {
        $contentWidth
    }
    $commands = @(Get-TerminalHeaderCommands -View $View -SupportsUnicode $SupportsUnicode)
    $commandRows = Get-TerminalHeaderCommandRows `
        -Commands $commands `
        -FirstWidth $firstCommandWidth `
        -SecondWidth $secondCommandWidth

    $titleLine = Add-TerminalFrameLine -Frame $Frame
    Add-TerminalFrameSegment -Frame $Frame -LineIndex $titleLine -X $left -Text $banner -PresentationRole Banner
    if ($commandRows.First.Count -gt 0) {
        $firstLength = Get-TerminalCommandRowLength -Commands $commandRows.First
        Add-TerminalCommandRow `
            -Frame $Frame `
            -LineIndex $titleLine `
            -Commands $commandRows.First `
            -StartX ($right - $firstLength)
    }

    $contextLine = Add-TerminalFrameLine -Frame $Frame
    if ($context.Length -gt 0) {
        Add-TerminalFrameSegment -Frame $Frame -LineIndex $contextLine -X $left -Text $context -PresentationRole Context
    }
    if ($commandRows.Second.Count -gt 0) {
        $secondLength = Get-TerminalCommandRowLength -Commands $commandRows.Second
        Add-TerminalCommandRow `
            -Frame $Frame `
            -LineIndex $contextLine `
            -Commands $commandRows.Second `
            -StartX ($right - $secondLength)
    }

    $separatorLine = Add-TerminalFrameLine -Frame $Frame
    Add-TerminalFrameSegment `
        -Frame $Frame `
        -LineIndex $separatorLine `
        -X 0 `
        -Text ([string]::new([char]0x2500, $Frame.Width)) `
        -PresentationRole PrimarySeparator
}
