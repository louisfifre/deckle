#!/usr/bin/env pwsh
#Requires -Version 7
<#
.SYNOPSIS
    Régénère TREE.md depuis git ls-files.
    À invoquer avant tout commit qui ajoute, supprime ou renomme un fichier tracé.
#>

$ErrorActionPreference = 'Stop'
$repoRoot   = (git rev-parse --show-toplevel).Trim()
$outputFile = Join-Path $repoRoot 'TREE.md'

# ── Collecte ──────────────────────────────────────────────────────────────

$files = git -C $repoRoot ls-files | Where-Object { $_ -ne '' }

# ── Construction de l'arbre ───────────────────────────────────────────────

function New-Node {
    @{
        Dirs  = [System.Collections.Specialized.OrderedDictionary]::new([System.StringComparer]::OrdinalIgnoreCase)
        Files = [System.Collections.Generic.List[string]]::new()
    }
}

function Add-Path {
    param($Node, [string[]]$Parts)
    if ($Parts.Count -eq 1) {
        $Node.Files.Add($Parts[0]) | Out-Null
        return
    }
    $dir = $Parts[0]
    if (-not $Node.Dirs.Contains($dir)) {
        $Node.Dirs[$dir] = New-Node
    }
    Add-Path $Node.Dirs[$dir] $Parts[1..($Parts.Count - 1)]
}

$root = New-Node
foreach ($f in $files) {
    Add-Path $root ($f -split '/')
}

# ── Rendu ─────────────────────────────────────────────────────────────────

$lines = [System.Collections.Generic.List[string]]::new()

function Render-Node {
    param($Node, [string]$Prefix)

    $sortedDirs  = $Node.Dirs.Keys | Sort-Object
    $sortedFiles = $Node.Files | Sort-Object

    $entries = @(
        foreach ($d in $sortedDirs)  { [pscustomobject]@{ Name = $d; IsDir = $true } }
        foreach ($f in $sortedFiles) { [pscustomobject]@{ Name = $f; IsDir = $false } }
    )

    for ($i = 0; $i -lt $entries.Count; $i++) {
        $e    = $entries[$i]
        $last = ($i -eq $entries.Count - 1)
        $conn   = if ($last) { '└── ' } else { '├── ' }
        $indent = if ($last) { '    ' } else { '│   ' }

        if ($e.IsDir) {
            $script:lines.Add("$Prefix$conn$($e.Name)/")
            Render-Node $Node.Dirs[$e.Name] "$Prefix$indent"
        } else {
            $script:lines.Add("$Prefix$conn$($e.Name)")
        }
    }
}

Render-Node $root ''

# ── Écriture ──────────────────────────────────────────────────────────────

$date = Get-Date -Format 'yyyy-MM-dd HH:mm'
$body = $lines -join "`n"

$content = @"
# Arborescence — Deckle
_Mise à jour : $date — source : ``git ls-files``_

``````
$body
``````
"@

[System.IO.File]::WriteAllText($outputFile, $content, [System.Text.Encoding]::UTF8)
Write-Host "TREE.md mis à jour ($($files.Count) fichiers suivis)."
