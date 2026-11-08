<#
.SYNOPSIS
    Update the prod copy
.DESCRIPTION
    Build main in Release in a build worktree of its own, check it with the
    self-check, and stage it in the prod copy, the Deckle the maintainer runs
    every day. The running Deckle is never stopped: the staged build takes its
    place at its next start.
.NOTES
    section: Run
    order: 130
    action: Update prod |
    action: Roll back prod | -Rollback
#>

# The prod copy (src/Deckle.App/ProdUpdate.cs says why this layout):
#   <Root>\current\           the build that runs; autostart points here
#   <Root>\next\              a complete build waiting for the next start
#   <Root>\previous\          the build current\ replaced
#   <Root>\apply-update.ps1   copied from the staged build's Prod\ folder
#
# Agents may run this script: it builds outside every checkout someone works
# in, takes a build slot through build-locked.ps1, and only ever writes next\
# while a Deckle runs. The first deploy, with no current\ yet, writes current\
# directly; starting it the first time stays the maintainer's act.
#
# Why build rather than publish: the prod copy is what the maintainer ran
# from the main checkout until now, a Release build, framework-dependent on the
# installed .NET runtime. Publishing a self-contained payload is the GitHub
# release's job (release/publish-app.ps1) and stays the maintainer's act.

[CmdletBinding()]
param(
    # What to deploy: a branch, a tag or a commit of the repository.
    [string]$Ref = 'main',
    # The prod copy root. By default prod\ in the primary checkout, ignored by
    # git: the prod copy is part of the project's development setup, not an
    # installed application. No build writes there, and clean.ps1 spares it:
    # it deletes only folders named as build outputs (bin, obj, artifacts...).
    # git clean -x would delete it, as it deletes every ignored folder.
    [string]$Root,
    # Stage previous\ as next\ instead of building: the next start rolls back.
    [switch]$Rollback,
    # Stage even when the commit is already the one in place or staged.
    [switch]$Force,
    [switch]$SkipSelfCheck
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
. (Join-Path $ScriptDir 'lib\action-summary.ps1')

$WorkflowOutput = New-WorkflowOutput -Category 'deploy'
$Workflow = 'Update prod'

# The primary checkout, whatever worktree this script runs from: the prod
# root and the build worktree are both derived from it.
$commonDir = (git -C $ScriptDir rev-parse --path-format=absolute --git-common-dir).Trim()
if ($LASTEXITCODE -ne 0) { throw "Not a git repository: $ScriptDir" }
$primary = Split-Path -Parent $commonDir
if (-not $Root) { $Root = Join-Path $primary 'prod' }

$current  = Join-Path $Root 'current'
$next     = Join-Path $Root 'next'
$previous = Join-Path $Root 'previous'
$staging  = Join-Path $Root 'next.staging'
$BuildTree = $null
$Commit = $null

trap {
    Write-ActionSummary -Workflow $Workflow -Result Failed `
        -Sentence 'The prod copy was not updated; the running Deckle is untouched.' `
        -Details ([ordered]@{ Root = $Root; Ref = $Ref; Commit = $Commit; BuildTree = $BuildTree; Error = $_.Exception.Message })
    throw
}

function Get-BuildCommit([string]$Folder) {
    # The informational version carries the commit: 0.96.0+<sha>.
    $exe = Join-Path $Folder 'Deckle.exe'
    if (-not (Test-Path -LiteralPath $exe)) { return $null }
    $version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
    if ($version -match '\+([0-9a-f]{40})') { return $Matches[1] }
    return $null
}

function Get-BuildVersion([string]$Folder) {
    $exe = Join-Path $Folder 'Deckle.exe'
    if (-not (Test-Path -LiteralPath $exe)) { return $null }
    return ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -split '\+')[0]
}

New-Item -ItemType Directory -Force -Path $Root | Out-Null

# The helper the staged build ships: the swap is run by the code of the build it
# installs, so a fix to it lands with that build.
function Copy-Helper([string]$Build) {
    $helper = Join-Path $Build 'Prod\apply-update.ps1'
    if (-not (Test-Path -LiteralPath $helper)) { throw "The build under $Build carries no $helper." }
    Copy-Item -LiteralPath $helper -Destination (Join-Path $Root 'apply-update.ps1') -Force
}

