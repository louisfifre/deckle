<#
.SYNOPSIS
    Gamma package
.DESCRIPTION
    Packs or ships the gamma package.
.NOTES
    section: Build
    order: 10
    action: Pack |
    action: Ship | -Publish | danger | Ships the package to the public feed.
#>

[CmdletBinding()]
param([switch]$Publish)

Write-Output "gamma $Publish"
