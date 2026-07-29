# Non-interactive grid surface with a live or persistent result viewport.
function New-GridStatusBody {
    param([Parameter(Mandatory)][object[]]$Rows)

    $body = @()
    $prefixWidth = 0
    $columnCount = 0
    $trailingWidth = 0

    foreach ($row in $Rows) {
        if ($row.ContainsKey('Title')) {
            $body += @{ Kind = 'title'; Text = [string]$row['Title'] }
            continue
        }
        if (-not $row.ContainsKey('Cells')) {
            $body += @{ Kind = 'blank' }
            continue
        }

        $prefix = if ($row.ContainsKey('Prefix') -and $row['Prefix']) { [string]$row['Prefix'] } else { '' }
        $prefixWidth = [Math]::Max($prefixWidth, $prefix.Length)
        $cells = @($row['Cells'])
        $columnOffset = if ($row.ContainsKey('ColumnOffset')) { [int]$row['ColumnOffset'] } else { 0 }
        $columnCount = [Math]::Max($columnCount, $columnOffset + $cells.Count)
        $trailingCell = if ($row.ContainsKey('TrailingCell')) { $row['TrailingCell'] } else { $null }
        if ($trailingCell) {
            $trailingWidth = [Math]::Max($trailingWidth, ([string]$trailingCell.Label).Length)
        }
        $body += @{
            Kind = 'row'; Prefix = $prefix; Cells = $cells; ColumnOffset = $columnOffset; TrailingCell = $trailingCell
        }
    }

    if ($prefixWidth -gt 0) { $prefixWidth = $script:MenuCategoryWidth + $script:MenuGridGap }
    return [pscustomobject]@{
        Body          = @($body)
        PrefixWidth   = $prefixWidth
        ColumnCount   = [Math]::Max(1, $columnCount)
        TrailingWidth = $trailingWidth
    }
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
        [ValidateSet('Full', 'Compact')]
        [string]$BannerStyle = 'Compact'
    )

    $grid = New-GridStatusBody -Rows $Rows
    $layout = New-GridBodyLayout -CommandBody $grid.Body -ResultTitle $Title -BannerStyle $BannerStyle
    $lineArray = @($Lines)
    $resultOffset = if ($Follow) {
        Get-GridResultOffset -Current 0 -PageSize $layout.ResultRowCount -LineCount $lineArray.Count -Direction Last
    } else {
        0
    }

    $viewport = New-MenuViewport -Header $Header -Footer $Footer -BodyCount $layout.Body.Count -ClearScreen -BannerStyle $BannerStyle
    $columnWidths = Get-GridColumnWidths `
        -ContentWidth $viewport.ContentWidth `
        -PrefixWidth $grid.PrefixWidth `
        -ColumnCount $grid.ColumnCount
    $trailingGap = if ($grid.TrailingWidth -gt 0) { $script:MenuGridGap } else { 0 }
    $page = Get-GridResultPage -Offset $resultOffset -PageSize $layout.ResultRowCount -LineCount $lineArray.Count

    for ($index = 0; $index -lt $layout.Body.Count; $index++) {
        Write-GridLine -Top $viewport.BodyTop -Index $index -Body $layout.Body -ColW $columnWidths -PrefixW $grid.PrefixWidth `
            -InnerWidth $viewport.InnerWidth -ContentWidth $viewport.ContentWidth `
            -ActiveBodyIndex -1 -ActiveCol -1 `
            -TrailingWidth $grid.TrailingWidth -TrailingGap $trailingGap -TrailingColumn $grid.ColumnCount `
            -ResultLines $lineArray -ResultOffset $resultOffset -ResultPage $page.Number -ResultPageCount $page.Count
    }
}
