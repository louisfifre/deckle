# menu.ps1 - Entry of the repository's terminal menu.
#
# This file is the only code Windows PowerShell 5.1 ever reads, so it stays
# ASCII, uses no PowerShell 7 syntax and declares no parameters: it hands
# every argument to lib/launcher/launcher.ps1 under PowerShell 7, or says
# that PowerShell 7 must be installed.

$ErrorActionPreference = 'Stop'
$launcher = Join-Path $PSScriptRoot 'lib\launcher\launcher.ps1'

if ($PSVersionTable.PSVersion.Major -ge 7) {
    & $launcher @args
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    return
}

$pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
if ($null -eq $pwsh) {
    Write-Host 'Deckle Scripts runs under PowerShell 7, which this machine does not have.'
    Write-Host 'Install it with: winget install Microsoft.PowerShell'
    exit 1
}
& $pwsh.Source -NoProfile -File $launcher @args
exit $LASTEXITCODE
