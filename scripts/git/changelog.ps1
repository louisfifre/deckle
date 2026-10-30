<#
.SYNOPSIS
    Update the changelog
.DESCRIPTION
    Generates CHANGELOG.md in the Keep a Changelog format from the Conventional Commits history, one section per published version. No external tool and no network: plain git log and PowerShell. Preview prints the document; -Apply writes it.
.NOTES
    section: Changelog
    order: 120
    action: Preview changelog |
    action: Write changelog | -Apply
#>

[CmdletBinding()]
param(
    # Root of the repository to work on; without it, the repository that
    # holds this script.
    [string]$Target,

    # Write the document. Without it the script only prints it.
    [switch]$Apply,

    # Release-notes mode: only this version's section (for example "0.4.5"),
    # without the document header. A version that is not published yet is
    # rendered as in progress: the commits since the latest published version,
    # dated today.
    [string]$NotesFor,

    # Destination of the document instead of CHANGELOG.md. It only moves the
    # destination: writing still needs -Apply.
    [string]$OutFile,

    # Lowest version rendered in full; older sections are left out.
    [string]$Floor
)

$ErrorActionPreference = 'Stop'
$LibDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'version.ps1')
. (Join-Path $PSScriptRoot 'git-common.ps1')

$Workflow = 'Update the changelog'
$RepoRoot = $null
$dest = $null

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Updating the changelog failed before completion.' `
        -Details ([ordered]@{
            Repository = $RepoRoot
            Version    = $NotesFor
            Output     = $dest
            Error      = $_.Exception.Message
        })
    throw
}

# A type absent from this map is dropped (chore, docs, test, ci, build, style,
# merges, non-conventional subjects): no catch-all, so noise never reaches
# Changed. The breaking marker `!` outranks the map: it is the author's word
# that the user loses something, so such a subject lands under Removed
# whatever its type. A revert stays a Removed unless it undoes a commit of the
# same range (see Get-GroupedEntries).
$TypeToGroup = [ordered]@{
    feat     = 'Added'
    fix      = 'Fixed'
    perf     = 'Changed'
    refactor = 'Changed'
    revert   = 'Removed'
}
# Keep a Changelog order.
$GroupOrder = @('Added', 'Changed', 'Deprecated', 'Removed', 'Fixed', 'Security')
$EmDash = [string][char]0x2014

function Get-FirstUpper([string]$Text) {
    if ([string]::IsNullOrEmpty($Text)) { return $Text }
    return [char]::ToUpper($Text[0]) + $Text.Substring(1)
}

# The sections are cut at the published versions. A release/vX.Y.Z tag says
# "published" without ambiguity; version tags alone also exist for internal
# versions, so they serve only while no release tag does.
function Get-Boundaries {
    $releaseTags = @(Invoke-Git -Root $RepoRoot -Arguments @('tag', '--list', 'release/v*') -Failure 'git could not list the release tags.')
    $releases = @(foreach ($tag in $releaseTags) {
        $version = ConvertTo-SemanticVersion -Text ($tag -replace '^release/', '')
        if ($null -ne $version) { [pscustomobject]@{ Tag = $tag; Version = $version } }
    })
    if ($releases.Count -gt 0) {
        return [pscustomobject]@{ Kind = 'release tags'; Prefix = 'release/v'; Items = @($releases | Sort-Object -Property Version) }
    }
    $versions = @(Get-RepositoryVersionTags -Path $RepoRoot)
    if ($versions.Count -gt 0) {
        return [pscustomobject]@{ Kind = 'version tags'; Prefix = 'v'; Items = $versions }
    }
    return [pscustomobject]@{ Kind = 'none'; Prefix = 'v'; Items = @() }
}

# Only GitHub gets compare links: its URL scheme is known, any other host is
# left with plain headings rather than a guessed link.
function Get-GitHubUrl {
    $url = (& git -C $RepoRoot remote get-url origin 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $url) { return $null }
    $url = ([string]$url).Trim()
    if ($url -match '^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$' -or
        $url -match '^git@github\.com:(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$') {
        return "https://github.com/$($Matches.owner)/$($Matches.repo)"
    }
    return $null
}

