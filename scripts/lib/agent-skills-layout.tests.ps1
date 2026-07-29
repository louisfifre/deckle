$ErrorActionPreference = 'Stop'

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

function Get-SkillNames([string]$Root) {
    @(
        Get-ChildItem -LiteralPath $Root -Directory |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'SKILL.md') } |
            Select-Object -ExpandProperty Name |
            Sort-Object
    )
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$canonicalRoot = Join-Path $repoRoot '.agents\skills'
$canonicalItem = Get-Item -LiteralPath $canonicalRoot

if ($canonicalItem.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) {
    throw '.agents/skills must be the physical, canonical project skill directory.'
}

$canonicalNames = Get-SkillNames -Root $canonicalRoot
if ($canonicalNames.Count -eq 0) {
    throw '.agents/skills must expose at least one project skill.'
}

foreach ($relativeFacade in @('.claude\skills', '.codex\skills')) {
    $facadePath = Join-Path $repoRoot $relativeFacade
    $facadeItem = Get-Item -LiteralPath $facadePath
    if ($facadeItem.LinkType -ne 'SymbolicLink') {
        throw "$relativeFacade must be a symbolic link. On Windows, enable Developer Mode, run 'git config core.symlinks true', then restore the path from Git."
    }

    $targetPath = if ([System.IO.Path]::IsPathFullyQualified($facadeItem.Target)) {
        [System.IO.Path]::GetFullPath($facadeItem.Target)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $facadeItem.FullName) $facadeItem.Target))
    }
    Assert-Equal $canonicalItem.FullName $targetPath "$relativeFacade target"

    $facadeNames = Get-SkillNames -Root $facadePath
    Assert-Equal ($canonicalNames -join "`n") ($facadeNames -join "`n") "$relativeFacade skill discovery"
}

Write-Host 'agent-skills-layout.tests.ps1: PASS' -ForegroundColor Green
