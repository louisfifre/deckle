$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'context-inventory.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

Assert-Equal 'Automatic instructions' (Get-ContextDocumentKind 'src/Deckle.App/AGENTS.md') 'AGENTS instructions'
Assert-Equal 'Automatic instructions' (Get-ContextDocumentKind 'src/Deckle.App/CLAUDE.md') 'CLAUDE instructions'
Assert-Equal 'On-demand references' (Get-ContextDocumentKind 'CONTEXT-MAP.md') 'context map'
Assert-Equal 'On-demand references' (Get-ContextDocumentKind '.claude/skills/winui-app/SKILL.md') 'skill'
Assert-Equal 'On-demand references' (Get-ContextDocumentKind 'docs/adr/0001-decision.md') 'ADR'
Assert-Equal 'On-demand references' (Get-ContextDocumentKind 'scripts/README.md') 'readme'

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
