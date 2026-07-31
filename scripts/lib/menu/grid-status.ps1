# Non-interactive grid surface with a live or persistent result viewport.

function Get-GridStatusCursorVisibility {
    if ([Console]::IsOutputRedirected) { return $true }
    try { return [Console]::CursorVisible } catch { return $true }
}

function Set-GridStatusCursorVisibility {
    param([Parameter(Mandatory)][bool]$Visible)

    if ([Console]::IsOutputRedirected) { return }
    try { [Console]::CursorVisible = $Visible } catch { }
}

function Set-GridStatusCursorParking {
    param([Parameter(Mandatory)]$View)

    $parkingRow = $View.Viewport.BodyTop + [Math]::Max(0, $View.Body.Count - 1)
    Set-MenuCursorPosition -Left 0 -Top $parkingRow
}

function Write-GridStatusRows {
    param(
        [Parameter(Mandatory)]$View,
        [Parameter(Mandatory)][int]$StartIndex,
        [Parameter(Mandatory)][object[]]$Lines,
        [Parameter(Mandatory)][int]$ResultOffset
    )

    $page = Get-GridResultPage -Offset $ResultOffset -PageSize $View.ResultRowCount -LineCount $Lines.Count
    for ($index = $StartIndex; $index -lt $View.Body.Count; $index++) {
        Write-GridLine -Top $View.Viewport.BodyTop -Index $index -Body $View.Body -ColW $View.ColumnWidths -PrefixW $View.Grid.PrefixWidth `
            -InnerWidth $View.Viewport.InnerWidth -ContentWidth $View.Viewport.ContentWidth `
            -ActiveBodyIndex -1 -ActiveCol -1 `
            -TrailingWidth $View.Grid.TrailingWidth -TrailingGap $View.Grid.TrailingGap -TrailingColumn $View.Grid.ColumnCount `
            -ResultLines $Lines -ResultOffset $ResultOffset -ResultPage $page.Number -ResultPageCount $page.Count
    }
}

function New-GridStatusView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Rows,
        [Parameter(Mandatory)][string]$Title,
        [object[]]$Lines = @(),
        [string]$HeaderCommands = '',
        [string]$Footer = '',
        [switch]$Follow,
        [AllowNull()][Nullable[bool]]$RestoreCursorVisible = $null,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    $cursorVisibleBeforeStart = if ($null -eq $RestoreCursorVisible) {
        Get-GridStatusCursorVisibility
    } else {
        [bool]$RestoreCursorVisible
    }
    $grid = New-GridPlan -Rows $Rows
    $layout = New-GridBodyLayout -CommandBody $grid.Body -ResultTitle $Title -BannerStyle $BannerStyle
    Set-GridStatusCursorVisibility -Visible $false
    try {
        $viewport = New-MenuViewport `
            -Header $Header -HeaderCommands $HeaderCommands -Footer $Footer `
            -BodyCount $layout.Body.Count -ClearScreen -BannerStyle $BannerStyle
    } catch {
        Set-GridStatusCursorVisibility -Visible $cursorVisibleBeforeStart
        throw
    }
    $metrics = Get-MenuMetrics
    $view = [pscustomobject]@{
        Header           = $Header
        Rows             = @($Rows)
        HeaderCommands   = $HeaderCommands
        Footer           = $Footer
        BannerStyle      = $BannerStyle
        Grid             = $grid
        Body             = $layout.Body
        ResultTitleIndex = $grid.Body.Count + 1
        ResultRowCount   = $layout.ResultRowCount
        Viewport         = $viewport
        ColumnWidths     = Get-GridColumnWidths -ContentWidth $viewport.ContentWidth -PrefixWidth $grid.PrefixWidth -ColumnCount $grid.ColumnCount
        TerminalWidth    = $metrics.TerminalWidth
        WindowHeight     = $metrics.WindowHeight
        RestoreCursorVisible = $cursorVisibleBeforeStart
    }
    $lineArray = @($Lines)
    $resultOffset = if ($Follow) {
        Get-GridResultOffset -Current 0 -PageSize $view.ResultRowCount -LineCount $lineArray.Count -Direction Last
    } else {
        0
    }
    Write-GridStatusRows -View $view -StartIndex 0 -Lines $lineArray -ResultOffset $resultOffset
    Set-GridStatusCursorParking -View $view
    return $view
}

function Update-GridStatusView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$View,
        [Parameter(Mandatory)][string]$Title,
        [object[]]$Lines = @(),
        [switch]$Follow
    )

    $metrics = Get-MenuMetrics
    if ($metrics.TerminalWidth -ne $View.TerminalWidth -or $metrics.WindowHeight -ne $View.WindowHeight) {
        return New-GridStatusView `
            -Header $View.Header -Rows $View.Rows -Title $Title -Lines $Lines -Footer $View.Footer `
            -HeaderCommands $View.HeaderCommands -BannerStyle $View.BannerStyle -Follow:$Follow `
            -RestoreCursorVisible $View.RestoreCursorVisible
    }

    $View.Body[$View.ResultTitleIndex].Text = $Title
    $lineArray = @($Lines)
    $resultOffset = if ($Follow) {
        Get-GridResultOffset -Current 0 -PageSize $View.ResultRowCount -LineCount $lineArray.Count -Direction Last
    } else {
        0
    }
    Write-GridStatusRows -View $View -StartIndex $View.ResultTitleIndex -Lines $lineArray -ResultOffset $resultOffset
    Set-GridStatusCursorParking -View $View
    return $View
}

function Close-GridStatusView {
    [CmdletBinding()]
    param([AllowNull()]$View)

    if ($null -eq $View) { return }
    Set-GridStatusCursorVisibility -Visible ([bool]$View.RestoreCursorVisible)
}

function Show-GridStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Rows,
        [Parameter(Mandatory)][string]$Title,
        [object[]]$Lines = @(),
        [string]$Footer = '',
        [switch]$Follow,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    $view = $null
    try {
        $view = New-GridStatusView `
            -Header $Header -Rows $Rows -Title $Title -Lines $Lines `
            -Footer $Footer -Follow:$Follow -BannerStyle $BannerStyle
    } finally {
        Close-GridStatusView -View $view
    }
}