function Get-TagDate([string]$Tag) {
    # @() because a one-line result comes back as a string, not an array.
    $date = @(Invoke-Git -Root $RepoRoot -Arguments @('log', '-1', '--format=%cs', "$Tag^{commit}") -Failure "git could not read the date of $Tag.")
    return $date[0].Trim()
}

# One record per commit, oldest first: hash, subject, and the hash a revert
# points at. The body is read for git's own "This reverts commit <sha>" line
# alone and discarded right here, so no sentence from it, trailers included,
# can leak into the changelog.
function Get-RangeCommits([string]$Range) {
    $commits = [System.Collections.Generic.List[hashtable]]::new()
    if ($script:HasCommits) {
        $format = '%H%x1f%s%x1f%b%x1e'                          # hash, US, subject, US, body, RS
        $text = (Invoke-Git -Root $RepoRoot -Arguments @('log', '--reverse', "--format=$format", $Range) -Failure "git could not read the history of $Range.") -join "`n"
        foreach ($record in ($text -split ([char]0x1e))) {
            $parts = $record -split ([char]0x1f), 3
            if ($parts.Count -lt 2) { continue }
            $subject = $parts[1].Trim("`r", "`n", ' ')
            if (-not $subject) { continue }
            $reverts = $null
            if ($parts.Count -eq 3 -and $parts[2] -match 'This reverts commit (?<sha>[0-9a-f]{7,40})') {
                $reverts = $Matches.sha
            }
            $commits.Add(@{
                Hash    = $parts[0].Trim("`r", "`n", ' ')
                Subject = $subject
                Reverts = $reverts
            })
        }
    }
    # Comma stops pipeline unrolling, so an empty range yields a real empty
    # array (not $null): the contract is "an array of records", always.
    return , $commits.ToArray()
}

function ConvertTo-Entry([hashtable]$Commit) {
    # Case-sensitive: "Merge ..." and git's "Revert ..." start with a capital
    # and must NOT match the lowercase type token, so they fall through to
    # dropped.
    if ($Commit.Subject -cmatch '^(?<type>[a-z]+)(?:\((?<scope>[^)]+)\))?(?<bang>!)?:\s+(?<desc>.+)$') {
        $type = $Matches.type
        $scope = $Matches.scope
        $group = if ($Matches.bang) { 'Removed' } else { $TypeToGroup[$type] }
        if (-not $group) { return $null }                        # skipped or unmapped type

        $description = Get-FirstUpper (($Matches.desc -split "`n")[0].Trim())
        $line = if ($scope) { "- **${scope}:** $description" } else { "- $description" }
        return @{ Group = $group; Line = $line; Type = $type }
    }
    return $null
}