if ($Rollback) {
    $Workflow = 'Roll back prod'
    if (-not (Test-Path -LiteralPath (Join-Path $previous 'Deckle.exe'))) {
        throw "No previous build under $previous to roll back to."
    }
    if (Test-Path -LiteralPath $next) { Remove-Item -LiteralPath $next -Recurse -Force }
    Rename-Item -LiteralPath $previous -NewName 'next'
    Write-ActionSummary -Workflow $Workflow -Result Success `
        -Sentence 'The previous build is staged; Deckle runs it from its next start.' `
        -Details ([ordered]@{ Root = $Root; Staged = Get-BuildVersion $next; Running = Get-BuildVersion $current })
    return
}

# The build worktree: a detached checkout in the worktrees container, beside
# the primary checkout (AGENTS.md, Worktrees), reserved to this script.
Write-WorkflowStep -Output $WorkflowOutput -Message "Resolve $Ref"
$Commit = (git -C $primary rev-parse --verify "$Ref^{commit}").Trim()
if ($LASTEXITCODE -ne 0) { throw "Unknown ref: $Ref" }
$BuildTree = Join-Path (Split-Path -Parent (Split-Path -Parent $primary)) ('worktrees\' + (Split-Path -Leaf $primary) + '\prod-build')
Write-WorkflowMessage -Output $WorkflowOutput -Message "$Ref = $Commit"

if (-not $Force) {
    $place = if ((Get-BuildCommit $next) -eq $Commit) { 'staged' }
             elseif (-not (Test-Path -LiteralPath $next) -and (Get-BuildCommit $current) -eq $Commit) { 'running' }
    if ($place) {
        Write-ActionSummary -Workflow $Workflow -Result Skipped `
            -Sentence "This commit is already $place in the prod copy." `
            -Details ([ordered]@{ Root = $Root; Ref = $Ref; Commit = $Commit })
        return
    }
}

Write-WorkflowStep -Output $WorkflowOutput -Message "Check out $Commit in $BuildTree"
if (Test-Path -LiteralPath (Join-Path $BuildTree '.git')) {
    # Nobody edits this tree; --force discards whatever an interrupted run left.
    git -C $BuildTree checkout --quiet --force --detach $Commit
} else {
    git -C $primary worktree add --detach $BuildTree $Commit
}
if ($LASTEXITCODE -ne 0) { throw "git could not check out $Commit in $BuildTree" }

Write-WorkflowStep -Output $WorkflowOutput -Message 'Build Release'
& pwsh -NoProfile -File (Join-Path $ScriptDir 'build-locked.ps1') `
    -Project (Join-Path $BuildTree 'src\Deckle.App\Deckle.App.csproj') -Configuration Release /v:m /nologo
if ($LASTEXITCODE -ne 0) { throw "The Release build failed (exit code $LASTEXITCODE)." }

$output = Join-Path $BuildTree 'artifacts\bin\Deckle.App\release'
if ((Get-BuildCommit $output) -ne $Commit) { throw "The build output under $output is not commit $Commit." }

if (-not $SkipSelfCheck) {
    # A separate, reduced instance on its own data root: the resident Deckle
    # is left as it runs.
    Write-WorkflowStep -Output $WorkflowOutput -Message 'Self-check the build'
    & pwsh -NoProfile -File (Join-Path $ScriptDir 'self-check.ps1') -Configuration Release -Target $BuildTree
    if ($LASTEXITCODE -ne 0) { throw "The self-check of the Release build failed (exit code $LASTEXITCODE)." }
}

Write-WorkflowStep -Output $WorkflowOutput -Message "Stage into $Root"
Copy-Helper $output
# Written whole under a name no start looks at, then renamed: a start never
# sees half a build in next\.
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
& robocopy $output $staging /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed copying $output (exit code $LASTEXITCODE)." }

$firstDeploy = -not (Test-Path -LiteralPath (Join-Path $current 'Deckle.exe'))
if ($firstDeploy) {
    if (Test-Path -LiteralPath $current) { Remove-Item -LiteralPath $current -Recurse -Force }
    Rename-Item -LiteralPath $staging -NewName 'current'
    $sentence = 'The prod copy is installed; start current\Deckle.exe once to run it.'
} else {
    # A build staged earlier and never applied gives way to this one.
    if (Test-Path -LiteralPath $next) { Remove-Item -LiteralPath $next -Recurse -Force }
    Rename-Item -LiteralPath $staging -NewName 'next'
    $sentence = 'The build is staged; Deckle runs it from its next start.'
}

Write-ActionSummary -Workflow $Workflow -Result Success -Sentence $sentence `
    -Details ([ordered]@{
        Root    = $Root
        Version = Get-BuildVersion $output
        Commit  = $Commit
        Running = Get-BuildVersion $current
        Exe     = Join-Path $current 'Deckle.exe'
    })
