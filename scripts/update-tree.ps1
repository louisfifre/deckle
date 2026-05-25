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

# ── Frontmatter ───────────────────────────────────────────────────────────

function Get-Frontmatter {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return $null }

    $lines = Get-Content -LiteralPath $Path -TotalCount 30 -Encoding UTF8 -ErrorAction SilentlyContinue
    if (-not $lines -or $lines.Count -lt 2) { return $null }
    if ($lines[0].Trim() -ne '---') { return $null }

    $fields = @{}
    for ($i = 1; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line.Trim() -eq '---') {
            if ($fields.Count -eq 0) { return $null }
            return $fields
        }
        if ($line -match '^\s*([A-Za-z0-9_-]+)\s*:\s*(.*)$') {
            $key = $Matches[1].ToLowerInvariant()
            $value = $Matches[2].Trim()
            # Strip surrounding quotes if present
            if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
                ($value.StartsWith("'") -and $value.EndsWith("'"))) {
                if ($value.Length -ge 2) {
                    $value = $value.Substring(1, $value.Length - 2)
                }
            }
            $fields[$key] = $value
        }
    }
    # Closing --- never found
    return $null
}

function Format-MarkdownSuffix {
    param([string]$AbsolutePath)

    $fm = Get-Frontmatter -Path $AbsolutePath
    if (-not $fm) { return '' }

    $type = $fm['type']
    $description = $fm['description']
    if (-not $fm.ContainsKey('name') -and -not $type -and -not $description) { return '' }
    if (-not $type -and -not $description) { return '' }

    if ($description -and $description.Length -gt 80) {
        $description = $description.Substring(0, 79).TrimEnd() + '…'
    }

    $typePart = if ($type) { "[$type]" } else { '' }
    $descPart = if ($description) { $description } else { '' }

    $tail = ($typePart, $descPart | Where-Object { $_ }) -join ' '
    if (-not $tail) { return '' }
    return "  — $tail"
}

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
    param($Node, [string]$Prefix, [string]$RelDir)

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
            $childRel = if ($RelDir) { "$RelDir/$($e.Name)" } else { $e.Name }
            Render-Node $Node.Dirs[$e.Name] "$Prefix$indent" $childRel
        } else {
            $suffix = ''
            if ($e.Name -like '*.md') {
                $rel = if ($RelDir) { "$RelDir/$($e.Name)" } else { $e.Name }
                $abs = Join-Path $repoRoot $rel
                $suffix = Format-MarkdownSuffix -AbsolutePath $abs
            }
            $script:lines.Add("$Prefix$conn$($e.Name)$suffix")
        }
    }
}

Render-Node $root '' ''

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