# Entries of one range, keyed by group, once the reverts are settled. A revert
# pointing at a commit of the same range undoes something the user never saw,
# so both lines go. A revert pointing outside the range stays a Removed: the
# user did see the thing. A revert without the pointer also stays a Removed,
# and is reported so the author can add git's line to the body.
function Get-GroupedEntries([hashtable[]]$Commits) {
    $cancelled = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($commit in $Commits) {
        if (-not $commit.Reverts) { continue }
        $origin = $Commits | Where-Object { $_.Hash.StartsWith($commit.Reverts) } | Select-Object -First 1
        if ($origin) {
            $null = $cancelled.Add($commit.Hash)
            $null = $cancelled.Add($origin.Hash)
        }
    }

    $byGroup = @{}
    foreach ($commit in $Commits) {
        if ($cancelled.Contains($commit.Hash)) { continue }
        $entry = ConvertTo-Entry $commit
        if ($null -eq $entry) { continue }
        if ($entry.Type -eq 'revert' -and -not $commit.Reverts) {
            Write-WorkflowMessage -Output $script:Output -Role Warning `
                -Message "Revert without a 'This reverts commit' line, listed under Removed: $($commit.Subject)"
        }
        if (-not $byGroup.ContainsKey($entry.Group)) { $byGroup[$entry.Group] = [System.Collections.Generic.List[string]]::new() }
        $byGroup[$entry.Group].Add($entry.Line)
    }
    return $byGroup
}

# $PreviousTag and $ThisTag are full tag names (with their v or release/v);
# a null $Version is the Unreleased section.
function Format-Section([string]$Version, [string]$PreviousTag, [string]$ThisTag, [string]$Date, [hashtable[]]$Commits) {
    $byGroup = Get-GroupedEntries $Commits

    $heading = if (-not $Version) {
        '## [Unreleased]'
    } elseif ($script:GitHubUrl -and $PreviousTag) {
        "## [$Version]($script:GitHubUrl/compare/$PreviousTag...$ThisTag) $EmDash $Date"
    } elseif ($script:GitHubUrl) {
        "## [$Version]($script:GitHubUrl/releases/tag/$ThisTag) $EmDash $Date"
    } else {
        "## [$Version] $EmDash $Date"
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add($heading)
    foreach ($group in $GroupOrder) {
        if ($byGroup.ContainsKey($group)) {
            $lines.Add('')
            $lines.Add("### $group")
            $lines.Add('')
            foreach ($line in $byGroup[$group]) { $lines.Add($line) }
        }
    }
    return ($lines -join "`n")
}

function Write-DocumentLines([string]$Text) {
    foreach ($line in ($Text.TrimEnd("`n") -split "`n")) {
        Write-OutputText -Text $line
    }
}

if ($NotesFor) {
    if ($NotesFor.TrimStart('v') -notmatch '^\d+\.\d+\.\d+$') {
        throw "-NotesFor expects a version like 1.2.3, got '$NotesFor'."
    }
}
if ($Floor) {
    if ($Floor.TrimStart('v') -notmatch '^\d+\.\d+\.\d+$') {
        throw "-Floor expects a version like 1.2.3, got '$Floor'."
    }
}

$RepoRoot = Get-TargetRoot -Target $Target -FallbackPath $PSScriptRoot
Write-OutputText -Text "Repo: $RepoRoot" -Role Muted
$Output = New-WorkflowOutput -Category 'changelog'
$HasCommits = Test-GitReference -Root $RepoRoot -Reference 'HEAD^{commit}'
$GitHubUrl = Get-GitHubUrl

$boundaries = Get-Boundaries
$items = @($boundaries.Items)
Write-WorkflowStep -Output $Output -Message "Read the history (boundaries: $($boundaries.Kind), $($items.Count))"
if ($items.Count -eq 0) {
    Write-WorkflowMessage -Output $Output -Role Muted -Message 'No published version yet: every change is listed as Unreleased.'
}

if ($NotesFor) {
    $version = $NotesFor.TrimStart('v')
    $index = -1
    for ($i = 0; $i -lt $items.Count; $i++) {
        if ($items[$i].Version -eq [version]$version) { $index = $i }
    }

    if ($index -ge 0) {
        $thisTag = $items[$index].Tag
        $previousTag = if ($index -gt 0) { $items[$index - 1].Tag } else { $null }
        $range = if ($previousTag) { "$previousTag..$thisTag" } else { $thisTag }
        $date = Get-TagDate $thisTag
    } else {
        # Not published yet: the notes cover what a publication would carry,
        # up to the version tag when it exists and to HEAD otherwise.
        $thisTag = "$($boundaries.Prefix)$version"
        $previousTag = if ($items.Count -gt 0) { $items[-1].Tag } else { $null }
        $endpoint = if (Test-GitReference -Root $RepoRoot -Reference "refs/tags/$thisTag") { $thisTag } else { 'HEAD' }
        $range = if ($previousTag) { "$previousTag..$endpoint" } else { $endpoint }
        $date = Get-Date -Format 'yyyy-MM-dd'
    }
    Write-WorkflowMessage -Output $Output -Message "Release notes for $version (range: $range)"
    $commits = Get-RangeCommits $range
    $section = Format-Section $version $previousTag $thisTag $date $commits
    if ($section -notmatch '### ') {
        Write-WorkflowMessage -Output $Output -Role Warning -Message "No user-facing commits in $range; the notes are just a heading."
    }
    $text = $section + "`n"
    $sectionCount = 1
} else {
    # Sections are cut between consecutive boundaries of the whole history,
    # so a floor hides old sections without moving the range of the first
    # one shown.
    $floorVersion = if ($Floor) { [version]$Floor.TrimStart('v') } else { $null }
    $sections = [System.Collections.Generic.List[string]]::new()

    # [Unreleased] appears only when a line survives: an empty heading would be
    # noise, and a feature reverted before release leaves no line at all.
    $range = if ($items.Count -gt 0) { "$($items[-1].Tag)..HEAD" } else { 'HEAD' }
    $unreleased = Get-RangeCommits $range
    if ($unreleased.Count -gt 0) {
        $section = Format-Section $null $null $null '' $unreleased
        if ($section -match '### ') {
            $sections.Add($section)
            Write-WorkflowMessage -Output $Output -Message "Unreleased: $($unreleased.Count) commit(s) since $(if ($items.Count -gt 0) { $items[-1].Tag } else { 'the start' })"
        }
    }

    for ($i = $items.Count - 1; $i -ge 0; $i--) {
        $item = $items[$i]
        if ($null -ne $floorVersion -and $item.Version -lt $floorVersion) { continue }
        $previousTag = if ($i -gt 0) { $items[$i - 1].Tag } else { $null }
        $range = if ($previousTag) { "$previousTag..$($item.Tag)" } else { $item.Tag }
        $commits = Get-RangeCommits $range
        $sections.Add((Format-Section ([string]$item.Version) $previousTag $item.Tag (Get-TagDate $item.Tag) $commits))
        Write-WorkflowMessage -Output $Output -Message "$($item.Version): $($commits.Count) commit(s) in range"
    }

    $header = @(
        '# Changelog'
        ''
        "All notable changes to $(Split-Path -Leaf $RepoRoot) are recorded here."
        ''
        'The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and'
        'the project adheres to [Semantic Versioning](https://semver.org/).'
        ''
        'This file is generated from the Conventional Commits history by `scripts/git/changelog.ps1`; do not edit it by hand.'
    ) -join "`n"
    $text = $header
    if ($sections.Count -gt 0) { $text += "`n`n" + ($sections -join "`n`n") }
    $text += "`n"
    $sectionCount = $sections.Count
}

# The notes go to stdout unless a destination is named; the full document
# always has one.
$dest = if ($OutFile) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)
} elseif (-not $NotesFor) {
    Join-Path $RepoRoot 'CHANGELOG.md'
} else {
    $null
}

