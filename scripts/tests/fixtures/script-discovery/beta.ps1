<#
.SYNOPSIS
    Beta build
.DESCRIPTION
    Builds the beta target in the chosen configuration.
.NOTES
    section: Build
    order: 20
#>

[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$Target,
    [switch]$Verbose2
)

Write-Output "beta $Configuration"
