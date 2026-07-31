# record-version.ps1 - Record internal progress without publishing a release.
#
# A frequent version record bumps <Version>, commits that one-line change, and
# refreshes the generated [Unreleased] changelog accumulated since the latest
# public release. It creates no git tag and no GitHub Release. -Current only
# refreshes the accumulator for an already-recorded version.

[CmdletBinding(DefaultParameterSetName = 'Next')]
param(
    [Parameter(ParameterSetName = 'Next')]
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump = 'patch',

    [Parameter(ParameterSetName = 'Current')]
    [switch]$Current,

    [string]$Target,
    [switch]$Pick,

    # Pushes the current branch only. Public release tags are never pushed here.
    [switch]$Push
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')

$WorkflowOutput = New-DeckleWorkflowOutput -Category 'record-version'

$Workflow = 'Record version'
$RepoRoot = $null
$Version = $null
$Tag = $null
$Mode = $null
$ChangelogCommit = $null
$Pushed = $false

trap {
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Version recording failed before completion.' `
        -Details ([ordered]@{
            Worktree           = $RepoRoot
            Mode               = $Mode
            Version            = $Tag
            'Changelog commit' = $ChangelogCommit
            Pushed             = $(if ($Pushed) { 'Yes' } else { 'No' })
            Error              = $_.Exception.Message
        })
    throw
}

function Get-AppVersion([string]$Root) {
    $csproj = Join-Path $Root 'src\Deckle.App\Deckle.App.csproj'
    if (-not (Test-Path $csproj)) { throw "csproj not found at $csproj - is '$Root' a Deckle worktree?" }
    $m = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if (-not $m) { throw "<Version> not found in $csproj" }
    $v = $m.Matches[0].Groups[1].Value.Trim()
    if ($v -notmatch '^\d+\.\d+\.\d+$') { throw "Current <Version> '$v' is not MAJOR.MINOR.PATCH - refuse to guess." }
    return $v
}

function Assert-NoTrackedChanges([string]$Root) {
    $dirty = & git -C $Root status --porcelain --untracked-files=no
    if ($LASTEXITCODE -ne 0) { throw "git status failed (code $LASTEXITCODE)" }
    if ($dirty) {
        throw "Tracked changes are pending - commit or stash them first:`n$($dirty -join "`n")"
    }
}

if ($Pick) {
    Import-Module (Join-Path $LibDir 'menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}
Write-DeckleOutputText -Text "Repo: $RepoRoot" -Role Muted

Assert-NoTrackedChanges $RepoRoot

if ($Current) {
    $Mode = 'Refresh current'
    $Version = Get-AppVersion $RepoRoot
    $Tag = "v$Version"
    Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Refresh Unreleased for $Tag"
} else {
    $Mode = "Bump $Bump"
    Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Record next internal version ($Bump)"
    & (Join-Path $ScriptDir 'cut-version.ps1') -Target $RepoRoot -Bump $Bump
    if (-not $?) { throw 'cut-version.ps1 failed' }
    $Version = Get-AppVersion $RepoRoot
    $Tag = "v$Version"
}

Write-DeckleWorkflowStep -Output $WorkflowOutput -Message 'Regenerate the Unreleased accumulator'
& (Join-Path $ScriptDir 'changelog.ps1') -Target $RepoRoot
if (-not $?) { throw 'changelog.ps1 failed' }

& git -C $RepoRoot add -- CHANGELOG.md
if ($LASTEXITCODE -ne 0) { throw "git add CHANGELOG.md failed (code $LASTEXITCODE)" }
& git -C $RepoRoot diff --cached --quiet -- CHANGELOG.md
$diffCode = $LASTEXITCODE
if ($diffCode -eq 1) {
    & git -C $RepoRoot commit -m 'docs(changelog): refresh unreleased changes'
    if ($LASTEXITCODE -ne 0) { throw "changelog refresh commit failed (code $LASTEXITCODE)" }
    $ChangelogCommit = 'docs(changelog): refresh unreleased changes'
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message $ChangelogCommit
} elseif ($diffCode -eq 0) {
    $ChangelogCommit = 'Unchanged'
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message 'CHANGELOG.md already matches the public-release range'
} else {
    throw "git diff --cached failed (code $diffCode)"
}

if ($Push) {
    Write-DeckleWorkflowStep -Output $WorkflowOutput -Message 'Push current branch'
    & git -C $RepoRoot push
    if ($LASTEXITCODE -ne 0) { throw "git push failed (code $LASTEXITCODE)" }
    $Pushed = $true
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message 'Branch pushed; no tag created'
}

$sentence = if ($Pushed) {
    "Deckle $Tag was recorded and pushed without creating a release tag."
} else {
    "Deckle $Tag was recorded locally without creating a release tag."
}

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence $sentence `
    -Details ([ordered]@{
        Worktree           = $RepoRoot
        Version            = $Tag
        Mode               = $Mode
        'Changelog commit' = $ChangelogCommit
        Tag                = 'Not created'
        Pushed             = $(if ($Pushed) { 'Yes' } else { 'No' })
    }) `
    -Next $(if ($Pushed) {
        @('Publish a GitHub Release later when this version should become downloadable.')
    } else {
        @(
            "git -C `"$RepoRoot`" push"
            'Publish a GitHub Release later when this version should become downloadable.'
        )
    })
