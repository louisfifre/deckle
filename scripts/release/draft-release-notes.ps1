<#
.SYNOPSIS
    Draft the public release notes
.DESCRIPTION
    Writes a draft of the public release notes of a version to artifacts/release-notes-<version>.md and prints its path. The context is already written in the history: the merge messages of main between the latest published version and the version ("Merge <branch>: <what it brings>"), plus the changelog lines scripts/git/changelog.ps1 -NotesFor prints. A small model, reached through the Claude Code CLI installed on this machine (headless, no tool allowed, the logged-in subscription, no API key), turns them into notes for a person who uses the app. What leaves the machine is that list of lines and the -AlsoSay sentence, nothing else. The script never publishes and publish-app.ps1 does not call it: read the draft before using it.
.NOTES
    order: 115
#>

[CmdletBinding()]
param(
    # The version the notes are for, with or without the leading v. Without
    # it, the version tag HEAD carries.
    [string]$Version,

    # The published version the notes start from. Without it, the latest
    # release/vX.Y.Z tag, the one publish-app.ps1 and changelog.ps1 read.
    [string]$Since,

    # A sentence of your own for what the history cannot know. Passed to the
    # model as a fact to put in the notes, in the right group.
    [string]$AlsoSay,

    # The model alias or name passed to the CLI: a small one is enough.
    [string]$Model = 'haiku',

    # Repository/worktree root. Defaults to the repo holding this script.
    [string]$Target,

    # The command that stands for the Claude Code CLI. Tests name a stub here
    # so that nothing is sent outside.
    [string]$ClaudeCommand = 'claude'
)

$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent $PSScriptRoot
$LibDir = Join-Path $ScriptsDir 'lib'
$GitDir = Join-Path $ScriptsDir 'git'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'version.ps1')
. (Join-Path $GitDir 'git-common.ps1')
Import-Module (Join-Path $LibDir 'release-validation.psm1') -Force

$Workflow = 'Draft the release notes'
$WorkflowOutput = New-WorkflowOutput -Category 'release-notes'
$RepoRoot = $null
$DraftPath = $null

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Drafting the release notes failed before completion.' `
        -Details ([ordered]@{
            Version = $Version
            Since   = $Since
            Draft   = $DraftPath
            Error   = $_.Exception.Message
        })
    throw
}

# The prompt is the only thing that is ever sent: the lines of the history, the
# sentence of -AlsoSay and the two versions. The placeholders are filled below.
$PromptTemplate = @'
You write the release notes of a desktop app, for the people who use it.

Below, between the markers, are the only facts you have: the version, the
version it follows, one line per change merged since (each says in plain words
what it brings), the changelog lines generated from the same history, and
sometimes a sentence from the maintainer for what the history cannot know.

Write the notes in English, in plain words, for a user of the app. Rules:
- Only what a user sees or uses. Leave out anything internal: tests, scripts,
  refactors, documentation, build, tooling, code names, file names.
- Nothing invented. Every note comes from the given lines or the maintainer's
  sentence. When a line is unclear for a user, leave it out rather than guess.
- Merge lines that say the same thing into one note. One short sentence each.
- Group the notes under these four headings, in this order, and leave a
  heading out when it has no note:
  ## What is new
  ## What changed
  ## What left
  ## Fixes
- The maintainer's sentence, when there is one, goes in the right group, as
  given in substance, never contradicted.
- Output the notes only: no title, no introduction, no closing remark, no
  comment about your work. Bullets start with "- ".

Version: {VERSION}
Follows: {SINCE}

Maintainer's sentence:
{ALSOSAY}

Merged changes, one per line:
{MERGES}

Changelog lines:
{CHANGELOG}
'@

function ConvertTo-VersionText([string]$Text, [string]$Label) {
    $value = $Text.Trim().TrimStart('v')
    if ($null -eq (ConvertTo-SemanticVersion -Text $value)) {
        throw "-$Label expects a version like 1.2.3, got '$Text'."
    }
    return $value
}

$RepoRoot = Get-TargetRoot -Target $Target -FallbackPath $PSScriptRoot
Write-OutputText -Text "Repo: $RepoRoot" -Role Muted

if (-not (Get-Command -Name $ClaudeCommand -ErrorAction SilentlyContinue)) {
    throw "The Claude Code CLI ('$ClaudeCommand') is not on PATH: install it and log in, then run this again."
}

# The version and the version it follows.
$versionText = if ($Version) { ConvertTo-VersionText $Version 'Version' } else { Get-RepositoryVersion -Path $RepoRoot }
$publishedTags = @(Get-DeckleReleaseTags -RepoRoot $RepoRoot)
$latestPublished = if ($publishedTags.Count -gt 0) { $publishedTags[-1].TrimStart('v') } else { $null }
$sinceText = if ($Since) { ConvertTo-VersionText $Since 'Since' } elseif ($latestPublished) { $latestPublished } else { throw 'No release/vX.Y.Z tag marks a published version: name the starting point with -Since.' }
if ([version]$versionText -le [version]$sinceText) {
    throw "Version ${versionText} does not come after ${sinceText}: there is nothing to draft."
}

