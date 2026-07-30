# Non-interactive status surface shown while a menu action is running.
function Get-MenuStatusLayout {
    param(
        [Parameter(Mandatory)][int]$LineCount,
        [Parameter(Mandatory)][int]$BodyCapacity,
        [switch]$Follow
    )

    $visibleLineCount = [Math]::Max(1, $BodyCapacity - 1)
    $lineOffset = if ($Follow) {
        [Math]::Max(0, $LineCount - $visibleLineCount)
    } else {
        0
    }
    return [pscustomobject]@{
        VisibleLineCount = $visibleLineCount
        LineOffset       = $lineOffset
    }
}

function Show-MenuStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][string]$Title,
        [string[]]$Lines = @(),
        [string]$Footer = '',
        [switch]$Follow,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    $lineArray = @($Lines)
    $capacity = Get-MenuBodyCapacity -BannerStyle $BannerStyle
    $layout = Get-MenuStatusLayout -LineCount $lineArray.Count -BodyCapacity $capacity -Follow:$Follow
    $body = @(@{ Kind = 'title'; Text = $Title })
    for ($slot = 0; $slot -lt $layout.VisibleLineCount; $slot++) {
        $body += @{ Kind = 'result'; Slot = $slot }
    }

    $viewport = New-MenuViewport -Header $Header -Footer $Footer -BodyCount $body.Count -ClearScreen -BannerStyle $BannerStyle
    for ($index = 0; $index -lt $body.Count; $index++) {
        Write-GridLine -Top $viewport.BodyTop -Index $index -Body $body -ColW @{} -PrefixW 0 `
            -InnerWidth $viewport.InnerWidth -ContentWidth $viewport.ContentWidth `
            -ActiveBodyIndex -1 -ActiveCol -1 -ResultLines $lineArray -ResultOffset $layout.LineOffset
    }
}
