#!/usr/bin/env pwsh
#Requires -Version 7
<#
.SYNOPSIS
    Install local git hooks from scripts/hooks/.
    Run once after a clone: hook sources are versioned, installed hooks are not.
#>

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
. (Join-Path $scriptDir 'action-summary.ps1')

$repoRoot  = (git rev-parse --show-toplevel).Trim()
$hooksDir  = Join-Path (git -C $repoRoot rev-parse --absolute-git-dir).Trim() 'hooks'
$sourceDir = Join-Path $repoRoot 'scripts' 'hooks'

function Step($msg) { Write-Host "`n[hooks] $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "        $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "        $msg" -ForegroundColor Yellow }

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

Write-Host "Repo: $repoRoot" -ForegroundColor DarkGray
Write-Host "Hooks: $hooksDir" -ForegroundColor DarkGray

Step 'Install git hooks'
$hookFiles = @('pre-commit')
foreach ($hookFile in $hookFiles) {
    $src = Join-Path $sourceDir $hookFile
    if (-not (Test-Path -LiteralPath $src)) {
        throw "Hook source missing: $src"
    }

    $dst = Join-Path $hooksDir $hookFile
    if (Test-Path $dst) {
        Warn "Existing hook '$hookFile' backed up as '$hookFile.bak'"
        Copy-Item $dst "$dst.bak" -Force
    }
    Copy-Item $src $dst -Force
    Ok "Installed $hookFile"
    $InstalledHooks.Add($hookFile) | Out-Null
}

# Merge driver used by .gitattributes for TREE.md. `ours` keeps the local side
# so two branches never collide on the generated tree listing; the next commit
# that changes the file set regenerates it via the pre-commit hook. The driver
# definition lives in .git/config (not shared by clone), so it is set here.
Step "Register TREE.md merge driver"
git config merge.ours.driver true
Ok "Registered merge.ours driver"

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
