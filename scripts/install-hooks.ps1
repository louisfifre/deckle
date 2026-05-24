#!/usr/bin/env pwsh
#Requires -Version 7
<#
.SYNOPSIS
    Installe les hooks git locaux depuis scripts/hooks/.
    À lancer une seule fois après un clone (les hooks ne sont pas versionnés
    par git, seule leur source l'est).
#>

$ErrorActionPreference = 'Stop'
$repoRoot  = (git rev-parse --show-toplevel).Trim()
$hooksDir  = Join-Path (git rev-parse --git-dir).Trim() 'hooks'
$sourceDir = Join-Path $repoRoot 'scripts' 'hooks'

foreach ($src in Get-ChildItem $sourceDir) {
    $dst = Join-Path $hooksDir $src.Name
    if (Test-Path $dst) {
        Write-Warning "Hook '$($src.Name)' existant sauvegardé en '$($src.Name).bak'."
        Copy-Item $dst "$dst.bak" -Force
    }
    Copy-Item $src.FullName $dst -Force
    Write-Host "Hook '$($src.Name)' installé."
}
