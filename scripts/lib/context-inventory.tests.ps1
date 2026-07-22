$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'context-inventory.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

Assert-Equal 'Automatic instructions' (Get-ContextLoadingMode 'src/Deckle.App/AGENTS.md') 'AGENTS loading mode'
Assert-Equal 'Automatic instructions' (Get-ContextLoadingMode 'src/Deckle.App/CLAUDE.md') 'CLAUDE loading mode'
Assert-Equal 'On-demand references' (Get-ContextLoadingMode 'CONTEXT-MAP.md') 'context loading mode'
Assert-Equal 'AGENTS' (Get-ContextDocumentType 'src/Deckle.App/AGENTS.md') 'AGENTS type'
Assert-Equal 'CLAUDE' (Get-ContextDocumentType 'src/Deckle.App/CLAUDE.md') 'CLAUDE type'
Assert-Equal 'Context' (Get-ContextDocumentType 'CONTEXT-MAP.md') 'context type'
Assert-Equal 'Skill' (Get-ContextDocumentType '.claude/skills/winui-app/SKILL.md') 'skill type'
Assert-Equal 'ADR' (Get-ContextDocumentType 'docs/adr/0001-decision.md') 'ADR type'
Assert-Equal 'README' (Get-ContextDocumentType 'scripts/README.md') 'readme type'

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
} finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}

Write-Host 'context-inventory.tests.ps1: PASS' -ForegroundColor Green
