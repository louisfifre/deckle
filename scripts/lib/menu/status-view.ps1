# Non-interactive status surface shown while a menu action is running.
function Show-MenuStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][string]$Title,
        [string[]]$Lines = @(),
        [string]$Footer = '',
        [ValidateSet('Full', 'Compact')]
        [string]$BannerStyle = 'Compact'
    )

    $lineArray = @($Lines)
    $capacity = Get-MenuBodyCapacity -BannerStyle $BannerStyle
    $visibleLineCount = [Math]::Min($lineArray.Count, [Math]::Max(1, $capacity - 1))
    $body = @(@{ Kind = 'title'; Text = $Title })
    for ($slot = 0; $slot -lt $visibleLineCount; $slot++) {
        $body += @{ Kind = 'result'; Slot = $slot }
    }

    $viewport = New-MenuViewport -Header $Header -Footer $Footer -BodyCount $body.Count -ClearScreen -BannerStyle $BannerStyle
    for ($index = 0; $index -lt $body.Count; $index++) {
        Write-GridLine -Top $viewport.BodyTop -Index $index -Body $body -ColW @{} -PrefixW 0 `
            -InnerWidth $viewport.InnerWidth -ContentWidth $viewport.ContentWidth `
            -ActiveBodyIndex -1 -ActiveCol -1 -ResultLines $lineArray -ResultOffset 0
    }
}
