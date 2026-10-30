# cut-version.ps1 — Bump the single source-of-truth <Version> in
# Deckle.App.csproj and commit only that line as `chore(version): vX.Y.Z`.
# No push, no changelog, no tag: record-version.ps1 composes the internal
# record, and only publish-app.ps1 creates a tag, after a successful release.

[CmdletBinding()]
param(
    # SemVer segment to increment. patch (default) for a fix or small step,
    # minor for a real cycle (feature, engine change), major for an overhaul.
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump = 'patch',

    # Target worktree. Resolves like the other commands: -Target takes an
    # explicit path, otherwise the repo two levels up.
    [string]$Target,

    # Write the bumped <Version> WITHOUT committing — an escape
    # hatch to inspect the rewrite. The whole point is the atomic bump, so
    # this is off by default.
    [switch]$NoCommit
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')

$WorkflowOutput = New-WorkflowOutput -Category 'cut-version'

$Workflow = 'Update version'
$RepoRoot = $null
$current = $null
$tag = $null

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Version update failed before completion." `
        -Details ([ordered]@{
            Worktree = $RepoRoot
            Bump     = $Bump
            From     = $(if ($current) { "v$current" } else { $null })
            To       = $tag
            Error    = $_.Exception.Message
        })
    throw
}

if ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}
Write-OutputText -Text "Repo: $RepoRoot" -Role Muted

$Csproj = Join-Path $RepoRoot 'src\Deckle.App\Deckle.App.csproj'
if (-not (Test-Path $Csproj)) { throw "csproj not found at $Csproj — is '$RepoRoot' a Deckle worktree?" }

# A pending tracked change would ride into the version commit. Untracked files
# (scratch benches, in-progress notes) have nothing to do with a bump, hence
# --untracked-files=no.
Write-WorkflowStep -Output $WorkflowOutput -Message 'Check no tracked change is pending'
$dirty = & git -C $RepoRoot status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0) { throw "git status failed (code $LASTEXITCODE)" }
if ($dirty) {
    throw "Tracked changes are pending — commit or stash them first so the bump is the only change in its commit:`n$($dirty -join "`n")"
}
Write-WorkflowMessage -Output $WorkflowOutput -Message 'Clean (no tracked change pending)'

Write-WorkflowStep -Output $WorkflowOutput -Message 'Read current <Version>'
$content = Get-Content -Raw -LiteralPath $Csproj
$verRe = [regex]'<Version>([^<]+)</Version>'
$m = $verRe.Match($content)
if (-not $m.Success) { throw "<Version> not found in $Csproj" }
$current = $m.Groups[1].Value.Trim()
if ($current -notmatch '^\d+\.\d+\.\d+$') {
    throw "Current <Version> '$current' is not MAJOR.MINOR.PATCH — refuse to guess."
}
Write-WorkflowMessage -Output $WorkflowOutput -Message "current: v$current"

$p = $current.Split('.') | ForEach-Object { [int]$_ }
# Parenthesise each addition: PowerShell's comma operator binds TIGHTER than
# `+`, so `@($p[0], $p[1] + 1, 0)` would parse as `@(($p[0],$p[1]) + (1,0))` —
# array concatenation yielding a spurious 4th segment (0.5.0 → 0.5.1.0), never
# an increment. The parens force the arithmetic before the array is built.
switch ($Bump) {
    'major' { $p = @(($p[0] + 1), 0, 0) }
    'minor' { $p = @($p[0], ($p[1] + 1), 0) }
    'patch' { $p = @($p[0], $p[1], ($p[2] + 1)) }
}
$next = $p -join '.'
$tag  = "v$next"
Write-WorkflowStep -Output $WorkflowOutput -Message "Bump ($Bump): v$current -> $tag"

$existing = & git -C $RepoRoot tag --list $tag
if ($LASTEXITCODE -ne 0) { throw "git tag --list failed (code $LASTEXITCODE)" }
if ($existing) { throw "Tag $tag already exists — refuse to overwrite." }

# The csproj has no BOM: writing UTF-8 without one keeps the diff to the
# <Version> line.
$newContent = $verRe.Replace($content, "<Version>$next</Version>", 1)
[System.IO.File]::WriteAllText($Csproj, $newContent, [System.Text.UTF8Encoding]::new($false))
Write-WorkflowMessage -Output $WorkflowOutput -Message 'csproj written'

if ($NoCommit) {
    Write-WorkflowMessage -Output $WorkflowOutput -Message 'NoCommit: wrote the bump only. No commit, no tag.' -Role Warning
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Success `
        -Sentence "Deckle version was updated from v$current to $tag without committing or tagging." `
        -Details ([ordered]@{
            Worktree = $RepoRoot
            Bump     = $Bump
            From     = "v$current"
            To       = $tag
            Commit   = 'Skipped (-NoCommit)'
            Tag      = 'Skipped (-NoCommit)'
        }) `
        -Next @("Inspect the csproj diff before committing manually.")
    return
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'Commit version record'
& git -C $RepoRoot add -- $Csproj
if ($LASTEXITCODE -ne 0) { throw "git add failed (code $LASTEXITCODE)" }
& git -C $RepoRoot commit -m "chore(version): $tag"
if ($LASTEXITCODE -ne 0) { throw "git commit failed (code $LASTEXITCODE)" }
Write-WorkflowMessage -Output $WorkflowOutput -Message "committed chore(version): $tag"

Write-Host ''
Write-OutputText -Text "Cut v$current -> $tag on $RepoRoot"
Write-OutputText -Text 'Not pushed. No tag was created; the tag belongs to a successful public release.' -Role Muted

Write-ActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence "Deckle version was bumped from v$current to $tag and committed without creating a tag." `
    -Details ([ordered]@{
        Worktree = $RepoRoot
        Bump     = $Bump
        From     = "v$current"
        To       = $tag
        Commit   = "chore(version): $tag"
        Tag      = 'Not created'
        Pushed   = 'No'
    }) `
    -Next @(
        "git -C `"$RepoRoot`" push"
        "Menu: Release > Publish app release"
    )
