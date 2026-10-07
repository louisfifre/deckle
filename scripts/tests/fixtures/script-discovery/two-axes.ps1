<#
.SYNOPSIS
    Two axes
#>

param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Debug',
    [ValidateSet('Fast', 'Full')][string]$Mode
)

Write-Output "$Configuration $Mode"
