<#
.SYNOPSIS
    Record a version
.DESCRIPTION
    Records the next version as an annotated tag vX.Y.Z on HEAD, bumping the latest version tag of the repository. Preview by default; -Apply creates the tag. The tag is pushed only with -Push.
.NOTES
    order: 100
    action: Record version | -Apply
#>

[CmdletBinding()]
param(
    # Which part of the latest version grows. The first version of a
    # repository is 0.1.0 whatever the bump.
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump = 'patch',

    # Root of the repository to work on; without it, the repository that
    # holds this script.
    [string]$Target,

    # Create the tag. Without it the script only shows what it would do.
    [switch]$Apply,

    # Also push the new tag to origin.
    [switch]$Push
)

$ErrorActionPreference = 'Stop'
$LibDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'version.ps1')
. (Join-Path $PSScriptRoot 'git-common.ps1')

$Workflow = 'Record a version'
$RepoRoot = $null
$from = $null
$to = $null

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Recording a version failed before completion.' `
        -Details ([ordered]@{
            Repository = $RepoRoot
            From       = $from
            To         = $to
            Error      = $_.Exception.Message
        })
    throw
}

# major resets minor and patch, minor resets patch: the usual reading of a
# semantic version.
function Get-NextVersion {
    param(
        [string]$Current,
        [Parameter(Mandatory)][string]$Bump
    )

    if (-not $Current) { return '0.1.0' }
    $version = [version]$Current
    switch ($Bump) {
        'major' { return "$($version.Major + 1).0.0" }
        'minor' { return "$($version.Major).$($version.Minor + 1).0" }
        default { return "$($version.Major).$($version.Minor).$($version.Build + 1)" }
    }
}

$RepoRoot = Get-TargetRoot -Target $Target -FallbackPath $PSScriptRoot
Write-OutputText -Text "Repo: $RepoRoot" -Role Muted
$output = New-WorkflowOutput -Category 'version'

Write-WorkflowStep -Output $output -Message 'Check the repository'

if (-not (Test-GitReference -Root $RepoRoot -Reference 'HEAD^{commit}')) {
    throw 'The repository has no commit yet. Make a commit before recording a version.'
}

# Untracked files are left out: a stray file cannot change what the tag names.
$dirty = @(Invoke-Git -Root $RepoRoot -Arguments @('status', '--porcelain', '--untracked-files=no') -Failure 'git could not read the state of the tracked files.')
if ($dirty.Count -gt 0) {
    throw "The tracked tree has uncommitted changes; commit or stash them first:`n$($dirty -join "`n")"
}

# A commit carries one version. The version reader throws when there is none,
# which is the case this script wants; any other error is a real failure.
$carried = $null
try {
    $carried = Get-RepositoryVersion -Path $RepoRoot
} catch {
    if ($_.Exception.Message -notmatch 'carries no version tag') { throw }
}
if ($carried) { throw "HEAD already carries v$carried. Make a new commit before recording another version." }

# Checked before the tag exists, so a missing remote never leaves a tag behind.
if ($Push -and @(Invoke-Git -Root $RepoRoot -Arguments @('remote') -Failure 'git could not list the remotes.') -notcontains 'origin') {
    throw "-Push needs a remote named origin, and this repository has none. Add one with 'git remote add origin <url>'."
}

$latest = Get-LatestRepositoryVersion -Path $RepoRoot
$from = if ($latest) { $latest } else { 'none' }
$to = Get-NextVersion -Current $latest -Bump $Bump
$tag = "v$to"
if (Test-GitReference -Root $RepoRoot -Reference "refs/tags/$tag") {
    throw "The tag $tag already exists on another commit."
}

$tagCommand = "git tag -a $tag -m `"Version $to`""
$pushCommand = "git push origin $tag"

Write-WorkflowMessage -Output $output -Message "Latest version: $from"
Write-WorkflowMessage -Output $output -Message "Next version:   $to ($Bump bump$(if (-not $latest) { ', first version of the repository' }))"

Write-WorkflowStep -Output $output -Message $(if ($Apply) { 'Record the version' } else { 'Commands this would run' })
if (-not $Apply) {
    Write-WorkflowAction -Output $output -Message $tagCommand
    if ($Push) { Write-WorkflowAction -Output $output -Message $pushCommand }
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Skipped `
        -Sentence "Preview only: $from -> $to would be recorded as $tag. Nothing was changed." `
        -Details ([ordered]@{
            From   = $from
            To     = $to
            Tag    = $tag
            Pushed = 'no'
        }) `
        -Next @('Run again with -Apply to record the tag.')
    return
}

$null = Invoke-Git -Root $RepoRoot -Arguments @('tag', '-a', $tag, '-m', "Version $to") -Failure "git could not create the tag $tag."
Write-WorkflowResult -Output $output -Message "Recorded $tag on HEAD"

$pushed = 'no'
if ($Push) {
    try {
        $null = Invoke-Git -Root $RepoRoot -Arguments @('push', 'origin', $tag) -Failure "git could not push $tag to origin."
        $pushed = 'yes'
        Write-WorkflowResult -Output $output -Message "Pushed $tag to origin"
    } catch {
        # The tag exists locally: report a partial result, not a failure that
        # would suggest nothing happened.
        Write-ActionSummary `
            -Workflow $Workflow `
            -Result Partial `
            -Sentence "$tag was recorded locally but could not be pushed." `
            -Details ([ordered]@{
                From   = $from
                To     = $to
                Tag    = $tag
                Pushed = 'no'
                Error  = $_.Exception.Message
            }) `
            -Next @("Push it when the remote is reachable: $pushCommand")
        exit 1
    }
}

Write-ActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence "Version $to was recorded as $tag." `
    -Details ([ordered]@{
        From   = $from
        To     = $to
        Tag    = $tag
        Pushed = $pushed
    }) `
    -Next $(if ($pushed -eq 'no') { @("Publish the tag when ready: $pushCommand") } else { @() })
