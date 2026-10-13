<#
.SYNOPSIS
    Update the changelog
.DESCRIPTION
    Generate the changelog and release notes from the git history. The
    Conventional-Commit history becomes a Keep a Changelog document with no
    external dependency: plain git log and PowerShell, no git-cliff binary, no
    GitHub API, no network. That autonomy is a founding goal of Deckle.
.NOTES
    section: Project
    order: 220
    action: Update changelog | -Commit
#>

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

$WorkflowOutput = New-DeckleWorkflowOutput -Category 'changelog'

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

if ($Pick) {
    Import-Module (Join-Path $LibDir 'menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)  # commands/ -> scripts/ -> repo root
}

Write-DeckleOutputText -Text "Repo: $RepoRoot" -Role Muted

# Earliest version regenerated in full. Everything below it predates strict
# Conventional Commits and is summarised, not itemised (see $GenesisNote).
$FloorTag = 'v0.4.0'
$RepoUrl  = 'https://github.com/louisfifre/deckle'

# A type absent from this map is dropped (chore/docs/test/ci/build/style,
# merges, non-conventional subjects): no catch-all, so noise never reaches
# Changed.
$TypeToGroup = [ordered]@{
    feat     = 'Added'
    fix      = 'Fixed'
    perf     = 'Changed'
    refactor = 'Changed'
    revert   = 'Removed'
}
# Keep a Changelog order.
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

function Get-FirstUpper([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return $s }
    return [char]::ToUpper($s[0]) + $s.Substring(1)
}

# release-history.json is the offline source of truth for public releases; git
# tags alone are ambiguous because older internal version records also created
# tags.
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

function Get-TagDate([string]$tag) {
    $d = & git -C $RepoRoot log -1 --format='%cs' "$tag^{commit}"
    if ($LASTEXITCODE -ne 0) { throw "git log for $tag failed (code $LASTEXITCODE)" }
    return $d.Trim()
}

# Subjects only, oldest first: the body is never read, so nothing from it,
# trailers included, can leak into the changelog.
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

# $thisTag and $prevTag carry the leading 'v'.
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

if ($NotesFor) {
    $version = $NotesFor.TrimStart('v')
    $thisTag = "v$version"
    $tags    = Get-VersionTags

    if ($tags -contains $thisTag) {
        $idx     = [array]::IndexOf($tags, $thisTag)
        $prevTag = if ($idx -gt 0) { $tags[$idx - 1] } else { $null }
        $range   = if ($prevTag) { "$prevTag..$thisTag" } else { $thisTag }
        $date    = Get-TagDate $thisTag
    } else {
        # A legacy internal tag may already exist: it serves as the endpoint,
        # never as a release boundary.
        $prevTag = if ($tags.Count) { $tags[-1] } else { $null }
        & git -C $RepoRoot rev-parse --verify --quiet "$thisTag^{commit}" *> $null
        $endRef  = if ($LASTEXITCODE -eq 0) { $thisTag } else { 'HEAD' }
        $range   = if ($prevTag) { "$prevTag..$endRef" } else { $endRef }
        $date    = (Get-Date -Format 'yyyy-MM-dd')
    }

    Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Release notes for v$version (range: $range)"
    $subjects = Get-RangeCommits $range
    $section  = Format-Section $version $prevTag $thisTag $date $subjects
    if ($section -notmatch '### ') {
        Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "No user-facing commits in $range — notes are just a heading." -Role Warning
    }

    if ($OutFile) {
        [System.IO.File]::WriteAllText($OutFile, $section + "`n", [System.Text.UTF8Encoding]::new($false))
        Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "Notes written to $OutFile"
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

$tags = Get-VersionTags
if (-not $tags.Count) { throw "No published release tags found in $RepoRoot" }

$floorIdx = [array]::IndexOf($tags, $FloorTag)
if ($floorIdx -lt 0) { throw "Floor tag $FloorTag not found in release-history.json" }

Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Regenerating changelog from $FloorTag (latest public release: $($tags[-1]))"

$rendered = $tags[$floorIdx..($tags.Count - 1)]
$sections = [System.Collections.Generic.List[string]]::new()

# [Unreleased] appears only when a commit is user-facing: an empty heading
# would be noise.
$unreleased = Get-RangeCommits "$($tags[-1])..HEAD"
if ($unreleased.Count) {
    $hasEntry = $false
    foreach ($s in $unreleased) { if (ConvertTo-Entry $s) { $hasEntry = $true; break } }
    if ($hasEntry) {
        $sections.Add((Format-Section '' $null $null '' $unreleased))
        Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "Unreleased — $($unreleased.Count) commits since public release $($tags[-1])"
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
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "v$version — $($subjects.Count) commits in range"
}

$doc = $Header.TrimEnd() + "`n`n" + (($sections -join "`n`n")) + "`n`n" + $GenesisNote.TrimEnd() + "`n"

$dest = if ($OutFile) { $OutFile } else { Join-Path $RepoRoot 'CHANGELOG.md' }
[System.IO.File]::WriteAllText($dest, $doc, [System.Text.UTF8Encoding]::new($false))
Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Done"
Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "Wrote $dest"

if ($Commit) {
    & git -C $RepoRoot add -- CHANGELOG.md
    if ($LASTEXITCODE -ne 0) { throw 'Could not stage CHANGELOG.md.' }

    & git -C $RepoRoot diff --cached --quiet -- CHANGELOG.md
    $diffExitCode = $LASTEXITCODE
    if ($diffExitCode -eq 1) {
        & git -C $RepoRoot commit -m 'docs(changelog): refresh unreleased changes' -- CHANGELOG.md
        if ($LASTEXITCODE -ne 0) { throw 'Could not commit CHANGELOG.md.' }
        $commitHash = (& git -C $RepoRoot rev-parse --short HEAD).Trim()
        Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "Committed CHANGELOG.md ($commitHash)"
    } elseif ($diffExitCode -eq 0) {
        Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message 'CHANGELOG.md is already current; no commit was needed.'
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
