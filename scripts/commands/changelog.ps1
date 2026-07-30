# changelog.ps1 — Generate CHANGELOG.md and release notes from the git history.
#
# Turns the Conventional-Commit history into a Keep a Changelog document, with
# NO external dependency: plain `git log` + PowerShell, no git-cliff binary, no
# GitHub API, no network. That autonomy is a founding goal of Deckle — a
# maintainer dev-tool should not reach for a paid/cloud/foreign dependency any
# more than the shipped app does.
#
# Two outputs, one engine:
#   - default            → regenerate the whole CHANGELOG.md in place. The file
#                          is fully generated; do not hand-edit it.
#   - -NotesFor <X.Y.Z>  → emit just that one version's section (the body a
#                          GitHub Release wants), to -OutFile or stdout. This is
#                          what publish-app.ps1 feeds to `gh ... --notes-file`.
#
# How a commit becomes a line. Each commit SUBJECT is parsed as
# `type(scope)!: description`. The type routes it to a Keep a Changelog group:
#   feat → Added | fix → Fixed | perf, refactor → Changed | revert → Removed
# Everything else — chore/docs/test/ci/build/style, merges, and any subject that
# is not a recognised conventional type — is DROPPED. There is no catch-all, so
# noise never leaks into "Changed". The description keeps its first line only,
# its first letter is upper-cased, and the scope (if any) leads it in bold.
#
# The changelog is regenerated from $FloorTag forward. Below that floor the
# history predates Conventional-Commit discipline (the WhispUI genesis, the
# early research/bench cycles) and renders as noise, so it is summarised in one
# line rather than itemised. Move the floor by editing $FloorTag.
#
# Pendant to publish-app.ps1 — same Step/Ok/Warn idiom, same -Target/-Pick shape.

[CmdletBinding()]
param(
    # Build a specific repo or worktree instead of the one containing this
    # script. Accepts any path — main repo or any git worktree root.
    [string]$Target,

    # Interactive picker: lists the main repo + all linked worktrees and
    # prompts for a choice. Overrides -Target.
    [switch]$Pick,

    # Release-notes mode: emit ONLY this version's section (e.g. "0.4.5"),
    # without the document header, instead of regenerating the whole file. If
    # the version is already in release-history.json, its public-release range
    # and tag date are used; otherwise it is treated as in-progress (commits
    # since the latest public release, dated today).
    [string]$NotesFor,

    # Write the output here instead of the default (CHANGELOG.md in full mode,
    # stdout in notes mode). Used by publish-app.ps1 to capture the notes file.
    [string]$OutFile,

    # Commit the regenerated repository CHANGELOG.md when it changed. This is
    # intentionally unavailable for release-note and custom-output modes.
    [switch]$Commit
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot                                  # scripts/commands/
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'release-history.psm1') -Force

function Step($msg) { Write-Host "`n[changelog] $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "           $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "           $msg" -ForegroundColor Yellow }

$Workflow = if ($NotesFor) { 'Generate release notes' } else { 'Update changelog' }
$RepoRoot = $null
$range = $null
$dest = $null
$commitHash = $null

if ($Commit -and ($NotesFor -or $OutFile)) {
    throw '-Commit is only available when regenerating the repository CHANGELOG.md.'
}

