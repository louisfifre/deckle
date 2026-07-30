$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$MenuDir = Join-Path $ScriptsDir 'lib\menu'
. (Join-Path $MenuDir 'chrome.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

Assert-Equal 2 @(Get-MenuBanner -Style Compact).Count 'compact banner line count'
Assert-Equal $false ([char]::IsWhiteSpace((Get-MenuBanner -Style Compact)[0][0])) 'menu chrome starts at the terminal edge'
Assert-Equal $true ((Get-MenuBanner -Style Compact)[1].EndsWith('SCRIPTS')) 'compact banner places scripts at the lower right'
Assert-Equal 1 (Get-MenuBannerGap -Style Compact) 'compact banner breathes before the menu header'
Assert-Equal 2 $script:MenuRowInset 'action rows keep their hierarchy without shifting the chrome'
Assert-Equal 12 (New-MenuRule -MaxWidth 12).Length 'rule uses requested width'
Assert-Equal 11 (New-MenuRule -MaxWidth 11 -Style Section).Length 'section rule uses requested width'
Assert-Equal 15 (Get-MenuBodyCapacity -BannerStyle Compact -WindowHeight 24) 'compact body keeps one row between the header rule and first section'
Assert-Equal 0 (Get-MenuBodyCapacity -BannerStyle Compact -WindowHeight 6) 'undersized terminal has no body capacity'
Assert-Equal 14 $script:MenuCategoryWidth 'launcher category column is shared across menus'
Assert-Equal 2 $script:MenuActionColumnCount 'interactive menus share two action columns'
Assert-Equal 'Red' (Get-MenuRoleColor -Role danger).Foreground 'destructive confirmation is red'
Assert-Equal 'DarkRed' (Get-MenuRoleColor -Role danger -Selected).Background 'selected destructive confirmation stays red'

$fullHeader = Format-MenuHeaderLine -Breadcrumb 'Deckle > Worktrees' -Commands '↑↓←→ move   Enter select   Esc back' -Width 74
Assert-Equal 74 $fullHeader.Length 'header commands align to the shared content edge'
Assert-Equal $true $fullHeader.EndsWith('↑↓←→ move   Enter select   Esc back') 'header keeps navigation commands visible'
$middleCompressed = Compress-MenuBreadcrumb -Breadcrumb 'Deckle > Maintenance > Repository statistics > Overview' -Width 47
Assert-Equal 'Deckle > … > Repository statistics > Overview' $middleCompressed 'breadcrumb removes its oldest middle level first'
$moreCompressed = Compress-MenuBreadcrumb -Breadcrumb 'Deckle > Maintenance > Repository statistics > Overview' -Width 39
Assert-Equal 'Deckle > … > Overview' $moreCompressed 'breadcrumb removes more middle levels before touching either end'
$compactHeader = Format-MenuHeaderLine -Breadcrumb 'Deckle > Maintenance > Custom > Measures' -Commands '↑↓←→ move   Enter run   Esc back' -Width 40
Assert-Equal $true $compactHeader.StartsWith('Deck…') 'long breadcrumbs truncate before navigation commands'
Assert-Equal $true $compactHeader.EndsWith('↑↓←→ move   Enter run   Esc back') 'compact header preserves navigation commands'

$fits = [pscustomobject]@{ ContentWidth = 74; WindowHeight = 24 }
$tooNarrow = [pscustomobject]@{ ContentWidth = 39; WindowHeight = 24 }
$tooShort = [pscustomobject]@{ ContentWidth = 74; WindowHeight = 19 }
Assert-Equal $true (Test-MenuViewportFits -BodyCount 13 -BannerStyle Compact -Metrics $fits) 'main menu fits supported terminal'
Assert-Equal $false (Test-MenuViewportFits -BodyCount 13 -BannerStyle Compact -Metrics $tooNarrow) 'minimum width is enforced'
Assert-Equal $false (Test-MenuViewportFits -BodyCount 13 -BannerStyle Compact -Metrics $tooShort) 'minimum height is enforced'

Write-Host 'chrome.tests.ps1: PASS' -ForegroundColor Green
