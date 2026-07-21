$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'context-inventory.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

Assert-Equal 'Agent instructions' (Get-ContextDocumentKind 'src/Deckle.App/AGENTS.md') 'agent instructions'
Assert-Equal 'Context' (Get-ContextDocumentKind 'CONTEXT-MAP.md') 'context map'
Assert-Equal 'ADR' (Get-ContextDocumentKind 'docs/adr/0001-decision.md') 'ADR'
Assert-Equal 'Prompt' (Get-ContextDocumentKind 'benchmark/asr/prompts/judge.md') 'prompt'
Assert-Equal 'README' (Get-ContextDocumentKind 'scripts/README.md') 'readme'
Assert-Equal 'Skill reference' (Get-ContextDocumentKind '.claude/skills/winui-app/references/layout.md') 'skill reference'
Assert-Equal 'Generated index' (Get-ContextDocumentKind 'TREE.md') 'generated index'

$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ('deckle-context-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $path = Join-Path $fixture 'CONTEXT.md'
    [System.IO.File]::WriteAllText($path, "one`ntwo", [System.Text.UTF8Encoding]::new($false))
    $measured = Measure-ContextDocument -RepoRoot $fixture -RelativePath 'CONTEXT.md'
    Assert-Equal 2 $measured.Lines 'line count'
    Assert-Equal 2 $measured.EstimatedTokens 'estimated tokens'
    Assert-Equal 7 $measured.Bytes 'UTF-8 bytes'
} finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}

Write-Host 'context-inventory.tests.ps1: PASS' -ForegroundColor Green
