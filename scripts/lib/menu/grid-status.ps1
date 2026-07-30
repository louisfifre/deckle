# Non-interactive grid surface with a live or persistent result viewport.

function Write-GridStatusRows {
    param(
        [Parameter(Mandatory)]$View,
        [Parameter(Mandatory)][int]$StartIndex,
        [Parameter(Mandatory)][string[]]$Lines,
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
        [string[]]$Lines = @(),
        [string]$Footer = '',
        [switch]$Follow,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    $grid = New-GridPlan -Rows $Rows
    $layout = New-GridBodyLayout -CommandBody $grid.Body -ResultTitle $Title -BannerStyle $BannerStyle
    $viewport = New-MenuViewport -Header $Header -Footer $Footer -BodyCount $layout.Body.Count -ClearScreen -BannerStyle $BannerStyle
    $metrics = Get-MenuMetrics
    $view = [pscustomobject]@{
        Header           = $Header
        Rows             = @($Rows)
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
    }
    $lineArray = @($Lines)
    $resultOffset = if ($Follow) {
        Get-GridResultOffset -Current 0 -PageSize $view.ResultRowCount -LineCount $lineArray.Count -Direction Last
    } else {
        0
    }
    Write-GridStatusRows -View $view -StartIndex 0 -Lines $lineArray -ResultOffset $resultOffset
    return $view
}

function Update-GridStatusView {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$View,
        [Parameter(Mandatory)][string]$Title,
        [string[]]$Lines = @(),
        [switch]$Follow
    )

    $metrics = Get-MenuMetrics
    if ($metrics.TerminalWidth -ne $View.TerminalWidth -or $metrics.WindowHeight -ne $View.WindowHeight) {
        return New-GridStatusView `
            -Header $View.Header -Rows $View.Rows -Title $Title -Lines $Lines -Footer $View.Footer `
            -BannerStyle $View.BannerStyle -Follow:$Follow
    }

    $View.Body[$View.ResultTitleIndex].Text = $Title
    $lineArray = @($Lines)
    $resultOffset = if ($Follow) {
        Get-GridResultOffset -Current 0 -PageSize $View.ResultRowCount -LineCount $lineArray.Count -Direction Last
    } else {
        0
    }
    Write-GridStatusRows -View $View -StartIndex $View.ResultTitleIndex -Lines $lineArray -ResultOffset $resultOffset
    return $View
}

function Show-GridStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Rows,
        [Parameter(Mandatory)][string]$Title,
        [string[]]$Lines = @(),
        [string]$Footer = '',
        [switch]$Follow,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    New-GridStatusView -Header $Header -Rows $Rows -Title $Title -Lines $Lines -Footer $Footer -Follow:$Follow -BannerStyle $BannerStyle | Out-Null
}
