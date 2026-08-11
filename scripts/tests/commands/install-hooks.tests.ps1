$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'

function Invoke-TestGit {
    param([string]$Root, [string[]]$Arguments)
    $output = & git -C $Root @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)" }
}

function Assert-TestGitRejected {
    param([string]$Root, [string[]]$Arguments, [string]$Because)
    & git -C $Root @Arguments *> $null
    if ($LASTEXITCODE -eq 0) { throw "Git command was expected to fail: $Because" }
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-hooks-$([guid]::NewGuid())"
$repository = Join-Path $testRoot 'repository'
$worktree = Join-Path $testRoot 'worktree'
$otherRepository = Join-Path $testRoot 'other-repository'
$globalHookDirectory = Join-Path $testRoot 'global hooks'
$previousGlobalConfig = $env:GIT_CONFIG_GLOBAL
$env:GIT_CONFIG_GLOBAL = Join-Path $testRoot 'global.gitconfig'

try {
    $null = New-Item -ItemType Directory -Path (Join-Path $repository 'scripts\commands') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $repository 'scripts\lib') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $repository 'scripts\hooks') -Force
    Copy-Item -LiteralPath (Join-Path $CommandDir 'install-hooks.ps1') -Destination (Join-Path $repository 'scripts\commands\install-hooks.ps1')
    Copy-Item -LiteralPath (Join-Path $LibDir 'action-summary.ps1') -Destination (Join-Path $repository 'scripts\lib\action-summary.ps1')
    Copy-Item -LiteralPath (Join-Path $LibDir 'script-output.ps1') -Destination (Join-Path $repository 'scripts\lib\script-output.ps1')
    Copy-Item -LiteralPath (Join-Path $ScriptsDir 'hooks\pre-commit') -Destination (Join-Path $repository 'scripts\hooks\pre-commit')
    Copy-Item -LiteralPath (Join-Path $ScriptsDir 'hooks\update-tree.ps1') -Destination (Join-Path $repository 'scripts\hooks\update-tree.ps1')
    Copy-Item -LiteralPath (Join-Path $ScriptsDir 'hooks\validate-commit-attribution.ps1') -Destination (Join-Path $repository 'scripts\hooks\validate-commit-attribution.ps1')

    Invoke-TestGit -Root $repository -Arguments @('init', '-q')
    Invoke-TestGit -Root $repository -Arguments @('config', 'user.name', 'Louis')
    Invoke-TestGit -Root $repository -Arguments @('config', 'user.email', 'git@louisfifre.com')
    Invoke-TestGit -Root $repository -Arguments @('add', '.')
    Invoke-TestGit -Root $repository -Arguments @('commit', '-m', 'test: seed hook fixture')
    Invoke-TestGit -Root $repository -Arguments @('worktree', 'add', '-q', '-b', 'test-worktree', $worktree)

    Push-Location $testRoot
    try {
        & (Join-Path $worktree 'scripts\commands\install-hooks.ps1') -GlobalHookDirectory $globalHookDirectory *> $null
    } finally {
        Pop-Location
    }

    $commonHook = Join-Path $repository '.git\hooks\pre-commit'
    if (-not (Test-Path -LiteralPath $commonHook)) { throw 'The hook was not installed in the shared Git directory.' }
    $worktreeHookDir = Join-Path $repository '.git\worktrees\worktree\hooks'
    if (Test-Path -LiteralPath $worktreeHookDir) { throw 'A worktree-local hooks directory must not be created.' }
    $mergeDriver = (& git -C $repository config --get merge.ours.driver).Trim()
    if ($mergeDriver -cne 'true') { throw 'The TREE.md merge driver was not registered in the repository.' }

    $globalHook = Join-Path $globalHookDirectory 'validate-commit-attribution.ps1'
    if (-not (Test-Path -LiteralPath $globalHook)) { throw 'The global commit-attribution guard was not installed.' }
    $globalEvent = (& git config --global --get hook.deckle-commit-attribution.event).Trim()
    if ($globalEvent -cne 'commit-msg') { throw 'The configured guard must run for commit-msg.' }
    $globalEnabled = (& git config --global --get hook.deckle-commit-attribution.enabled).Trim()
    if ($globalEnabled -cne 'true') { throw 'The configured guard must be enabled.' }

    Set-Content -LiteralPath (Join-Path $worktree 'guard-fixture.txt') -Value 'guard fixture'
    Invoke-TestGit -Root $worktree -Arguments @('add', 'guard-fixture.txt')
    Invoke-TestGit -Root $worktree -Arguments @('commit', '-m', 'test: exercise traditional pre-commit hook')
    if (-not (Test-Path -LiteralPath (Join-Path $worktree 'TREE.md'))) {
        throw 'The traditional pre-commit hook must still generate TREE.md.'
    }

    $null = New-Item -ItemType Directory -Path $otherRepository -Force
    Invoke-TestGit -Root $otherRepository -Arguments @('init', '-q')
    Invoke-TestGit -Root $otherRepository -Arguments @('config', 'user.name', 'Louis')
    Invoke-TestGit -Root $otherRepository -Arguments @('config', 'user.email', 'git@louisfifre.com')
    Invoke-TestGit -Root $otherRepository -Arguments @('commit', '--allow-empty', '-m', 'test: accept maintainer-only commit')

    Assert-TestGitRejected -Root $otherRepository -Arguments @(
        '-c', 'user.name=PelopeeNoire',
        'commit', '--allow-empty', '--author=Louis <git@louisfifre.com>', '-m', 'test: reject incorrect committer name'
    ) -Because 'the committer name must match the sole maintainer identity.'
    Assert-TestGitRejected -Root $otherRepository -Arguments @(
        '-c', 'user.email=louis@local.dev',
        'commit', '--allow-empty', '--author=Louis <git@louisfifre.com>', '-m', 'test: reject incorrect committer email'
    ) -Because 'the committer email must match the sole maintainer identity.'
    Assert-TestGitRejected -Root $otherRepository -Arguments @(
        'commit', '--allow-empty', '--author=PelopeeNoire <git@louisfifre.com>', '-m', 'test: reject incorrect author name'
    ) -Because 'the author name must match the sole maintainer identity.'
    Assert-TestGitRejected -Root $otherRepository -Arguments @(
        'commit', '--allow-empty', '--author=Louis <louis@local.dev>', '-m', 'test: reject incorrect author email'
    ) -Because 'the author email must match the sole maintainer identity.'

    $forbiddenMessages = @(
        'Co-Authored-By: Example Agent <agent@example.invalid>',
        'Generated with Example Agent',
        'Generated-By: Example Agent',
        'AI-Generated: Example Agent',
        'Assisted-By: Example Agent'
    )
    foreach ($forbiddenMessage in $forbiddenMessages) {
        Assert-TestGitRejected -Root $otherRepository -Arguments @(
            'commit', '--allow-empty', '-m', 'test: reject attribution marker', '-m', $forbiddenMessage
        ) -Because "'$forbiddenMessage' must be rejected in every repository."
    }

    Push-Location $testRoot
    try {
        & (Join-Path $repository 'scripts\hooks\update-tree.ps1') *> $null
    } finally {
        Pop-Location
    }
    if (-not (Test-Path -LiteralPath (Join-Path $repository 'TREE.md'))) {
        throw 'TREE.md generation must resolve its repository outside the current directory.'
    }

    Write-Host 'install-hooks.tests.ps1: PASS' -ForegroundColor Green
} finally {
    $env:GIT_CONFIG_GLOBAL = $previousGlobalConfig
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