trap {
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "$Workflow failed before completion." `
        -Details ([ordered]@{
            Worktree = $RepoRoot
            Version  = $NotesFor
            Range    = $range
            Output   = $dest
            Error    = $_.Exception.Message
        })
    throw
}

# ── RepoRoot resolution (mirrors build-run.ps1 / publish-app.ps1) ────────────
if ($Pick) {
    Import-Module (Join-Path $LibDir 'menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)  # commands/ -> scripts/ -> repo root
}

Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray

# ── Generation constants ─────────────────────────────────────────────────────
# Earliest version regenerated in full. Everything below it predates strict
# Conventional Commits and is summarised, not itemised (see $GenesisNote).
$FloorTag = 'v0.4.0'
$RepoUrl  = 'https://github.com/louisfifre/deckle'

# type → Keep a Changelog group. A type absent from this map is dropped
# (chore/docs/test/ci/build/style, "merge", and non-conventional subjects).
$TypeToGroup = [ordered]@{
    feat     = 'Added'
    fix      = 'Fixed'
    perf     = 'Changed'
    refactor = 'Changed'
    revert   = 'Removed'
}
# Fixed emission order (Keep a Changelog). Empty groups are omitted.
$GroupOrder = @('Added', 'Changed', 'Deprecated', 'Removed', 'Fixed', 'Security')

$Header = @"
# Changelog

All notable changes to Deckle are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
the project adheres to [Semantic Versioning](https://semver.org/). Deckle has no
public API: the version is read at the **user/behaviour** level, and during the
``0.x`` phase any release may change behaviour (see the ``deckle-versioning``
doctrine). This file is generated from the Conventional-Commit history by
``scripts/commands/changelog.ps1`` — do not edit it by hand.
"@

$GenesisNote = @"
## Earlier history

Versions 0.2.0 – 0.3.5 — the WhispUI genesis and the early Deckle cycles
(hotkey transcription, ambient lighting, observability) — predate this
generated changelog and are not itemised here. See the git history.
"@

# ── Helpers ──────────────────────────────────────────────────────────────────

# Upper-case the first character only, leaving the rest untouched (mirrors
# git-cliff's `upper_first`). Culture-correct for accented initials (é → É).
function Get-FirstUpper([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return $s }
    return [char]::ToUpper($s[0]) + $s.Substring(1)
}

# Public release tags, ascending. release-history.json is the offline source of
# truth; git tags alone are ambiguous because older internal version records
# also created tags.
function Get-VersionTags {
    $tags = @(Get-PublishedReleaseTags -RepoRoot $RepoRoot)
    foreach ($tag in $tags) {
        & git -C $RepoRoot rev-parse --verify --quiet "$tag^{commit}" *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Published release tag $tag is missing from the local repository"
        }
    }
    return [string[]]$tags
}

# ISO date (YYYY-MM-DD) of the commit a tag points at.
function Get-TagDate([string]$tag) {
    $d = & git -C $RepoRoot log -1 --format='%cs' "$tag^{commit}"
    if ($LASTEXITCODE -ne 0) { throw "git log for $tag failed (code $LASTEXITCODE)" }
    return $d.Trim()
}

# Commits in a range (e.g. "v0.3.5..v0.4.0"), oldest first. Returns objects with
# Subject only — the body is never read (no parser inspects it, so nothing from
# a commit body, including any trailer, can leak into the changelog).
function Get-RangeCommits([string]$range) {
    $fmt = '%s%x1f%x1e'                                     # subject, US, RS
    $out = (& git -C $RepoRoot log --reverse "--format=$fmt" $range) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "git log $range failed (code $LASTEXITCODE)" }
    $commits = [System.Collections.Generic.List[string]]::new()
    foreach ($rec in ($out -split ([char]0x1e))) {
        $subject = ($rec -split ([char]0x1f))[0].Trim("`r", "`n", ' ')
        if ($subject) { $commits.Add($subject) }
    }
    # Comma stops pipeline unrolling, so an empty range yields a real empty
    # array (not $null) — the contract is "an array of subjects", always.
    return , $commits.ToArray()
}

# Parse one subject into @{ Group; Line } or $null when the commit is dropped.
function ConvertTo-Entry([string]$subject) {
    # Case-sensitive: "Merge …" / git's "Revert …" start with a capital and must
    # NOT match the lowercase type token, so they fall through to dropped.
    if ($subject -cnotmatch '^(?<type>[a-z]+)(?:\((?<scope>[^)]+)\))?!?:\s+(?<desc>.+)$') {
        return $null
    }
    $group = $TypeToGroup[$Matches.type]
    if (-not $group) { return $null }                        # skipped / unmapped type

    $desc = Get-FirstUpper (($Matches.desc -split "`n")[0].Trim())
    $line = if ($Matches.scope) { "- **$($Matches.scope):** $desc" } else { "- $desc" }
    return @{ Group = $group; Line = $line }
}

