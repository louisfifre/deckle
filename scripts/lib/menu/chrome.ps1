# Shared menu chrome and rendering primitives.
function Get-MenuMetrics {
    $terminalWidth = [Math]::Max(40, [Console]::WindowWidth - 1)
    $innerWidth = [Math]::Max(24, $terminalWidth - 4)
    [pscustomobject]@{
        TerminalWidth = $terminalWidth
        InnerWidth    = $innerWidth
    }
}

function Get-MenuCellRole {
    param($Cell)

    if ($Cell -is [hashtable] -and $Cell.ContainsKey('Role')) {
        return [string]$Cell['Role']
    }

    $roleProp = $Cell.PSObject.Properties['Role']
    if ($null -ne $roleProp -and $roleProp.Value) { return [string]$roleProp.Value }

    $label = if ($Cell -is [hashtable]) { [string]$Cell['Label'] } else { [string]$Cell.Label }
    if ($label -eq '< Back') { return 'back' }
    if ($label.EndsWith([char]0x2026) -or $label.EndsWith('...')) { return 'folder' }
    return 'action'
}

function Get-MenuRoleColor {
    param(
        [string]$Role,
        [switch]$Selected
    )

    if ($Selected) {
        return @{ Foreground = 'Black'; Background = 'Gray' }
    }

    switch ($Role) {
        'folder' { return @{ Foreground = 'DarkYellow'; Background = $null } }
        'back'   { return @{ Foreground = 'DarkGray';   Background = $null } }
        default  { return @{ Foreground = $null;        Background = $null } }
    }
}

function New-MenuRule {
    param(
        [int]$MaxWidth,
        [ValidateSet('Solid', 'Section')]
        [string]$Style = 'Solid'
    )

    $width = [Math]::Min(56, [Math]::Max(0, $MaxWidth))
    if ($Style -eq 'Section') {
        return (('- ' * [Math]::Ceiling($width / 2.0)).Substring(0, $width))
    }
    return ([string][char]0x2500) * $width
}

function Get-MenuBanner {
    # The figlet is duplicated in src/Deckle.Installer/Ui/ConsoleUi.cs (BannerArt) —
    # PowerShell and C# cannot share a source; keep the two in sync.
    @(
        '  ____   _____   ____  _  __  _      _____'
        ' |  _ \ | ____| / ___|| |/ / | |    | ____|'
        ' | | | ||  _|  | |    |   /  | |    |  _|'
        ' | |_| || |___ | |___ |   \  | |___ | |___'
        ' |____/ |_____| \____||_|\_\ |_____||_____|'
        '  S C R I P T S'
    )
}

function Limit-MenuText {
    param(
        [AllowNull()][string]$Text,
        [int]$Width
    )

    $value = [string]$Text
    if ($Width -le 0) { return '' }
    if ($value.Length -le $Width) { return $value }
    if ($Width -le 1) { return ([char]0x2026) }
    return $value.Substring(0, $Width - 1) + ([char]0x2026)
}

function Write-MenuSegment {
    param(
        [string]$Text,
        [string]$ForegroundColor,
        [string]$BackgroundColor
    )

    if (-not [Console]::IsOutputRedirected -and ($ForegroundColor -or $BackgroundColor)) {
        try {
            $prefix = ''
            if ($ForegroundColor) {
                $prefix += $PSStyle.Foreground.FromConsoleColor([ConsoleColor]$ForegroundColor)
            }
            if ($BackgroundColor) {
                $prefix += $PSStyle.Background.FromConsoleColor([ConsoleColor]$BackgroundColor)
            }
            Write-Host "$prefix$Text$($PSStyle.Reset)" -NoNewline
            return
        } catch {
            # Fall through to host colors if PSStyle is unavailable or rejects a color.
        }
    }

    $args = @{ Object = $Text; NoNewline = $true }
    if ($ForegroundColor) { $args.ForegroundColor = $ForegroundColor }
    if ($BackgroundColor) { $args.BackgroundColor = $BackgroundColor }
    Write-Host @args
}