$details = [ordered]@{
    Boundaries = if ($items.Count -gt 0) { "$($boundaries.Kind) ($($items.Count))" } else { 'none' }
    Sections   = [string]$sectionCount
    Output     = if ($dest) { $dest } else { 'stdout' }
}

if (-not $dest) {
    Write-WorkflowStep -Output $Output -Message 'Print the release notes'
    Write-DocumentLines $text
    Write-ActionSummary -Workflow $Workflow -Result Success -Sentence "The release notes of $version were printed." -Details $details
    return
}

$existing = if (Test-Path -LiteralPath $dest -PathType Leaf) { [IO.File]::ReadAllText($dest) } else { $null }
$changed = ($existing -cne $text)
$details['Changed'] = if ($changed) { 'yes' } else { 'no' }
$fileName = Split-Path -Leaf $dest

if (-not $Apply) {
    Write-WorkflowStep -Output $Output -Message 'Preview of the document'
    Write-DocumentLines $text
    Write-WorkflowStep -Output $Output -Message 'Effect'
    $effect = if ($null -eq $existing) { "$fileName does not exist and would be created." }
    elseif ($changed) { "$fileName would change." }
    else { "$fileName is already current." }
    Write-WorkflowMessage -Output $Output -Message $effect
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Success `
        -Sentence "Preview only: $effect Nothing was written." `
        -Details $details `
        -Next $(if ($changed) { @('Run again with -Apply to write it.') } else { @() })
    return
}

Write-WorkflowStep -Output $Output -Message 'Write the document'
if ($changed) {
    $parent = Split-Path -Parent $dest
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw "The folder of the destination does not exist: $parent"
    }
    [IO.File]::WriteAllText($dest, $text, [Text.UTF8Encoding]::new($false))
    Write-WorkflowResult -Output $Output -Message "Wrote $dest"
} else {
    Write-WorkflowMessage -Output $Output -Role Muted -Message "$fileName is already current; nothing was written."
}
Write-ActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence $(if ($changed) { "$fileName was written." } else { "$fileName was already current." }) `
    -Details $details
