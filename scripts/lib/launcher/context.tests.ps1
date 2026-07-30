$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'context.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

function Select-YesNo {
    param(
        [string]$Question,
        [bool]$Default,
        [string]$ConfirmLabel,
        [string]$CancelLabel,
        [switch]$Destructive,
        [switch]$ClearScreen,
        [string]$BannerStyle
    )
    $script:ConfirmationClearsScreen = [bool]$ClearScreen
    return $true
}

$confirmed = Read-YesNo -Question 'Delete outputs?' -ConfirmLabel 'Delete outputs' -CancelLabel 'Keep files' -Destructive
Assert-Equal $true $confirmed 'confirmation returns the selected outcome'
Assert-Equal $true $script:ConfirmationClearsScreen 'confirmation replaces the current menu surface'

Write-Host 'context.tests.ps1: PASS' -ForegroundColor Green
