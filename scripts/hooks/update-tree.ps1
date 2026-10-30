<#
.SYNOPSIS
    Regenerate TREE.md from git ls-files
.DESCRIPTION
    Serves the pre-commit hook and direct manual runs. Annotates Markdown files
    with their front matter and scripts with their Header.
#>

#Requires -Version 7

$ErrorActionPreference = 'Stop'
$repoRoot   = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outputFile = Join-Path $repoRoot 'TREE.md'
. (Join-Path $repoRoot 'scripts\lib\script-output.ps1')
Import-Module (Join-Path $repoRoot 'scripts\lib\script-discovery.psm1') -Force

$WorkflowOutput = New-WorkflowOutput -Category 'tree'

Write-OutputText -Text "Repo: $repoRoot" -Role Muted
Write-OutputText -Text "TREE: $outputFile" -Role Muted

Write-WorkflowStep -Output $WorkflowOutput -Message 'Collect tracked files'
$files = git -C $repoRoot ls-files | Where-Object { $_ -ne '' }
Write-WorkflowMessage -Output $WorkflowOutput -Message "$($files.Count) tracked file(s)"

function Format-TreeSuffix {
    param([string]$Name, [string]$Description)

    if (-not $Name -and -not $Description) { return '' }

    if ($Description -and $Description.Length -gt 80) {
        $Description = $Description.Substring(0, 79).TrimEnd() + '…'
    }

    $namePart = if ($Name) { $Name } else { '' }
    $descPart = if ($Description) { $Description } else { '' }

    $parts = @($namePart, $descPart) | Where-Object { $_ }
    $tail = $parts -join ' '
    if (-not $tail) { return '' }
    return "  — $tail"
}

function Format-MarkdownSuffix {
    param([string]$AbsolutePath)

    $fm = Get-MarkdownFrontMatter -Path $AbsolutePath
    if (-not $fm) { return '' }
    return Format-TreeSuffix -Name $fm['name'] -Description $fm['description']
}

# A script Header plays the role of front matter: its synopsis names the
# file and its description describes it, so TREE.md reads both alike.
function Format-ScriptSuffix {
    param([string]$AbsolutePath)

    if (-not (Test-Path -LiteralPath $AbsolutePath -PathType Leaf)) { return '' }
    $header = Read-ScriptHeader -Path $AbsolutePath
    if ($header.Status -ne 'Described') { return '' }
    return Format-TreeSuffix -Name $header.Label -Description $header.Description
}

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
            $rel = if ($RelDir) { "$RelDir/$($e.Name)" } else { $e.Name }
            if ($e.Name -like '*.md') {
                $suffix = Format-MarkdownSuffix -AbsolutePath (Join-Path $repoRoot $rel)
            } elseif ($e.Name -like '*.ps1' -or $e.Name -like '*.psm1') {
                $suffix = Format-ScriptSuffix -AbsolutePath (Join-Path $repoRoot $rel)
            }
            $script:lines.Add("$Prefix$conn$($e.Name)$suffix")
        }
    }
}

Render-Node $root '' ''

Write-WorkflowStep -Output $WorkflowOutput -Message 'Write TREE.md'
$body = $lines -join "`n"

# No wall-clock timestamp on purpose: it would make every regeneration differ
# and collide across branches at merge time. Without it an unchanged tree
# regenerates a byte-identical file; git history already records when it
# changed.
$content = @"
# Arborescence — Deckle
_Généré depuis ``git ls-files`` — ne pas éditer à la main._

``````
$body
``````
"@

[System.IO.File]::WriteAllText($outputFile, $content, [System.Text.Encoding]::UTF8)
Write-WorkflowMessage -Output $WorkflowOutput -Message "TREE.md updated"
