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

    $lines = Get-Content -LiteralPath $Path -TotalCount 120 -Encoding UTF8 -ErrorAction SilentlyContinue
    if (-not $lines -or $lines.Count -lt 2) { return $null }
    if ($lines[0].Trim() -ne '---') { return $null }

    $fields = @{}
    $i = 1
    while ($i -lt $lines.Count) {
        $line = $lines[$i]
        if ($line.Trim() -eq '---') {
            if ($fields.Count -eq 0) { return $null }
            return $fields
        }
        if ($line -match '^([A-Za-z0-9_-]+)\s*:\s*(.*)$') {
            $key = $Matches[1].ToLowerInvariant()
            $value = $Matches[2].Trim()

            if ($value -match '^[|>][+-]?$') {
                $blockLines = [System.Collections.Generic.List[string]]::new()
                $i++
                while ($i -lt $lines.Count) {
                    $blockLine = $lines[$i]
                    if ($blockLine.Trim() -eq '---') {
                        $i--
                        break
                    }
                    if ($blockLine -match '^[A-Za-z0-9_-]+\s*:') {
                        $i--
                        break
                    }
                    $blockLines.Add($blockLine) | Out-Null
                    $i++
                }
                $value = Format-FrontmatterBlock -Lines $blockLines
            }

            # Strip surrounding quotes if present
            if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
                ($value.StartsWith("'") -and $value.EndsWith("'"))) {
                if ($value.Length -ge 2) {
                    $value = $value.Substring(1, $value.Length - 2)
                }
            }
            $fields[$key] = $value
        }
        $i++
    }
    # Closing --- never found
    return $null
}

function Format-FrontmatterBlock {
    param([System.Collections.Generic.List[string]]$Lines)

    if (-not $Lines -or $Lines.Count -eq 0) { return '' }

    $minIndent = $null
    foreach ($line in $Lines) {
        if ($line.Trim() -eq '') { continue }
        $indent = ([regex]::Match($line, '^\s*')).Value.Length
        if ($null -eq $minIndent -or $indent -lt $minIndent) {
            $minIndent = $indent
        }
    }

    if ($null -eq $minIndent) { return '' }

    $normalized = foreach ($line in $Lines) {
        if ($line.Length -ge $minIndent) {
            $line.Substring($minIndent)
        } else {
            $line.TrimStart()
        }
    }

    return (($normalized -join ' ') -replace '\s+', ' ').Trim()
}

function Format-MarkdownSuffix {
    param([string]$AbsolutePath)

    $fm = Get-Frontmatter -Path $AbsolutePath
    if (-not $fm) { return '' }

    $name = $fm['name']
    $type = $fm['type']
    $description = $fm['description']
    if (-not $name -and -not $type -and -not $description) { return '' }

    if ($description -and $description.Length -gt 80) {
        $description = $description.Substring(0, 79).TrimEnd() + '…'
    }

    $namePart = if ($name) { $name } else { '' }
    $typePart = if ($type) { "[$type]" } else { '' }
    $descPart = if ($description) { $description } else { '' }

    $parts = @($namePart, $typePart, $descPart) | Where-Object { $_ }
    $tail = $parts -join ' '
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

$body = $lines -join "`n"

# No wall-clock timestamp on purpose: it made every regeneration differ even
# when the tree was identical, so any two branches collided on this line at
# merge time. Without it, a content-only commit regenerates a byte-identical
# file (git add is a no-op) and the listing only changes when the file set
# does. git history already records when TREE.md was last touched.
$content = @"
# Arborescence — Deckle
_Généré depuis ``git ls-files`` — ne pas éditer à la main._

``````
$body
``````
"@

[System.IO.File]::WriteAllText($outputFile, $content, [System.Text.Encoding]::UTF8)
Write-Host "TREE.md mis à jour ($($files.Count) fichiers suivis)."
