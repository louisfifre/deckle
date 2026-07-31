$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'

function Invoke-TestGit {
    param([string]$Root, [string[]]$Arguments)
    & git -C $Root @Arguments *> $null
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed" }
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-hooks-$([guid]::NewGuid())"
$repository = Join-Path $testRoot 'repository'
$worktree = Join-Path $testRoot 'worktree'

try {
    $null = New-Item -ItemType Directory -Path (Join-Path $repository 'scripts\commands') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $repository 'scripts\lib') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $repository 'scripts\hooks') -Force
    Copy-Item -LiteralPath (Join-Path $CommandDir 'install-hooks.ps1') -Destination (Join-Path $repository 'scripts\commands\install-hooks.ps1')
    Copy-Item -LiteralPath (Join-Path $LibDir 'action-summary.ps1') -Destination (Join-Path $repository 'scripts\lib\action-summary.ps1')
    Copy-Item -LiteralPath (Join-Path $LibDir 'script-output.ps1') -Destination (Join-Path $repository 'scripts\lib\script-output.ps1')
    Copy-Item -LiteralPath (Join-Path $ScriptsDir 'hooks\pre-commit') -Destination (Join-Path $repository 'scripts\hooks\pre-commit')

    Invoke-TestGit -Root $repository -Arguments @('init', '-q')
    Invoke-TestGit -Root $repository -Arguments @('config', 'user.name', 'Deckle Tests')
    Invoke-TestGit -Root $repository -Arguments @('config', 'user.email', 'deckle-tests@example.invalid')
    Invoke-TestGit -Root $repository -Arguments @('add', '.')
    Invoke-TestGit -Root $repository -Arguments @('commit', '-m', 'test: seed hook fixture')
    Invoke-TestGit -Root $repository -Arguments @('worktree', 'add', '-q', '-b', 'test-worktree', $worktree)

    Push-Location $worktree
    try {
        & (Join-Path $worktree 'scripts\commands\install-hooks.ps1') *> $null
    } finally {
        Pop-Location
    }

    $commonHook = Join-Path $repository '.git\hooks\pre-commit'
    if (-not (Test-Path -LiteralPath $commonHook)) { throw 'The hook was not installed in the shared Git directory.' }
    $worktreeHookDir = Join-Path $repository '.git\worktrees\worktree\hooks'
    if (Test-Path -LiteralPath $worktreeHookDir) { throw 'A worktree-local hooks directory must not be created.' }

    Write-Host 'install-hooks.tests.ps1: PASS' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
