# Fixed launcher chrome with a native scrolling output region.

function Start-MenuActionConsole {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Header,
        [string]$HeaderCommands = 'Ctrl+C quit',
        [ValidateSet('Compact')]
        [string]$BannerStyle = 'Compact'
    )

    Wait-MenuViewportSize -BodyCount 1 -BannerStyle $BannerStyle
    Clear-MenuScreen

    $metrics = Get-MenuMetrics
    $banner = @(Get-MenuBanner -Style $BannerStyle)
    for ($index = 0; $index -lt $banner.Count; $index++) {
        Write-MenuPlainLine -Row $index -Text $banner[$index] -ForegroundColor Blue -BackgroundColor $null
    }

    $headerRow = $banner.Count + (Get-MenuBannerGap -Style $BannerStyle)
    for ($row = $banner.Count; $row -lt $headerRow; $row++) {
        Write-MenuPlainLine -Row $row -Text '' -ForegroundColor $null -BackgroundColor $null
    }

    $headerLine = Format-MenuHeaderLine -Breadcrumb $Header -Commands $HeaderCommands -Width $metrics.ContentWidth
    Write-MenuPlainLine -Row $headerRow -Text $headerLine -ForegroundColor DarkGray -BackgroundColor $null
    Write-MenuPlainLine -Row ($headerRow + 1) -Text (New-MenuRule -MaxWidth $metrics.ContentWidth) -ForegroundColor DarkGray -BackgroundColor $null
    Write-MenuPlainLine -Row ($headerRow + 2) -Text '' -ForegroundColor $null -BackgroundColor $null

    $scrollTop = $headerRow + 3
    $scrollBottom = $metrics.WindowHeight - 1
    if (-not [Console]::IsOutputRedirected) {
        $escape = [char]27
        Write-Ansi ("{0}[{1};{2}r" -f $escape, ($scrollTop + 1), ($scrollBottom + 1))
        try { [Console]::CursorVisible = $false } catch { }
        Set-MenuCursorPosition -Left 0 -Top $scrollTop
    }

    return [pscustomobject]@{
        HeaderRow    = $headerRow
        ScrollTop    = $scrollTop
        ScrollBottom = $scrollBottom
        IsInteractive = -not [Console]::IsOutputRedirected
    }
}

function Write-MenuActionOutput {
    param([AllowNull()]$InputObject)

    if ($InputObject -is [System.Management.Automation.InformationRecord] -and
        $InputObject.MessageData -is [System.Management.Automation.HostInformationMessage]) {
        $message = $InputObject.MessageData
        $arguments = @{
            Object    = [string]$message.Message
            NoNewline = [bool]$message.NoNewLine
        }
        try {
            if ($message.ForegroundColor -ne [Console]::ForegroundColor) {
                $arguments.ForegroundColor = $message.ForegroundColor
            }
            if ($message.BackgroundColor -ne [Console]::BackgroundColor) {
                $arguments.BackgroundColor = $message.BackgroundColor
            }
        } catch {
            # Hosts without console colors still receive the original text.
        }
        Write-Host @arguments
        return
    }

    if ($InputObject -is [System.Management.Automation.ErrorRecord]) {
        Write-Host ([string]$InputObject) -ForegroundColor Red
        return
    }
    if ($InputObject -is [System.Management.Automation.WarningRecord]) {
        Write-Host ([string]$InputObject) -ForegroundColor Yellow
        return
    }

    Write-Host ([string]$InputObject)
}

function Stop-MenuActionConsole {
    [CmdletBinding()]
    param([AllowNull()]$Console)

    if ($null -eq $Console -or -not $Console.IsInteractive) { return }

    $escape = [char]27
    Write-Ansi ("{0}[r{0}[0m{0}[?25h" -f $escape)
    try { [Console]::CursorVisible = $true } catch { }
}
