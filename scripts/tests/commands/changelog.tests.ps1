$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandDir = Join-Path $ScriptsDir 'commands'

function Invoke-Git([string]$Root, [string[]]$Arguments) {
    $output = & git -C $Root @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed (code $LASTEXITCODE): $($output -join [Environment]::NewLine)" }
}

function New-FixtureRepo([string]$Name) {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-changelog-$Name-$([guid]::NewGuid())"
    $null = New-Item -ItemType Directory -Path $root
    Invoke-Git $root @('init', '-q')
    # The global commit guard accepts the maintainer's identity alone, in
    # every repository, fixtures included.
    Invoke-Git $root @('config', 'user.name', 'Louis')
    Invoke-Git $root @('config', 'user.email', 'git@louisfifre.com')
    return $root
}

# Returns the new commit's hash so a later revert can point at it.
function Add-Commit([string]$Root, [string]$Subject, [string]$Content, [string]$Body) {
    Set-Content -LiteralPath (Join-Path $Root 'fixture.txt') -Value $Content -Encoding utf8NoBOM
    Invoke-Git $Root @('add', 'fixture.txt')
    $arguments = @('commit', '-m', $Subject)
    if ($Body) { $arguments += @('-m', $Body) }
    Invoke-Git $Root $arguments
    return (& git -C $Root rev-parse HEAD).Trim()
}

function Set-PublishedTags([string]$Root, [string[]]$Tags) {
    $json = '{ "PublishedTags": [' + (($Tags | ForEach-Object { "`"$_`"" }) -join ', ') + '] }'
    Set-Content -LiteralPath (Join-Path $Root 'release-history.json') -Value $json -Encoding utf8NoBOM
}

function Invoke-Changelog([string]$Root) {
    $changelog = Join-Path $Root 'CHANGELOG.md'
    & (Join-Path $CommandDir 'changelog.ps1') -Target $Root -OutFile $changelog
    if (-not $?) { throw 'Changelog generation failed.' }
    return (Get-Content -Raw -LiteralPath $changelog)
}

function Assert-Contains([string]$Text, [string]$Expected, [string]$Message) {
    if (-not $Text.Contains($Expected)) { throw "$Message`nMissing: $Expected" }
}

function Assert-NotContains([string]$Text, [string]$Unexpected, [string]$Message) {
    if ($Text.Contains($Unexpected)) { throw "$Message`nUnexpected: $Unexpected" }
}

function Get-Section([string]$Document, [string]$Heading) {
    $start = $Document.IndexOf($Heading)
    if ($start -lt 0) { throw "Section not found: $Heading" }
    $next = $Document.IndexOf("`n## ", $start + $Heading.Length)
    if ($next -lt 0) { return $Document.Substring($start) }
    return $Document.Substring($start, $next - $start)
}

$roots = [System.Collections.Generic.List[string]]::new()
try {
    # Every case the accumulator must settle, in one history.
    $root = New-FixtureRepo 'cases'
    $roots.Add($root)
    $null = Add-Commit $root 'feat: first public capability' 'one'
    Invoke-Git $root @('tag', 'v0.4.0')
    $released = Add-Commit $root 'feat(audio): released capability' 'two'
    Invoke-Git $root @('tag', 'v0.5.0')
    $origin = Add-Commit $root 'feat(hud): smooth the rise' 'three'
    $null = Add-Commit $root 'revert(hud): take the smoothing back out' 'four' "This reverts commit $origin."
    $null = Add-Commit $root 'refactor(settings)!: drop the legacy page' 'five'
    $null = Add-Commit $root 'chore!: drop the old installer' 'six'
    $null = Add-Commit $root 'revert(audio): remove the released capability' 'seven' "This reverts commit $released."
    $null = Add-Commit $root 'revert(scripts): undo a step by hand' 'eight'
    $null = Add-Commit $root 'feat(home): accumulated feature' 'nine'
    Set-PublishedTags $root @('v0.4.0', 'v0.5.0')

    $full = Invoke-Changelog $root
    $unreleased = Get-Section $full '## [Unreleased]'

    Assert-Contains $unreleased '### Removed' 'The breaking marker must open the Removed group.'
    Assert-Contains $unreleased '- **settings:** Drop the legacy page' 'A mapped type with the breaking marker lands under Removed.'
    Assert-Contains $unreleased '- Drop the old installer' 'The breaking marker surfaces a type the map would otherwise drop.'
    Assert-NotContains $unreleased 'Smooth the rise' 'A feature reverted before release must leave no Added line.'
    Assert-NotContains $unreleased 'Take the smoothing back out' 'A revert of an unreleased feature must leave no Removed line.'
    Assert-Contains $unreleased '- **audio:** Remove the released capability' 'A revert of a released capability stays a Removed.'
    Assert-Contains $unreleased '- **scripts:** Undo a step by hand' 'A revert without the pointer stays a Removed.'
    Assert-Contains $unreleased '- **home:** Accumulated feature' 'Other lines of the accumulator are untouched.'
    Assert-Contains (Get-Section $full '## [0.5.0]') '- **audio:** Released capability' 'A frozen section keeps the line a later revert undoes.'

    # An accumulator holding only a reverted feature has nothing to announce.
    $root = New-FixtureRepo 'cancelled'
    $roots.Add($root)
    $null = Add-Commit $root 'feat: first public capability' 'one'
    Invoke-Git $root @('tag', 'v0.4.0')
    $origin = Add-Commit $root 'feat(hud): smooth the rise' 'two'
    $null = Add-Commit $root 'revert(hud): take the smoothing back out' 'three' "This reverts commit $origin."
    Set-PublishedTags $root @('v0.4.0')

    $full = Invoke-Changelog $root
    Assert-NotContains $full '## [Unreleased]' 'A cancelled pair must not open an Unreleased section.'

    Write-Host 'changelog.tests.ps1 passed' -ForegroundColor Green
} finally {
    foreach ($r in $roots) {
        if (Test-Path -LiteralPath $r) { Remove-Item -LiteralPath $r -Recurse -Force }
    }
}
