$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
Import-Module (Join-Path $LibDir 'context-inventory.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

Assert-Equal 'Automatic instructions' (Get-ContextLoadingMode 'src/Deckle.App/AGENTS.md') 'AGENTS loading mode'
Assert-Equal 'Automatic instructions' (Get-ContextLoadingMode 'src/Deckle.App/CLAUDE.md') 'CLAUDE loading mode'
Assert-Equal 'On-demand references' (Get-ContextLoadingMode 'CONTEXT-MAP.md') 'context loading mode'
Assert-Equal 'AGENTS' (Get-ContextDocumentType 'src/Deckle.App/AGENTS.md') 'AGENTS type'
Assert-Equal 'CLAUDE' (Get-ContextDocumentType 'src/Deckle.App/CLAUDE.md') 'CLAUDE type'
Assert-Equal 'Context' (Get-ContextDocumentType 'CONTEXT-MAP.md') 'context type'
Assert-Equal 'Skill' (Get-ContextDocumentType '.agents/skills/winui-app/SKILL.md') 'skill type'
Assert-Equal 'Skill reference' (Get-ContextDocumentType '.agents/skills/winui-app/references/foundation-setup.md') 'skill reference type'
Assert-Equal 'ADR' (Get-ContextDocumentType 'docs/adr/0001-decision.md') 'ADR type'
Assert-Equal 'README' (Get-ContextDocumentType 'scripts/README.md') 'readme type'

$filtered = @(Select-ContextInventoryPaths -Paths @(
    'AGENTS.md',
    'docs/adr/0001-decision.md',
    'src/App/AGENTS.md',
    'src/App/README.md'
) -RelativePath 'src' -LoadingModes 'Automatic instructions')
Assert-Equal 1 $filtered.Count 'context paths are filtered before document reads'
Assert-Equal 'src/App/AGENTS.md' $filtered[0].Path 'path and loading-mode filters compose'

$references = @(Select-ContextInventoryPaths -Paths @(
    'docs/adr/0001-decision.md',
    'docs/README.md'
) -DocumentTypes 'ADR')
Assert-Equal 1 $references.Count 'document type filter'
Assert-Equal 'ADR' $references[0].DocumentType 'classified candidate is retained'

Assert-Equal 3 (Measure-MarkdownSections -Lines @(
    '# Title',
    '',
    '## Section',
    '```markdown',
    '# Example, not a section',
    '```',
    '   ### Nested section',
    'Paragraph with # inline text'
)) 'ATX headings outside fenced code blocks'

$dates = ConvertFrom-GitMarkdownLog -Lines @(
    '@@2026-07-22',
    '',
    'docs/current.md',
    '@@2026-07-20',
    'docs/current.md',
    'docs/older.md'
)
Assert-Equal '2026-07-22' $dates['docs/current.md'] 'latest modification wins'
Assert-Equal '2026-07-20' $dates['docs/older.md'] 'older modification retained'

$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ('deckle-context-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $path = Join-Path $fixture 'CONTEXT.md'
    [System.IO.File]::WriteAllText($path, "one`ntwo", [System.Text.UTF8Encoding]::new($false))
    $measured = Measure-ContextDocument -RepoRoot $fixture -RelativePath 'CONTEXT.md'
    Assert-Equal 2 $measured.Lines 'line count'
    Assert-Equal 0 $measured.Sections 'section count'
    Assert-Equal 2 $measured.EstimatedTokens 'estimated tokens'
    Assert-Equal 7 $measured.Bytes 'UTF-8 bytes'

    & git -C $fixture init --quiet
    & git -C $fixture add -- CONTEXT.md
    $uncommittedInventory = @(Get-ContextInventory -RepoRoot $fixture)
    Assert-Equal 1 $uncommittedInventory.Count 'a repository without HEAD still has a context inventory'
    Assert-Equal $null $uncommittedInventory[0].Modified 'missing Git history has no invented modification date'
} finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}

Write-Host 'context-inventory.tests.ps1: PASS' -ForegroundColor Green
