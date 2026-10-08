<#
.SYNOPSIS
    Array set
.DESCRIPTION
    Takes a multi-value scope filter.
#>

param(
    [ValidateSet('A', 'B')][string[]]$Scope = @('A', 'B')
)

Write-Output ($Scope -join ',')
