$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'

# A throwaway repository holding copies of the scripts under test, and a stub
# standing for the Claude Code CLI: the test sends nothing outside.
$root = Join-Path ([IO.Path]::GetTempPath()) "deckle-draft-notes-$([guid]::NewGuid())"
$scriptPath = Join-Path $root 'scripts\release\draft-release-notes.ps1'
$stubPath = Join-Path $root 'stub-claude.ps1'
$recordDir = Join-Path $root 'recorded'

# No param block: a git flag such as -a would bind to a parameter of ours.
function Invoke-TestGit {
    $output = & git -C $root @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed: $output" }
    return $output
}

# The script runs in a child pwsh: its trap rethrows, which an in-process call
# would report as a bare "ScriptHalted".
function Invoke-Draft {
    $text = @(& pwsh -NoProfile -File $scriptPath @args 2>&1 | ForEach-Object { [string]$_ }) -join "`n"
    return [pscustomobject]@{ Code = $LASTEXITCODE; Text = $text }
}

function Assert-Fails([string[]]$Arguments, [string]$Expected) {
    $run = Invoke-Draft @Arguments
    if ($run.Code -eq 0) { throw "Expected a failure containing '$Expected'" }
    if ($run.Text -notlike "*$Expected*") { throw "Expected error containing '$Expected', got '$($run.Text)'" }
}

function Add-Merge([string]$Branch, [string]$Subject, [string]$Message) {
    Invoke-TestGit checkout -q -b $Branch | Out-Null
    $file = Join-Path $root "$($Branch -replace '/', '-').txt"
    Set-Content -LiteralPath $file -Value $Branch -Encoding utf8NoBOM
    Invoke-TestGit add . | Out-Null
    Invoke-TestGit commit -q -m $Subject | Out-Null
    Invoke-TestGit checkout -q main | Out-Null
    Invoke-TestGit merge -q --no-ff $Branch -m $Message | Out-Null
}

