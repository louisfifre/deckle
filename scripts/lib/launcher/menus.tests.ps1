$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'context.ps1')
. (Join-Path $PSScriptRoot 'menus.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

function Select-Grid {
    param($Header, $Rows, $Footer, $StartSel, [switch]$ClearScreen, $BannerStyle, $ResultTitle, $ResultLines)
    $script:LastSubmenuBannerStyle = $BannerStyle
    return $script:NextMenuSelection
}
function Invoke-WorktreeScript { }

$rows = @(@{ Prefix = 'Section'; Items = @( @{ Label = 'Action'; Value = 'action' } ) })
$script:DeckleMenuIsCompact = $false
$script:NextMenuSelection = '__back__'
Show-Submenu -Header 'Deckle > Test' -Rows $rows | Out-Null
Assert-Equal 'Full' $script:LastSubmenuBannerStyle 'submenus use the full opening state'

Use-DeckleCompactMenu
Show-Submenu -Header 'Deckle > Test' -Rows $rows | Out-Null
Assert-Equal 'Compact' $script:LastSubmenuBannerStyle 'submenus retain the session compact state'

$script:DeckleMenuIsCompact = $false
$script:NextMenuSelection = 'readme-stats'
Show-ProjectMenu
Assert-Equal 'Compact' (Get-DeckleMenuBannerStyle) 'selecting a real command compacts the session before it runs'

Write-Host 'menus.tests.ps1: PASS' -ForegroundColor Green