# Render one version section. $thisTag/$prevTag carry the leading 'v'.
function Format-Section([string]$version, [string]$prevTag, [string]$thisTag, [string]$date, [string[]]$subjects) {
    $byGroup = @{}
    foreach ($s in $subjects) {
        $e = ConvertTo-Entry $s
        if ($null -ne $e) {
            if (-not $byGroup.ContainsKey($e.Group)) { $byGroup[$e.Group] = [System.Collections.Generic.List[string]]::new() }
            $byGroup[$e.Group].Add($e.Line)
        }
    }

    $heading = if (-not $version) {
        '## [Unreleased]'
    } elseif ($prevTag) {
        "## [$version]($RepoUrl/compare/$prevTag...$thisTag) — $date"
    } else {
        "## [$version]($RepoUrl/releases/tag/$thisTag) — $date"
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add($heading)
    foreach ($g in $GroupOrder) {
        if ($byGroup.ContainsKey($g)) {
            $lines.Add('')
            $lines.Add("### $g")
            $lines.Add('')
            foreach ($l in $byGroup[$g]) { $lines.Add($l) }
        }
    }
    return ($lines -join "`n")
}

# ── Mode: release notes (single version) ─────────────────────────────────────
if ($NotesFor) {
    $version = $NotesFor.TrimStart('v')
    $thisTag = "v$version"
    $tags    = Get-VersionTags

    if ($tags -contains $thisTag) {
        # Public release: range from the preceding public release.
        $idx     = [array]::IndexOf($tags, $thisTag)
        $prevTag = if ($idx -gt 0) { $tags[$idx - 1] } else { $null }
        $range   = if ($prevTag) { "$prevTag..$thisTag" } else { $thisTag }
        $date    = Get-TagDate $thisTag
    } else {
        # In-progress release: commits since the latest public release. A
        # legacy internal tag may already exist; use it as the endpoint without
        # treating it as a release boundary.
        $prevTag = if ($tags.Count) { $tags[-1] } else { $null }
        & git -C $RepoRoot rev-parse --verify --quiet "$thisTag^{commit}" *> $null
        $endRef  = if ($LASTEXITCODE -eq 0) { $thisTag } else { 'HEAD' }
        $range   = if ($prevTag) { "$prevTag..$endRef" } else { $endRef }
        $date    = (Get-Date -Format 'yyyy-MM-dd')
    }

    Step "Release notes for v$version (range: $range)"
    $subjects = Get-RangeCommits $range
    $section  = Format-Section $version $prevTag $thisTag $date $subjects
    if ($section -notmatch '### ') { Warn "No user-facing commits in $range — notes are just a heading." }

    if ($OutFile) {
        [System.IO.File]::WriteAllText($OutFile, $section + "`n", [System.Text.UTF8Encoding]::new($false))
        Ok "Notes written to $OutFile"
        $dest = $OutFile
    } else {
        Write-Output $section
        $dest = 'stdout'
    }
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Success `
        -Sentence "Release notes for v$version were generated from $range." `
        -Details ([ordered]@{
            Worktree = $RepoRoot
            Version  = "v$version"
            Range    = $range
            Commits  = $subjects.Count
            Output   = $dest
        })
    return
}

# ── Mode: full regeneration of CHANGELOG.md ──────────────────────────────────
$tags = Get-VersionTags
if (-not $tags.Count) { throw "No published release tags found in $RepoRoot" }

$floorIdx = [array]::IndexOf($tags, $FloorTag)
if ($floorIdx -lt 0) { throw "Floor tag $FloorTag not found in release-history.json" }

Step "Regenerating changelog from $FloorTag (latest public release: $($tags[-1]))"

# Rendered tags: floor .. latest, emitted newest-first.
$rendered = $tags[$floorIdx..($tags.Count - 1)]
$sections = [System.Collections.Generic.List[string]]::new()

# Optional [Unreleased] section: commits beyond the latest public release, only
# if any are user-facing (otherwise an empty heading would be noise).
$unreleased = Get-RangeCommits "$($tags[-1])..HEAD"
if ($unreleased.Count) {
    $hasEntry = $false
    foreach ($s in $unreleased) { if (ConvertTo-Entry $s) { $hasEntry = $true; break } }
    if ($hasEntry) {
        $sections.Add((Format-Section '' $null $null '' $unreleased))
        Ok "Unreleased — $($unreleased.Count) commits since public release $($tags[-1])"
    }
}

for ($i = $rendered.Count - 1; $i -ge 0; $i--) {
    $thisTag = $rendered[$i]
    $version = $thisTag.TrimStart('v')
    # Previous boundary is the preceding public release, never an internal tag.
    $globalIdx = [array]::IndexOf($tags, $thisTag)
    $prevTag   = if ($globalIdx -gt 0) { $tags[$globalIdx - 1] } else { $null }
    $range     = if ($prevTag) { "$prevTag..$thisTag" } else { $thisTag }
    $date      = Get-TagDate $thisTag
    $subjects  = Get-RangeCommits $range
    $sections.Add((Format-Section $version $prevTag $thisTag $date $subjects))
    Ok "v$version — $($subjects.Count) commits in range"
}

$doc = $Header.TrimEnd() + "`n`n" + (($sections -join "`n`n")) + "`n`n" + $GenesisNote.TrimEnd() + "`n"

$dest = if ($OutFile) { $OutFile } else { Join-Path $RepoRoot 'CHANGELOG.md' }
[System.IO.File]::WriteAllText($dest, $doc, [System.Text.UTF8Encoding]::new($false))
Step "Done"
Ok "Wrote $dest"

if ($Commit) {
    & git -C $RepoRoot add -- CHANGELOG.md
    if ($LASTEXITCODE -ne 0) { throw 'Could not stage CHANGELOG.md.' }

    & git -C $RepoRoot diff --cached --quiet -- CHANGELOG.md
    $diffExitCode = $LASTEXITCODE
    if ($diffExitCode -eq 1) {
        & git -C $RepoRoot commit -m 'docs(changelog): refresh unreleased changes' -- CHANGELOG.md
        if ($LASTEXITCODE -ne 0) { throw 'Could not commit CHANGELOG.md.' }
        $commitHash = (& git -C $RepoRoot rev-parse --short HEAD).Trim()
        Ok "Committed CHANGELOG.md ($commitHash)"
    } elseif ($diffExitCode -eq 0) {
        Ok 'CHANGELOG.md is already current; no commit was needed.'
    } else {
        throw 'Could not inspect the staged CHANGELOG.md change.'
    }
}

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence "CHANGELOG.md was regenerated from $FloorTag through public release $($tags[-1])." `
    -Details ([ordered]@{
        Worktree     = $RepoRoot
        Output       = $dest
        'Floor tag'  = $FloorTag
        'Latest public release' = $tags[-1]
        Sections     = $sections.Count
        Commit       = $commitHash
    })