$sinceRef = @("refs/tags/v$sinceText", "refs/tags/release/v$sinceText") |
    Where-Object { Test-GitReference -Root $RepoRoot -Reference $_ } | Select-Object -First 1
if (-not $sinceRef) { throw "No tag v$sinceText or release/v$sinceText in this repository." }
$endRef = if (Test-GitReference -Root $RepoRoot -Reference "refs/tags/v$versionText") { "refs/tags/v$versionText" } else { 'HEAD' }

# The merge messages of main: first-parent merges only, subjects only, so no
# body (conflict lists, trailers) travels.
Write-WorkflowStep -Output $WorkflowOutput -Message "Read the merges of main from v$sinceText to $(if ($endRef -eq 'HEAD') { 'HEAD' } else { "v$versionText" })"
$merges = @(Invoke-Git -Root $RepoRoot -Arguments @('log', '--first-parent', '--merges', '--reverse', '--format=%s', "$sinceRef..$endRef") -Failure 'git could not read the merges.' |
    ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($merges.Count -eq 0) { throw "No merge between v${sinceText} and v${versionText}: there is nothing to draft." }

# The changelog section runs from the latest published version, so it is the
# right one only when the notes start there too. Its heading carries the
# repository address and is left out: only the group names and the lines go.
$changelogLines = @()
if ($sinceText -eq $latestPublished) {
    Write-WorkflowStep -Output $WorkflowOutput -Message 'Read the changelog section'
    $tmp = [IO.Path]::GetTempFileName()
    try {
        & pwsh -NoProfile -File (Join-Path $GitDir 'changelog.ps1') -Target $RepoRoot -NotesFor $versionText -OutFile $tmp -Apply *> $null
        if ($LASTEXITCODE -ne 0) { throw 'changelog.ps1 could not print the section of the version.' }
        $changelogLines = @([IO.File]::ReadAllLines($tmp) | Where-Object { $_.Trim() -and $_ -notmatch '^## ' })
    } finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-WorkflowMessage -Output $WorkflowOutput -Role Warning `
        -Message "The changelog section starts at v$latestPublished, not v${sinceText}: only the merge messages are sent."
}

$prompt = $PromptTemplate.
    Replace('{VERSION}', $versionText).
    Replace('{SINCE}', $sinceText).
    Replace('{ALSOSAY}', $(if ($AlsoSay) { $AlsoSay.Trim() } else { '(none)' })).
    Replace('{MERGES}', ($merges -join "`n")).
    Replace('{CHANGELOG}', $(if ($changelogLines.Count -gt 0) { $changelogLines -join "`n" } else { '(none)' }))

# Headless, no tool, no session kept, no skill, no MCP server, no project file:
# the model sees the prompt and nothing else. The prompt goes in by standard
# input (it can pass the command-line limit), from a folder with nothing in it.
# No API key: the CLI uses the login it already has.
Write-WorkflowStep -Output $WorkflowOutput -Message "Ask the model ($Model) for the draft: $($merges.Count) merge line(s), $($changelogLines.Count) changelog line(s)"
$cliArguments = @(
    '-p'
    '--model', $Model
    '--tools', ''
    '--no-session-persistence'
    '--disable-slash-commands'
    '--strict-mcp-config'
    '--system-prompt', 'You write release notes from the facts you are given, and only from them.'
)
$emptyDir = Join-Path ([IO.Path]::GetTempPath()) "deckle-notes-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $emptyDir
Push-Location $emptyDir
try {
    $global:LASTEXITCODE = 0
    $reply = @($prompt | & $ClaudeCommand @cliArguments 2>&1 | ForEach-Object { [string]$_ })
    $exitCode = $LASTEXITCODE
} finally {
    Pop-Location
    Remove-Item -LiteralPath $emptyDir -Recurse -Force -ErrorAction SilentlyContinue
}
if ($exitCode -ne 0) {
    throw "The Claude Code CLI failed (exit code $exitCode): $((($reply | Select-Object -First 3) -join ' ').Trim())"
}
$draft = ($reply -join "`n").Trim()
if (-not $draft) { throw 'The Claude Code CLI returned no text.' }

$DraftPath = Join-Path $RepoRoot "artifacts\release-notes-$versionText.md"
$null = New-Item -ItemType Directory -Path (Split-Path -Parent $DraftPath) -Force
[IO.File]::WriteAllText($DraftPath, $draft + "`n", [Text.UTF8Encoding]::new($false))
Write-WorkflowResult -Output $WorkflowOutput -Message "Wrote $DraftPath"
Write-OutputText -Text $DraftPath

Write-ActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence "A draft of the notes of v$versionText was written; nothing was published." `
    -Details ([ordered]@{
        Version = "v$versionText"
        Since   = "v$sinceText"
        Merges  = [string]$merges.Count
        Draft   = $DraftPath
    }) `
    -Next @('Read the draft, correct it, then give it to publish-app.ps1 with -Notes.')
