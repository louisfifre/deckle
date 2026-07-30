$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'
$LauncherDir = Join-Path $LibDir 'launcher'
$MenuDir = Join-Path $LibDir 'menu'
. (Join-Path $LauncherDir 'context.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

function Start-MenuSession {
    param([switch]$AlternateScreen)
    $script:StartedAlternateScreen = [bool]$AlternateScreen
}
function Stop-MenuSession { $script:StoppedMenuSession = $true }

Start-DeckleMenuSession
Stop-DeckleMenuSession
Assert-Equal $true $script:StartedAlternateScreen 'launcher starts the authoritative menu session'
Assert-Equal $true $script:StoppedMenuSession 'launcher stops the authoritative menu session'

function Select-YesNo {
    param(
        [string]$Question,
        [bool]$Default,
        [string]$ConfirmLabel,
        [string]$CancelLabel,
        [string[]]$ContextLines,
        [switch]$Destructive,
        [switch]$ClearScreen,
        [string]$BannerStyle
    )
    $script:ConfirmationClearsScreen = [bool]$ClearScreen
    $script:ConfirmationContext = @($ContextLines)
    return $true
}

$confirmed = Read-YesNo -Question 'Delete outputs?' -ConfirmLabel 'Delete outputs' -CancelLabel 'Keep files' -ContextLines @('Deletes generated files.') -Destructive
Assert-Equal $true $confirmed 'confirmation returns the selected outcome'
Assert-Equal $true $script:ConfirmationClearsScreen 'confirmation replaces the current menu surface'
Assert-Equal 'Deletes generated files.' $script:ConfirmationContext[0] 'confirmation keeps its consequence in the replacement surface'

Write-Host 'context.tests.ps1: PASS' -ForegroundColor Green