function Set-MenuCursorPosition {
    param(
        [int]$Left,
        [int]$Top
    )

    if ([Console]::IsOutputRedirected) { return }
    try {
        [Console]::SetCursorPosition($Left, $Top)
    } catch {
        # Non-interactive hosts can reject cursor movement; keep rendering testable.
    }
}

function Write-MenuPlainLine {
    param(
        [int]$Row,
        [string]$Text,
        [string]$ForegroundColor,
        [string]$BackgroundColor
    )

    $metrics = Get-MenuMetrics
    $line = Limit-MenuText -Text $Text -Width $metrics.TerminalWidth
    if ($line.Length -lt $metrics.TerminalWidth) {
        $line += ' ' * ($metrics.TerminalWidth - $line.Length)
    }
    Set-MenuCursorPosition -Left 0 -Top $Row
    Write-MenuSegment -Text $line -ForegroundColor $ForegroundColor -BackgroundColor $BackgroundColor
}

function Write-MenuChrome {
    param(
        [int]$BaseRow,
        [string]$Header,
        [string]$Footer,
        [int]$BodyCount
    )

    $metrics = Get-MenuMetrics
    $banner = Get-MenuBanner
    for ($i = 0; $i -lt $banner.Count; $i++) {
        Write-MenuPlainLine -Row ($BaseRow + $i) -Text $banner[$i] -ForegroundColor Blue -BackgroundColor $null
    }

    $headerRow = $BaseRow + $banner.Count
    Write-MenuPlainLine -Row $headerRow -Text (' ' + $Header) -ForegroundColor DarkGray -BackgroundColor $null
    Write-MenuPlainLine -Row ($headerRow + 1) -Text (' ' + (New-MenuRule -MaxWidth $metrics.InnerWidth)) -ForegroundColor DarkGray -BackgroundColor $null
    Write-MenuPlainLine -Row ($headerRow + 2) -Text '' -ForegroundColor $null -BackgroundColor $null
    Write-MenuPlainLine -Row ($headerRow + 3 + $BodyCount) -Text '' -ForegroundColor $null -BackgroundColor $null
    Write-MenuPlainLine -Row ($headerRow + 4 + $BodyCount) -Text (' ' + $Footer) -ForegroundColor DarkGray -BackgroundColor $null

    [pscustomobject]@{
        BodyTop    = $headerRow + 3
        Bottom     = $headerRow + 5 + $BodyCount
        InnerWidth = $metrics.InnerWidth
    }
}

function New-MenuViewport {
    param(
        [string]$Header,
        [string]$Footer,
        [int]$BodyCount,
        [switch]$ClearScreen
    )

    if ($ClearScreen) { Clear-MenuScreen } else { Write-Host "" }
    $baseRow = [Console]::CursorTop

    $reserveRows = $BodyCount + (Get-MenuBanner).Count + 5
    for ($i = 0; $i -lt $reserveRows; $i++) {
        Write-Host ""
    }

    return Write-MenuChrome -BaseRow $baseRow -Header $Header -Footer $Footer -BodyCount $BodyCount
}

function Write-MenuLinePrefix {
    param([int]$Row)
    Set-MenuCursorPosition -Left 0 -Top $Row
    Write-MenuSegment -Text '  ' -ForegroundColor $null -BackgroundColor $null
}

function Write-MenuLineRemainder {
    param(
        [int]$InnerWidth,
        [int]$Written
    )
    if ($Written -lt $InnerWidth) {
        Write-MenuSegment -Text (' ' * ($InnerWidth - $Written)) -ForegroundColor $null -BackgroundColor $null
    }
}

function Write-MenuContentSegment {
    param(
        [string]$Text,
        [ref]$Written,
        [int]$InnerWidth,
        [string]$ForegroundColor,
        [string]$BackgroundColor
    )

    $remaining = $InnerWidth - $Written.Value
    if ($remaining -le 0) { return }
    $segment = Limit-MenuText -Text $Text -Width $remaining
    Write-MenuSegment -Text $segment -ForegroundColor $ForegroundColor -BackgroundColor $BackgroundColor
    $Written.Value += $segment.Length
}