try {
    foreach ($dir in 'scripts\release', 'scripts\git', 'scripts\lib', 'recorded') {
        $null = New-Item -ItemType Directory -Path (Join-Path $root $dir) -Force
    }
    Copy-Item -LiteralPath (Join-Path $ScriptsDir 'release\draft-release-notes.ps1') -Destination $scriptPath
    foreach ($name in 'changelog.ps1', 'git-common.ps1') {
        Copy-Item -LiteralPath (Join-Path $ScriptsDir "git\$name") -Destination (Join-Path $root "scripts\git\$name")
    }
    foreach ($name in 'action-summary.ps1', 'script-output.ps1', 'version.ps1', 'release-validation.psm1') {
        Copy-Item -LiteralPath (Join-Path $LibDir $name) -Destination (Join-Path $root "scripts\lib\$name")
    }

    # The stub records what it was called with and what came in on standard
    # input, and answers with a fixed text.
    @'
$record = Join-Path $PSScriptRoot 'recorded'
Set-Content -LiteralPath (Join-Path $record 'arguments.txt') -Value ($args | ForEach-Object { "[$_]" }) -Encoding utf8NoBOM
Set-Content -LiteralPath (Join-Path $record 'prompt.txt') -Value @($input) -Encoding utf8NoBOM
Set-Content -LiteralPath (Join-Path $record 'cwd.txt') -Value (Get-Location).Path -Encoding utf8NoBOM
'## What is new'
''
'- Stub notes.'
exit 0
'@ | Set-Content -LiteralPath $stubPath -Encoding utf8NoBOM

    Invoke-TestGit init --initial-branch=main | Out-Null
    Invoke-TestGit config user.name 'Deckle Tests' | Out-Null
    Invoke-TestGit config user.email 'deckle-tests@example.com' | Out-Null
    Invoke-TestGit config core.autocrlf false | Out-Null
    Invoke-TestGit config commit.gpgsign false | Out-Null
    Invoke-TestGit config core.hooksPath (Join-Path $root 'no-hooks') | Out-Null
    Set-Content -LiteralPath (Join-Path $root '.gitignore') -Value "artifacts/`nrecorded/`nstub-claude.ps1`nfailing-claude.ps1" -Encoding utf8NoBOM
    Invoke-TestGit add . | Out-Null
    Invoke-TestGit commit -q -m 'chore: seed repository' | Out-Null
    Invoke-TestGit tag -a v0.1.0 -m v0.1.0 | Out-Null
    Invoke-TestGit tag -a release/v0.1.0 -m release/v0.1.0 | Out-Null

    Add-Merge 'feat/lexicon' 'feat(lexicon): add a page of words' 'Merge feat/lexicon: the lexicon page lists the words added one by one'
    Add-Merge 'fix/hotkey' 'fix(input): keep the shortcut over the lock screen' 'Merge fix/hotkey: the shortcut works over the lock screen'
    Add-Merge 'refactor/internal' 'refactor: move a helper' "Merge refactor/internal: an internal helper moves`n`n# Conflicts:`n#`tsrc/Secret.cs"
    Invoke-TestGit tag -a v0.2.0 -m v0.2.0 | Out-Null

    # Nothing on PATH answers to the name: one clear sentence.
    Assert-Fails @('-ClaudeCommand', 'deckle-no-such-cli-xyz') 'is not on PATH'

    # The real run shape, with the stub.
    $run = Invoke-Draft -ClaudeCommand $stubPath -AlsoSay 'Mail is no longer part of Deckle.'
    if ($run.Code -ne 0) { throw "The draft run failed: $($run.Text)" }
    $draftPath = Join-Path $root 'artifacts\release-notes-0.2.0.md'
    if (-not (Test-Path -LiteralPath $draftPath)) { throw 'The draft was not written under artifacts.' }
    if ((Get-Content -Raw -LiteralPath $draftPath) -notlike '*Stub notes.*') { throw 'The draft does not hold the answer of the CLI.' }
    if ($run.Text -notlike "*$draftPath*") { throw 'The path of the draft was not printed.' }

    $arguments = Get-Content -Raw -LiteralPath (Join-Path $recordDir 'arguments.txt')
    foreach ($expected in '[-p]', '[--model]', '[haiku]', '[--tools]', '[--no-session-persistence]', '[--strict-mcp-config]') {
        if (-not $arguments.Contains($expected)) { throw "The CLI was not called with $expected. Got: $arguments" }
    }
    if ($arguments -match '(?i)api[-_ ]?key|--settings|--mcp-config|--add-dir|--dangerously') { throw "The CLI call carries an option it must not: $arguments" }

    $prompt = Get-Content -Raw -LiteralPath (Join-Path $recordDir 'prompt.txt')
    foreach ($expected in 'the lexicon page lists the words added one by one', 'the shortcut works over the lock screen', 'Mail is no longer part of Deckle.', '### Added', 'Version: 0.2.0', 'Follows: 0.1.0') {
        if ($prompt -notlike "*$expected*") { throw "The prompt does not hold '$expected'." }
    }
    # Nothing but the given lines: no merge body, no repository address, no
    # path of this machine, no address.
    foreach ($forbidden in 'Secret.cs', 'Conflicts', 'github.com', $root, [Environment]::UserName, '@', '## [0.2.0]') {
        if ($prompt -like "*$forbidden*") { throw "The prompt holds '$forbidden'." }
    }
    $cwd = (Get-Content -Raw -LiteralPath (Join-Path $recordDir 'cwd.txt')).Trim()
    if ($cwd -like "$root*") { throw 'The CLI ran inside the repository.' }

    # Not published, not committed: artifacts/ is ignored.
    if (@(Invoke-TestGit status --porcelain).Count -ne 0) { throw 'The draft left the repository dirty.' }

    # -Version and -Since are read: a version that does not follow, a missing tag.
    Assert-Fails @('-ClaudeCommand', $stubPath, '-Version', '0.1.0') 'nothing to draft'
    Assert-Fails @('-ClaudeCommand', $stubPath, '-Since', '0.0.9') 'No tag v0.0.9'
    Assert-Fails @('-ClaudeCommand', $stubPath, '-Version', 'nonsense') 'expects a version'

    # A CLI that fails stops the script and writes no draft.
    $failing = Join-Path $root 'failing-claude.ps1'
    "'not logged in'`nexit 3" | Set-Content -LiteralPath $failing -Encoding utf8NoBOM
    Remove-Item -LiteralPath $draftPath
    Assert-Fails @('-ClaudeCommand', $failing) 'exit code 3'
    if (Test-Path -LiteralPath $draftPath) { throw 'A failed CLI call left a draft.' }

    # The script is not wired into publish-app.ps1.
    $publish = Get-Content -Raw -LiteralPath (Join-Path $ScriptsDir 'release\publish-app.ps1')
    if ($publish -like '*draft-release-notes*') { throw 'publish-app.ps1 must not call the draft script.' }

    Write-Host 'draft-release-notes.tests.ps1: PASS' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
