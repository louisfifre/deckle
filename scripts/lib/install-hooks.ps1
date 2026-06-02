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
$hooksDir  = Join-Path (git -C $repoRoot rev-parse --absolute-git-dir).Trim() 'hooks'
$sourceDir = Join-Path $repoRoot 'scripts' 'hooks'

$hookFiles = @('pre-commit')
foreach ($hookFile in $hookFiles) {
    $src = Join-Path $sourceDir $hookFile
    if (-not (Test-Path -LiteralPath $src)) {
        throw "Hook source missing: $src"
    }

    $dst = Join-Path $hooksDir $hookFile
    if (Test-Path $dst) {
        Write-Warning "Hook '$hookFile' existant sauvegardé en '$hookFile.bak'."
        Copy-Item $dst "$dst.bak" -Force
    }
    Copy-Item $src $dst -Force
    Write-Host "Hook '$hookFile' installé."
}

# Merge driver used by .gitattributes for TREE.md. `ours` keeps the local side
# so two branches never collide on the generated tree listing; the next commit
# that changes the file set regenerates it via the pre-commit hook. The driver
# definition lives in .git/config (not shared by clone), so it is set here.
git config merge.ours.driver true
Write-Host "Driver de merge 'ours' enregistré (TREE.md)."
