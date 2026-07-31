#!/usr/bin/env pwsh
#Requires -Version 7
<#
.SYNOPSIS
    Install local git hooks from scripts/hooks/.
    Run once after a clone: hook sources are versioned, installed hooks are not.
#>

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
$libDir = Join-Path (Split-Path -Parent $scriptDir) 'lib'
. (Join-Path $libDir 'action-summary.ps1')

$repoRoot  = (git rev-parse --show-toplevel).Trim()
$gitCommonDir = (git -C $repoRoot rev-parse --path-format=absolute --git-common-dir).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitCommonDir)) {
    throw 'Could not resolve the shared Git directory.'
}
$hooksDir  = Join-Path $gitCommonDir 'hooks'
$sourceDir = Join-Path $repoRoot 'scripts' 'hooks'

$WorkflowOutput = New-DeckleWorkflowOutput -Category 'hooks'

$Workflow = 'Install git hooks'
$InstalledHooks = New-Object System.Collections.Generic.List[string]

trap {
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Git hook installation failed before completion." `
        -Details ([ordered]@{
            Worktree          = $repoRoot
            'Hooks directory' = $hooksDir
            Installed         = ($InstalledHooks.ToArray() -join ', ')
            Error             = $_.Exception.Message
        })
    throw
}

Write-DeckleOutputText -Text "Repo: $repoRoot" -Role Muted
Write-DeckleOutputText -Text "Hooks: $hooksDir" -Role Muted

Write-DeckleWorkflowStep -Output $WorkflowOutput -Message 'Install git hooks'
$hookFiles = @('pre-commit')
foreach ($hookFile in $hookFiles) {
    $src = Join-Path $sourceDir $hookFile
    if (-not (Test-Path -LiteralPath $src)) {
        throw "Hook source missing: $src"
    }

    $dst = Join-Path $hooksDir $hookFile
    if (Test-Path $dst) {
        Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "Existing hook '$hookFile' backed up as '$hookFile.bak'" -Role Warning
        Copy-Item $dst "$dst.bak" -Force
    }
    Copy-Item $src $dst -Force
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "Installed $hookFile"
    $InstalledHooks.Add($hookFile) | Out-Null
}

# Merge driver used by .gitattributes for TREE.md. `ours` keeps the local side
# so two branches never collide on the generated tree listing; the next commit
# that changes the file set regenerates it via the pre-commit hook. The driver
# definition lives in .git/config (not shared by clone), so it is set here.
Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Register TREE.md merge driver"
git config merge.ours.driver true
Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "Registered merge.ours driver"

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence "Deckle git hooks were installed and the TREE.md merge driver was registered." `
    -Details ([ordered]@{
        Worktree          = $repoRoot
        'Hooks directory' = $hooksDir
        Installed         = ($InstalledHooks.ToArray() -join ', ')
        'Merge driver'    = 'merge.ours.driver=true'
    })
