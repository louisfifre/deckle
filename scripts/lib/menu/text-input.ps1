# Single-line text entry that stays inside Deckle's compact menu chrome.

function Read-MenuText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][string]$Title,
        [string[]]$Lines = @(),
        [string]$Label = 'Value',
        [AllowEmptyString()][string]$Default = '',
        [string]$Footer = 'Type a value   Enter confirm   Esc back',
        [ValidateSet('Full', 'Compact')]
        [string]$BannerStyle = 'Compact'
    )

    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) {
        $answer = Read-Host $Title
        if ([string]::IsNullOrWhiteSpace($answer)) { return $null }
        return $answer.Trim()
    }

    $bodyCount = 5
    $buffer = [string]$Default
    $cursor = $buffer.Length
    $viewport = $null
    $metrics = $null

    $render = {
        $viewport = New-MenuViewport -Header $Header -Footer $Footer -BodyCount $bodyCount -ClearScreen -BannerStyle $BannerStyle
        $metrics = Get-MenuMetrics
        Write-MenuPlainLine -Row $viewport.BodyTop -Text ('  ' + $Title.ToUpperInvariant()) -ForegroundColor Magenta -BackgroundColor $null
        for ($index = 0; $index -lt 2; $index++) {
            $text = if ($index -lt $Lines.Count) { '  ' + $Lines[$index] } else { '' }
            Write-MenuPlainLine -Row ($viewport.BodyTop + 1 + $index) -Text $text -ForegroundColor DarkGray -BackgroundColor $null
        }
        Write-MenuPlainLine -Row ($viewport.BodyTop + 3) -Text '' -ForegroundColor $null -BackgroundColor $null
    }

    $renderInput = {
        $prefix = "  $Label  "
        $available = [Math]::Max(1, $metrics.ContentWidth - $prefix.Length)
        $start = [Math]::Max(0, $cursor - $available + 1)
        if ($start -gt $buffer.Length) { $start = $buffer.Length }
        $length = [Math]::Min($available, $buffer.Length - $start)
        $visible = if ($length -gt 0) { $buffer.Substring($start, $length) } else { '' }
        Write-MenuPlainLine -Row ($viewport.BodyTop + 4) -Text ($prefix + $visible) -ForegroundColor $null -BackgroundColor $null
        Set-MenuCursorPosition -Left ([Math]::Min($metrics.TerminalWidth - 1, $prefix.Length + $cursor - $start)) -Top ($viewport.BodyTop + 4)
    }

    . $render
    [Console]::CursorVisible = $true
    try {
        while ($true) {
            . $renderInput
            $key = [Console]::ReadKey($true)
            $currentMetrics = Get-MenuMetrics
            if ($currentMetrics.TerminalWidth -ne $metrics.TerminalWidth -or $currentMetrics.WindowHeight -ne $metrics.WindowHeight) {
                . $render
            }

            switch ($key.Key) {
                'Enter' {
                    $value = $buffer.Trim()
                    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
                    return $value
                }
                'Escape'    { return $null }
                'LeftArrow' { if ($cursor -gt 0) { $cursor-- } }
                'RightArrow' { if ($cursor -lt $buffer.Length) { $cursor++ } }
                'Home' { $cursor = 0 }
                'End'  { $cursor = $buffer.Length }
                'Backspace' {
                    if ($cursor -gt 0) {
                        $buffer = $buffer.Remove($cursor - 1, 1)
                        $cursor--
                    }
                }
                'Delete' {
                    if ($cursor -lt $buffer.Length) { $buffer = $buffer.Remove($cursor, 1) }
                }
                default {
                    if ($buffer.Length -lt 1024 -and
                        -not [char]::IsControl($key.KeyChar) -and
                        ($key.Modifiers -band ([ConsoleModifiers]::Control -bor [ConsoleModifiers]::Alt)) -eq 0) {
                        $buffer = $buffer.Insert($cursor, [string]$key.KeyChar)
                        $cursor++
                    }
                }
            }
        }
    } finally {
        [Console]::CursorVisible = $false
        if ($viewport) { Set-MenuCursorPosition -Left 0 -Top $viewport.Bottom }
    }
}
