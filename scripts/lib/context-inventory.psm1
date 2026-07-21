Set-StrictMode -Version Latest

function Get-ContextDocumentKind {
    param([Parameter(Mandatory)][string]$RelativePath)

    $name = [System.IO.Path]::GetFileName($RelativePath)
    if ($name -in @('AGENTS.md', 'CLAUDE.md')) { return 'Automatic instructions' }
    return 'On-demand references'
}

function Measure-MarkdownSections {
    param([Parameter(Mandatory)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Lines)

    $sections = 0
    $fenceCharacter = $null
    foreach ($line in $Lines) {
        if ($line -match '^\s{0,3}(`{3,}|~{3,})') {
            $character = $Matches[1][0]
            if ($null -eq $fenceCharacter) {
                $fenceCharacter = $character
            } elseif ($character -eq $fenceCharacter) {
                $fenceCharacter = $null
            }
            continue
        }
        if ($null -eq $fenceCharacter -and $line -match '^\s{0,3}#{1,6}(?:\s+|$)') {
            $sections++
        }
    }
    return $sections
}

function Measure-ContextDocument {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $fullPath = Join-Path $RepoRoot $RelativePath
    $text = [System.IO.File]::ReadAllText($fullPath)
    $bytes = [System.IO.File]::ReadAllBytes($fullPath).Length
    $contentLines = [System.IO.File]::ReadAllLines($fullPath)

    [pscustomobject]@{
        Kind            = Get-ContextDocumentKind -RelativePath $RelativePath
        Path            = $RelativePath -replace '\\', '/'
        Bytes           = $bytes
        Characters      = $text.Length
        Lines           = $contentLines.Count
        Sections        = Measure-MarkdownSections -Lines $contentLines
        EstimatedTokens = [Math]::Ceiling($text.Length / 4.0)
    }
}

function Get-RecentlyAddedMarkdownPaths {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [ValidateRange(1, 3650)][int]$Days = 30
    )

    $baseline = & git -C $RepoRoot rev-list -1 --before="$Days days ago" HEAD
    if ($LASTEXITCODE -ne 0) { throw "Could not resolve the Git baseline from $Days days ago." }
    if (-not $baseline) { return @() }

    $paths = @(& git -C $RepoRoot diff --name-only --diff-filter=A $baseline HEAD -- '*.md')
    if ($LASTEXITCODE -ne 0) { throw "Could not list Markdown files added in the last $Days days." }
    return @($paths | ForEach-Object { $_ -replace '\\', '/' })
}

function Get-ContextInventory {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $paths = @(& git -C $RepoRoot ls-files -- '*.md')
    if ($LASTEXITCODE -ne 0) { throw "Could not list tracked Markdown files under $RepoRoot." }

    $recentPaths = @{}
    foreach ($days in @(1, 7, 30)) {
        $recentPaths[$days] = [System.Collections.Generic.HashSet[string]]::new(
            [string[]](Get-RecentlyAddedMarkdownPaths -RepoRoot $RepoRoot -Days $days),
            [System.StringComparer]::OrdinalIgnoreCase)
    }

    @($paths | ForEach-Object {
        $document = Measure-ContextDocument -RepoRoot $RepoRoot -RelativePath $_
        $document | Add-Member -NotePropertyName Added1Day -NotePropertyValue $recentPaths[1].Contains($document.Path)
        $document | Add-Member -NotePropertyName Added7Days -NotePropertyValue $recentPaths[7].Contains($document.Path)
        $document | Add-Member -NotePropertyName Added30Days -NotePropertyValue $recentPaths[30].Contains($document.Path)
        $document
    })
}

Export-ModuleMember -Function Get-ContextDocumentKind, Measure-MarkdownSections, Measure-ContextDocument, Get-RecentlyAddedMarkdownPaths, Get-ContextInventory
