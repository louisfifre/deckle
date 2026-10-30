<#
.SYNOPSIS
    Mark a release
.DESCRIPTION
    Marks the version of HEAD as published with an annotated tag release/vX.Y.Z on the same commit. HEAD must carry a version tag. Preview by default; -Apply creates the tag. The tag is pushed only with -Push.
.NOTES
    section: Release
    order: 110
    action: Mark release | -Apply
#>

[CmdletBinding()]
param(
    # Root of the repository to work on; without it, the repository that
    # holds this script.
    [string]$Target,

    # Create the tag. Without it the script only shows what it would do.
    [switch]$Apply,

    # Also push the release tag to origin.
    [switch]$Push
)

$ErrorActionPreference = 'Stop'
$LibDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'version.ps1')
. (Join-Path $PSScriptRoot 'git-common.ps1')

$Workflow = 'Mark a release'
$RepoRoot = $null
$version = $null

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Marking a release failed before completion.' `
        -Details ([ordered]@{
            Repository = $RepoRoot
            Version    = $version
            Error      = $_.Exception.Message
        })
    throw
}

$RepoRoot = Get-TargetRoot -Target $Target -FallbackPath $PSScriptRoot
Write-OutputText -Text "Repo: $RepoRoot" -Role Muted
$output = New-WorkflowOutput -Category 'release'

Write-WorkflowStep -Output $output -Message 'Check the repository'

if (-not (Test-GitReference -Root $RepoRoot -Reference 'HEAD^{commit}')) {
    throw 'The repository has no commit yet. Make a commit and record a version before marking a release.'
}

# The version reader owns the error for a commit without a version, and its
# sentence already says how to record one.
$version = Get-RepositoryVersion -Path $RepoRoot
$tag = "release/v$version"
if (Test-GitReference -Root $RepoRoot -Reference "refs/tags/$tag") {
    throw "The tag $tag already exists: version $version is already marked as released."
}

# Checked before the tag exists, so a missing remote never leaves a tag behind.
if ($Push -and @(Invoke-Git -Root $RepoRoot -Arguments @('remote') -Failure 'git could not list the remotes.') -notcontains 'origin') {
    throw "-Push needs a remote named origin, and this repository has none. Add one with 'git remote add origin <url>'."
}

$tagCommand = "git tag -a $tag -m `"Release $version`""
$pushCommand = "git push origin $tag"

Write-WorkflowMessage -Output $output -Message "Version of HEAD: $version"

Write-WorkflowStep -Output $output -Message $(if ($Apply) { 'Mark the release' } else { 'Commands this would run' })
if (-not $Apply) {
    Write-WorkflowAction -Output $output -Message $tagCommand
    if ($Push) { Write-WorkflowAction -Output $output -Message $pushCommand }
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Skipped `
        -Sentence "Preview only: version $version would be marked as released with $tag. Nothing was changed." `
        -Details ([ordered]@{
            Version = $version
            Tag     = $tag
            Pushed  = 'no'
        }) `
        -Next @('Run again with -Apply to create the release tag.')
    return
}

$null = Invoke-Git -Root $RepoRoot -Arguments @('tag', '-a', $tag, '-m', "Release $version", 'HEAD') -Failure "git could not create the tag $tag."
Write-WorkflowResult -Output $output -Message "Marked $tag on HEAD"

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
            -Sentence "$tag was created locally but could not be pushed." `
            -Details ([ordered]@{
                Version = $version
                Tag     = $tag
                Pushed  = 'no'
                Error   = $_.Exception.Message
            }) `
            -Next @("Push it when the remote is reachable: $pushCommand")
        exit 1
    }
}

Write-ActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence "Version $version was marked as released with $tag." `
    -Details ([ordered]@{
        Version = $version
        Tag     = $tag
        Pushed  = $pushed
    }) `
    -Next $(if ($pushed -eq 'no') { @("Publish the mark when ready: $pushCommand") } else { @() })
