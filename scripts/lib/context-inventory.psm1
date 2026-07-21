Set-StrictMode -Version Latest

function Get-ContextDocumentKind {
    param([Parameter(Mandatory)][string]$RelativePath)

    $name = [System.IO.Path]::GetFileName($RelativePath)
    if ($name -in @('AGENTS.md', 'CLAUDE.md')) { return 'Agent instructions' }
    if ($name -in @('CONTEXT.md', 'CONTEXT-MAP.md')) { return 'Context' }
    if ($name -eq 'JOURNAL.md') { return 'Journal' }
    if ($name -eq 'SKILL.md') { return 'Skill' }
    if ($RelativePath -match '(^|[\\/])\.claude[\\/]skills[\\/]') { return 'Skill reference' }
    if ($RelativePath -match '(^|[\\/])docs[\\/]adr[\\/]') { return 'ADR' }
    if ($RelativePath -match '(^|[\\/])docs[\\/]research[\\/]') { return 'Research' }
    if ($RelativePath -match '(^|[\\/])prompts?[\\/]') { return 'Prompt' }
    if ($name -eq 'README.md') { return 'README' }
    if ($name -eq 'CHANGELOG.md') { return 'Changelog' }
    if ($name -eq 'TREE.md') { return 'Generated index' }
    return 'Project document'
}

function Measure-ContextDocument {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $fullPath = Join-Path $RepoRoot $RelativePath
    $text = [System.IO.File]::ReadAllText($fullPath)
    $bytes = [System.IO.File]::ReadAllBytes($fullPath).Length
    $lines = [System.IO.File]::ReadAllLines($fullPath).Count

    [pscustomobject]@{
        Kind            = Get-ContextDocumentKind -RelativePath $RelativePath
        Path            = $RelativePath -replace '\\', '/'
        Bytes           = $bytes
        Characters      = $text.Length
        Lines           = $lines
        EstimatedTokens = [Math]::Ceiling($text.Length / 4.0)
    }
}

function Get-ContextInventory {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $paths = @(& git -C $RepoRoot ls-files -- '*.md')
    if ($LASTEXITCODE -ne 0) { throw "Could not list tracked Markdown files under $RepoRoot." }

    @($paths | ForEach-Object {
        Measure-ContextDocument -RepoRoot $RepoRoot -RelativePath $_
    })
}

Export-ModuleMember -Function Get-ContextDocumentKind, Measure-ContextDocument, Get-ContextInventory
