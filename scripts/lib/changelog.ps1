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
    # the tag v<X.Y.Z> exists, its range and date are used; otherwise the
    # version is treated as in-progress (commits since the latest tag, dated
    # today) so notes can be produced before the release tag is cut.
    [string]$NotesFor,

    # Write the output here instead of the default (CHANGELOG.md in full mode,
    # stdout in notes mode). Used by publish-app.ps1 to capture the notes file.
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot                                  # scripts/lib/

function Step($msg) { Write-Host "`n[changelog] $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "           $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "           $msg" -ForegroundColor Yellow }

# ── RepoRoot resolution (mirrors build-run.ps1 / publish-app.ps1) ────────────
if ($Pick) {
    Import-Module (Join-Path $ScriptDir '_menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)  # lib/ -> scripts/ -> repo root
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
``scripts/lib/changelog.ps1`` — do not edit it by hand.
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

# All SemVer release tags (vX.Y.Z), ascending. Excludes the unrelated
# native-runtime tag (native-v*) — the 'v*' glob already does.
function Get-VersionTags {
    $tags = & git -C $RepoRoot tag --list 'v*' --sort=version:refname
    if ($LASTEXITCODE -ne 0) { throw "git tag failed (code $LASTEXITCODE)" }
    return @($tags | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' })
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
        # Released version: range from the preceding tag, dated at the tag.
        $idx     = [array]::IndexOf($tags, $thisTag)
        $prevTag = if ($idx -gt 0) { $tags[$idx - 1] } else { $null }
        $range   = if ($prevTag) { "$prevTag..$thisTag" } else { $thisTag }
        $date    = Get-TagDate $thisTag
    } else {
        # In-progress version (tag not cut yet): commits since the latest tag.
        $prevTag = if ($tags.Count) { $tags[-1] } else { $null }
        $range   = if ($prevTag) { "$prevTag..HEAD" } else { 'HEAD' }
        $date    = (Get-Date -Format 'yyyy-MM-dd')
    }

    Step "Release notes for v$version (range: $range)"
    $subjects = Get-RangeCommits $range
    $section  = Format-Section $version $prevTag $thisTag $date $subjects
    if ($section -notmatch '### ') { Warn "No user-facing commits in $range — notes are just a heading." }

    if ($OutFile) {
        [System.IO.File]::WriteAllText($OutFile, $section + "`n", [System.Text.UTF8Encoding]::new($false))
        Ok "Notes written to $OutFile"
    } else {
        Write-Output $section
    }
    return
}

# ── Mode: full regeneration of CHANGELOG.md ──────────────────────────────────
$tags = Get-VersionTags
if (-not $tags.Count) { throw "No version tags (v*) found in $RepoRoot" }

$floorIdx = [array]::IndexOf($tags, $FloorTag)
if ($floorIdx -lt 0) { throw "Floor tag $FloorTag not found among version tags" }

Step "Regenerating changelog from $FloorTag (latest: $($tags[-1]))"

# Rendered tags: floor .. latest, emitted newest-first.
$rendered = $tags[$floorIdx..($tags.Count - 1)]
$sections = [System.Collections.Generic.List[string]]::new()

# Optional [Unreleased] section: commits beyond the latest tag, only if any of
# them is user-facing (otherwise an empty Unreleased heading would be noise).
$unreleased = Get-RangeCommits "$($tags[-1])..HEAD"
if ($unreleased.Count) {
    $hasEntry = $false
    foreach ($s in $unreleased) { if (ConvertTo-Entry $s) { $hasEntry = $true; break } }
    if ($hasEntry) {
        $sections.Add((Format-Section '' $null $null '' $unreleased))
        Ok "Unreleased — $($unreleased.Count) commits since $($tags[-1])"
    }
}

for ($i = $rendered.Count - 1; $i -ge 0; $i--) {
    $thisTag = $rendered[$i]
    $version = $thisTag.TrimStart('v')
    # Previous tag is the neighbour below in the FULL tag list (may be < floor).
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
