$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$MenuDir = Join-Path $ScriptsDir 'lib\menu'
. (Join-Path $MenuDir 'chrome.ps1')
. (Join-Path $MenuDir 'action-console.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$script:ActionConsoleLines = [System.Collections.Generic.List[object]]::new()
$script:ActionConsoleAnsi = [System.Collections.Generic.List[string]]::new()
function Get-MenuMetrics {
    return [pscustomobject]@{ TerminalWidth = 79; WindowHeight = 24; InnerWidth = 75; ContentWidth = 74; IsCompact = $false }
}
function Wait-MenuViewportSize { param([int]$BodyCount, [string]$BannerStyle) }
function Clear-MenuScreen { }
function Write-MenuPlainLine {
    param([int]$Row, [string]$Text, [string]$ForegroundColor, [string]$BackgroundColor)
    $script:ActionConsoleLines.Add([pscustomobject]@{ Row = $Row; Text = $Text; ForegroundColor = $ForegroundColor })
}
function Write-Ansi { param([string]$Sequence) $script:ActionConsoleAnsi.Add($Sequence) }
function Set-MenuCursorPosition { param([int]$Left, [int]$Top) }

$view = Start-MenuActionConsole -Header 'Deckle > Build Debug · Running…'
$header = @($script:ActionConsoleLines | Where-Object Row -eq $view.HeaderRow)[0]
$rule = @($script:ActionConsoleLines | Where-Object Row -eq ($view.HeaderRow + 1))[0]
Assert-Equal $true $header.Text.StartsWith('Deckle > Build Debug · Running…') 'running state stays in the breadcrumb'
Assert-Equal $true $header.Text.EndsWith('Ctrl+C quit') 'only the available action hint remains visible'
Assert-Equal 74 $rule.Text.Length 'separator keeps the shared content width'
Assert-Equal ($view.HeaderRow + 3) $view.ScrollTop 'native output starts below the fixed separator and breathing row'

$escape = [char]27
if ($view.IsInteractive) {
    Assert-Equal ("{0}[{1};24r" -f $escape, ($view.ScrollTop + 1)) $script:ActionConsoleAnsi[0] 'scrolling is constrained below the fixed chrome'
}

$script:ActionConsoleAnsi.Clear()
Stop-MenuActionConsole -Console ([pscustomobject]@{ IsInteractive = $true })
Assert-Equal ("{0}[r{0}[0m{0}[?25h" -f $escape) $script:ActionConsoleAnsi[0] 'closing the action restores full-screen scrolling and the cursor'

$hostLine = @(& { Write-Host 'colored' -ForegroundColor Green } 6>&1)[0]
$forwarded = @(Write-MenuActionOutput -InputObject $hostLine 6>&1)
Assert-Equal ([ConsoleColor]::Green) $forwarded[0].MessageData.ForegroundColor 'PowerShell host colors pass through unchanged'

Write-Host 'action-console.tests.ps1: PASS' -ForegroundColor Green
