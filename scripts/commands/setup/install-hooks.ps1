<#
.SYNOPSIS
    Install git hooks
.DESCRIPTION
    Install Deckle's repository and global git hooks. Run once after a clone:
    hook sources are versioned, installed hooks are not.
.NOTES
    section: Repo
    order: 210
#>

#Requires -Version 7

[CmdletBinding()]
param(
    [string]$GlobalHookDirectory = (Join-Path $env:LOCALAPPDATA 'Deckle\GitHooks')
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
$libDir = Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDir)) 'lib'
. (Join-Path $libDir 'action-summary.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $scriptDir))
$gitCommonDir = (git -C $repoRoot rev-parse --path-format=absolute --git-common-dir).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitCommonDir)) {
    throw 'Could not resolve the shared Git directory.'
}
$hooksDir  = Join-Path $gitCommonDir 'hooks'
$sourceDir = Join-Path $repoRoot 'scripts' 'hooks'

$WorkflowOutput = New-WorkflowOutput -Category 'hooks'

$Workflow = 'Install git hooks'
$InstalledHooks = New-Object System.Collections.Generic.List[string]
$GlobalHookName = 'deckle-commit-attribution'
$GlobalHookSource = Join-Path $sourceDir 'validate-commit-attribution.ps1'
$GlobalHookPath = Join-Path $GlobalHookDirectory 'validate-commit-attribution.ps1'

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Git hook installation failed before completion." `
        -Details ([ordered]@{
            Worktree          = $repoRoot
            'Hooks directory' = $hooksDir
            'Global hook'     = $GlobalHookPath
            Installed         = ($InstalledHooks.ToArray() -join ', ')
            Error             = $_.Exception.Message
        })
    throw
}

Write-OutputText -Text "Repo: $repoRoot" -Role Muted
Write-OutputText -Text "Hooks: $hooksDir" -Role Muted
Write-OutputText -Text "Global hook: $GlobalHookPath" -Role Muted

Write-WorkflowStep -Output $WorkflowOutput -Message 'Install git hooks'
$hookFiles = @('pre-commit')
foreach ($hookFile in $hookFiles) {
    $src = Join-Path $sourceDir $hookFile
    if (-not (Test-Path -LiteralPath $src)) {
        throw "Hook source missing: $src"
    }

    $dst = Join-Path $hooksDir $hookFile
    if (Test-Path $dst) {
        Write-WorkflowMessage -Output $WorkflowOutput -Message "Existing hook '$hookFile' backed up as '$hookFile.bak'" -Role Warning
        Copy-Item $dst "$dst.bak" -Force
    }
    Copy-Item $src $dst -Force
    Write-WorkflowMessage -Output $WorkflowOutput -Message "Installed $hookFile"
    $InstalledHooks.Add($hookFile) | Out-Null
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'Install global commit-attribution guard'
$gitVersionText = (& git --version) -replace '^git version\s+', ''
if ($LASTEXITCODE -ne 0 -or $gitVersionText -notmatch '^(\d+)\.(\d+)') {
    throw 'Could not determine the installed Git version.'
}
$gitVersion = [version]::new([int]$Matches[1], [int]$Matches[2])
if ($gitVersion -lt [version]'2.54') {
    throw "Git 2.54 or newer is required for configured hooks; found $gitVersionText."
}
if (-not (Test-Path -LiteralPath $GlobalHookSource)) {
    throw "Global hook source missing: $GlobalHookSource"
}

$null = New-Item -ItemType Directory -Path $GlobalHookDirectory -Force
Copy-Item -LiteralPath $GlobalHookSource -Destination $GlobalHookPath -Force
$commandPath = $GlobalHookPath.Replace('\', '/') -replace "'", "'`"'`"'"
$hookCommand = "pwsh -NoLogo -NoProfile -NonInteractive -File '$commandPath'"

git config --global "hook.$GlobalHookName.command" $hookCommand
if ($LASTEXITCODE -ne 0) { throw 'Could not register the global commit-attribution hook command.' }
git config --global --replace-all "hook.$GlobalHookName.event" commit-msg
if ($LASTEXITCODE -ne 0) { throw 'Could not register the global commit-attribution hook event.' }
git config --global "hook.$GlobalHookName.enabled" true
if ($LASTEXITCODE -ne 0) { throw 'Could not enable the global commit-attribution hook.' }
Write-WorkflowMessage -Output $WorkflowOutput -Message 'Installed the global commit-attribution guard'
$InstalledHooks.Add('commit-msg (global)') | Out-Null

# Merge driver used by .gitattributes for TREE.md. `ours` keeps the local side
# so two branches never collide on the generated tree listing; the next commit
# that changes the file set regenerates it via the pre-commit hook. The driver
# definition lives in .git/config (not shared by clone), so it is set here.
Write-WorkflowStep -Output $WorkflowOutput -Message "Register TREE.md merge driver"
git -C $repoRoot config merge.ours.driver true
if ($LASTEXITCODE -ne 0) { throw "Could not register the TREE.md merge driver." }
Write-WorkflowMessage -Output $WorkflowOutput -Message "Registered merge.ours driver"

Write-ActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence "Deckle git hooks and the global commit-attribution guard were installed." `
    -Details ([ordered]@{
        Worktree          = $repoRoot
        'Hooks directory' = $hooksDir
        'Global hook'     = $GlobalHookPath
        Installed         = ($InstalledHooks.ToArray() -join ', ')
        'Merge driver'    = 'merge.ours.driver=true'
    })
