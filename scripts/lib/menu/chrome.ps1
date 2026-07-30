# Shared menu chrome and rendering primitives.
$script:MenuPreferredContentWidth = 74
$script:MenuMinimumContentWidth = 40
$script:MenuCategoryWidth = 14
$script:MenuRowInset = 2
$script:MenuGridGap = 3
$script:MenuActionColumnCount = 2
$script:MenuHeaderGap = 3

function Get-MenuMetrics {
    try {
        $windowWidth = [Console]::WindowWidth
    } catch {
        $windowWidth = $script:MenuPreferredContentWidth + 4
    }
    try {
        $windowHeight = [Console]::WindowHeight
    } catch {
        $windowHeight = 24
    }

    # Leave the last terminal column untouched: writing into it can trigger an
    # automatic line wrap in some hosts. Never pretend the terminal is wider
    # than it really is; narrow hosts must truncate instead of wrapping.
    $terminalWidth = [Math]::Max(1, $windowWidth - 1)
    $innerWidth = [Math]::Max(1, $terminalWidth - 4)
    $contentWidth = [Math]::Min($script:MenuPreferredContentWidth, $innerWidth)
    [pscustomobject]@{
        TerminalWidth = $terminalWidth
        WindowHeight  = $windowHeight
        InnerWidth    = $innerWidth
        ContentWidth  = $contentWidth
        IsCompact     = $contentWidth -lt $script:MenuMinimumContentWidth
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

    if ($Selected -and $Role -eq 'danger') {
        return @{ Foreground = 'White'; Background = 'DarkRed' }
    }
    if ($Selected) {
        return @{ Foreground = 'Black'; Background = 'Gray' }
    }

    switch ($Role) {
        'folder' { return @{ Foreground = 'DarkYellow'; Background = $null } }
        'back'   { return @{ Foreground = 'DarkGray';   Background = $null } }
        'quit'   { return @{ Foreground = 'Red';        Background = $null } }
        'danger' { return @{ Foreground = 'Red';        Background = $null } }
        default  { return @{ Foreground = $null;        Background = $null } }
    }
}

function New-MenuRule {
    param(
        [int]$MaxWidth,
        [ValidateSet('Solid', 'Section')]
        [string]$Style = 'Solid'
    )

    $width = [Math]::Max(0, $MaxWidth)
    if ($Style -eq 'Section') {
        return (('- ' * [Math]::Ceiling($width / 2.0)).Substring(0, $width))
    }
    return ([string][char]0x2500) * $width
}

function Get-MenuBanner {
    param(
        [ValidateSet('Compact')]
        [string]$Style = 'Compact'
    )

    return @(
        '█▀▄ █▀▀ █▀▀ █▄▀ █   █▀▀'
        '█▄▀ █▄▄ █▄▄ █ █ █▄▄ █▄▄  SCRIPTS'
    )
}

function Get-MenuBannerGap {
    param(
        [ValidateSet('Compact')]
        [string]$Style = 'Compact'
    )

    return 1
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

function Format-MenuHeaderLine {
    param(
        [AllowNull()][string]$Breadcrumb,
        [AllowNull()][string]$Commands,
        [int]$Width
    )

    if ($Width -le 0) { return '' }

    $right = Limit-MenuText -Text $Commands -Width $Width
    if (-not $right) { return Limit-MenuText -Text $Breadcrumb -Width $Width }

    $leftWidth = $Width - $right.Length - $script:MenuHeaderGap
    if ($leftWidth -le 0) { return $right.PadLeft($Width) }

    $left = Limit-MenuText -Text $Breadcrumb -Width $leftWidth
    $gap = ' ' * ($Width - $left.Length - $right.Length)
    return $left + $gap + $right
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
        [string]$HeaderCommands,
        [string]$Footer,
        [int]$BodyCount,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    $metrics = Get-MenuMetrics
    $banner = @(Get-MenuBanner -Style $BannerStyle)
    for ($i = 0; $i -lt $banner.Count; $i++) {
        Write-MenuPlainLine -Row ($BaseRow + $i) -Text $banner[$i] -ForegroundColor Blue -BackgroundColor $null
    }

    $bannerGap = Get-MenuBannerGap -Style $BannerStyle
    for ($i = 0; $i -lt $bannerGap; $i++) {
        Write-MenuPlainLine -Row ($BaseRow + $banner.Count + $i) -Text '' -ForegroundColor $null -BackgroundColor $null
    }

    $headerRow = $BaseRow + $banner.Count + $bannerGap
    $header = Format-MenuHeaderLine -Breadcrumb $Header -Commands $HeaderCommands -Width $metrics.ContentWidth
    $footer = Limit-MenuText -Text $Footer -Width $metrics.ContentWidth
    Write-MenuPlainLine -Row $headerRow -Text $header -ForegroundColor DarkGray -BackgroundColor $null
    Write-MenuPlainLine -Row ($headerRow + 1) -Text (New-MenuRule -MaxWidth $metrics.ContentWidth) -ForegroundColor DarkGray -BackgroundColor $null
    Write-MenuPlainLine -Row ($headerRow + 2 + $BodyCount) -Text '' -ForegroundColor $null -BackgroundColor $null
    Write-MenuPlainLine -Row ($headerRow + 3 + $BodyCount) -Text $footer -ForegroundColor DarkGray -BackgroundColor $null

    [pscustomobject]@{
        BodyTop    = $headerRow + 2
        Bottom     = $headerRow + 4 + $BodyCount
        InnerWidth = $metrics.InnerWidth
        ContentWidth = $metrics.ContentWidth
    }
}

function Get-MenuBodyCapacity {
    param(
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact',
        [int]$WindowHeight
    )

    if ($WindowHeight -le 0) {
        try {
            $WindowHeight = [Console]::WindowHeight
        } catch {
            $WindowHeight = 24
        }
    }

    # Leave one physical row unused so reserving the viewport never scrolls
    # the alternate screen.
    $chromeHeight = @(Get-MenuBanner -Style $BannerStyle).Count + (Get-MenuBannerGap -Style $BannerStyle) + 5
    return [Math]::Max(0, $WindowHeight - $chromeHeight)
}

function Test-MenuViewportFits {
    param(
        [Parameter(Mandatory)][int]$BodyCount,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact',
        $Metrics = (Get-MenuMetrics)
    )

    return $Metrics.ContentWidth -ge $script:MenuMinimumContentWidth -and
        $BodyCount -le (Get-MenuBodyCapacity -BannerStyle $BannerStyle -WindowHeight $Metrics.WindowHeight)
}

function Wait-MenuViewportSize {
    param(
        [Parameter(Mandatory)][int]$BodyCount,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) { return }

    $requiredHeight = $BodyCount + @(Get-MenuBanner -Style $BannerStyle).Count + (Get-MenuBannerGap -Style $BannerStyle) + 5
    while (-not (Test-MenuViewportFits -BodyCount $BodyCount -BannerStyle $BannerStyle)) {
        Clear-MenuScreen
        $metrics = Get-MenuMetrics
        $banner = @(Get-MenuBanner -Style $BannerStyle)
        $lines = @($banner) + @(
            ''
            "Resize the terminal to at least $($script:MenuMinimumContentWidth + 5) x $requiredHeight."
            "Current size: $([Console]::WindowWidth) x $($metrics.WindowHeight)."
            'Press any key after resizing.'
        )
        $visibleCount = [Math]::Min($lines.Count, $metrics.WindowHeight)
        for ($index = 0; $index -lt $visibleCount; $index++) {
            Write-MenuPlainLine -Row $index -Text $lines[$index] -ForegroundColor $(if ($index -lt $banner.Count) { 'Blue' } else { 'DarkGray' }) -BackgroundColor $null
        }
        [Console]::ReadKey($true) | Out-Null
    }
}

function New-MenuViewport {
    param(
        [string]$Header,
        [string]$HeaderCommands,
        [string]$Footer,
        [int]$BodyCount,
        [switch]$ClearScreen,
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    Wait-MenuViewportSize -BodyCount $BodyCount -BannerStyle $BannerStyle
    if ($ClearScreen) { Clear-MenuScreen } else { Write-Host "" }
    $baseRow = [Console]::CursorTop

    $reserveRows = $BodyCount + @(Get-MenuBanner -Style $BannerStyle).Count + (Get-MenuBannerGap -Style $BannerStyle) + 4
    for ($i = 0; $i -lt $reserveRows; $i++) {
        Write-Host ""
    }

    return Write-MenuChrome -BaseRow $baseRow -Header $Header -HeaderCommands $HeaderCommands -Footer $Footer -BodyCount $BodyCount -BannerStyle $BannerStyle
}

function Write-MenuLinePrefix {
    param([int]$Row)
    Set-MenuCursorPosition -Left 0 -Top $Row
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
