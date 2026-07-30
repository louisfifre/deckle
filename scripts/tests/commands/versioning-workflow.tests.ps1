$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'

function Invoke-Git([string]$Root, [string[]]$Arguments) {
    & git -C $Root @Arguments *> $null
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed (code $LASTEXITCODE)" }
}

function Add-Commit([string]$Root, [string]$Subject, [string]$Content) {
    Set-Content -LiteralPath (Join-Path $Root 'fixture.txt') -Value $Content -Encoding utf8NoBOM
    Invoke-Git $Root @('add', 'fixture.txt')
    Invoke-Git $Root @('commit', '-m', $Subject)
}

function Assert-Contains([string]$Text, [string]$Expected, [string]$Message) {
    if (-not $Text.Contains($Expected)) { throw "$Message`nMissing: $Expected" }
}

function Assert-NotContains([string]$Text, [string]$Unexpected, [string]$Message) {
    if ($Text.Contains($Unexpected)) { throw "$Message`nUnexpected: $Unexpected" }
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-versioning-$([guid]::NewGuid())"
$null = New-Item -ItemType Directory -Path $root
try {
    Invoke-Git $root @('init', '-q')
    Invoke-Git $root @('config', 'user.name', 'Deckle Tests')
    Invoke-Git $root @('config', 'user.email', 'deckle-tests@example.invalid')

    Add-Commit $root 'feat: first public capability' 'one'
    Invoke-Git $root @('tag', 'v0.4.0')
    Add-Commit $root 'fix: historical internal fix' 'two'
    Invoke-Git $root @('tag', 'v0.5.0')
    Add-Commit $root 'feat: second public capability' 'three'
    Invoke-Git $root @('tag', 'v0.8.0')
    Add-Commit $root 'feat: accumulated feature' 'four'
    Invoke-Git $root @('tag', 'v0.9.0')
    Add-Commit $root 'fix: accumulated fix' 'five'

    Set-Content -LiteralPath (Join-Path $root 'release-history.json') -Encoding utf8NoBOM -Value @'
{
  "PublishedTags": ["v0.4.0", "v0.8.0"]
}
'@

    $changelog = Join-Path $root 'CHANGELOG.md'
    & (Join-Path $CommandDir 'changelog.ps1') -Target $root -OutFile $changelog
    if (-not $?) { throw 'Full changelog generation failed.' }
    $full = Get-Content -Raw -LiteralPath $changelog
    Assert-Contains $full '## [Unreleased]' 'Changes since the latest public release should accumulate under Unreleased.'
    Assert-Contains $full 'Accumulated feature' 'The accumulator should cross an internal tag.'
    Assert-Contains $full 'Accumulated fix' 'The accumulator should include every later user-facing commit.'
    Assert-NotContains $full '## [0.5.0]' 'An internal historical tag must not become a changelog section.'
    Assert-NotContains $full '## [0.9.0]' 'An internal current tag must not become a changelog section.'

    & (Join-Path $CommandDir 'changelog.ps1') -Target $root -Commit
    if (-not $?) { throw 'Committed changelog generation failed.' }
    $changelogCommit = (& git -C $root log -1 --format='%s').Trim()
    Assert-Contains $changelogCommit 'docs(changelog): refresh unreleased changes' 'The menu mode should commit the generated changelog.'
    $headBeforeNoOp = (& git -C $root rev-parse HEAD).Trim()
    & (Join-Path $CommandDir 'changelog.ps1') -Target $root -Commit
    if (-not $?) { throw 'No-op changelog generation failed.' }
    $headAfterNoOp = (& git -C $root rev-parse HEAD).Trim()
    if ($headBeforeNoOp -ne $headAfterNoOp) { throw 'An unchanged changelog must not create an empty commit.' }

    $notes = Join-Path $root 'notes.md'
    & (Join-Path $CommandDir 'changelog.ps1') -Target $root -NotesFor 0.13.0 -OutFile $notes
    if (-not $?) { throw 'Release-note generation failed.' }
    $releaseNotes = Get-Content -Raw -LiteralPath $notes
    Assert-Contains $releaseNotes 'compare/v0.8.0...v0.13.0' 'Notes should compare against the latest public release.'
    Assert-Contains $releaseNotes 'Accumulated feature' 'Release notes should include changes before an internal tag.'
    Assert-Contains $releaseNotes 'Accumulated fix' 'Release notes should include changes after an internal tag.'

    $projectDir = Join-Path $root 'src\Deckle.App'
    $null = New-Item -ItemType Directory -Path $projectDir
    Set-Content -LiteralPath (Join-Path $projectDir 'Deckle.App.csproj') -Encoding utf8NoBOM -Value '<Project><PropertyGroup><Version>0.13.0</Version></PropertyGroup></Project>'
    Invoke-Git $root @('add', 'src/Deckle.App/Deckle.App.csproj', 'release-history.json', 'CHANGELOG.md')
    Invoke-Git $root @('commit', '-m', 'chore(version): v0.13.0')

    & (Join-Path $CommandDir 'record-version.ps1') -Target $root -Bump patch
    if (-not $?) { throw 'Internal version recording failed.' }
    $project = Get-Content -Raw -LiteralPath (Join-Path $projectDir 'Deckle.App.csproj')
    Assert-Contains $project '<Version>0.13.1</Version>' 'The internal version should advance.'
    $newTag = & git -C $root tag --list 'v0.13.1'
    if ($newTag) { throw 'An internal version record must not create a git tag.' }
    $latestSubjects = @(& git -C $root log -2 --format='%s')
    Assert-Contains ($latestSubjects -join "`n") 'chore(version): v0.13.1' 'The bump should use the internal version commit type.'

    Invoke-Git $root @('tag', 'v0.13.1')
    & (Join-Path $CommandDir 'record-release.ps1') -Target $root -Version 0.13.1
    if (-not $?) { throw 'Public release recording failed.' }
    $frozen = Get-Content -Raw -LiteralPath $changelog
    Assert-Contains $frozen '## [0.13.1]' 'The successful public release should freeze a version section.'
    Assert-Contains $frozen 'compare/v0.8.0...v0.13.1' 'The frozen section should span the previous public release.'
    Assert-NotContains $frozen '## [Unreleased]' 'The accumulator should reset when no later user-facing change exists.'

    Write-Host 'versioning-workflow.tests.ps1 passed' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
