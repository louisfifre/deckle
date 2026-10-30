<#
.SYNOPSIS
    Install git hooks
.DESCRIPTION
    Installs the repository's git hooks from their versioned sources and
    registers the TREE.md merge driver. The hook sources are versioned with the
    repository, the installed hooks are not, so a fresh clone runs this once.
    An existing hook that differs is kept aside as a .bak file.
.NOTES
    section: Repository
    order: 110
    action: Preview hooks |
    action: Install git hooks | -Apply
#>

[CmdletBinding()]
param(
    [string]$Target,
    [switch]$Apply,
    [string[]]$Hooks = @('pre-commit', 'commit-msg')
)

$ErrorActionPreference = 'Stop'
$LibDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')

$Workflow = 'Install git hooks'
$KnownHooks = @('pre-commit', 'commit-msg')

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Installing the git hooks failed.' `
        -Details ([ordered]@{
            Error = $_.Exception.Message
        })
    throw
}

function Resolve-TargetRoot {
    param([string]$Path)

    if ($Path) {
        if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
            throw "Target '$Path' is not an existing folder."
        }
        return (Resolve-Path -LiteralPath $Path).Path
    }
    $top = & git -C $PSScriptRoot rev-parse --show-toplevel 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git could not find the repository root from '$PSScriptRoot'. Run the script inside a repository or pass -Target."
    }
    return [IO.Path]::GetFullPath(([string]$top).Trim())
}

# Hooks are shell scripts: a carriage return after the shebang makes the
# interpreter unfindable, so what lands in the hooks folder is always LF.
function Read-HookText {
    param([Parameter(Mandatory)][string]$Path)

    return [IO.File]::ReadAllText($Path) -replace "`r`n", "`n"
}

function Get-FreeBackupPath {
    param([Parameter(Mandatory)][string]$HookPath)

    $candidate = "$HookPath.bak"
    $number = 1
    while (Test-Path -LiteralPath $candidate) {
        $candidate = "$HookPath.bak.$number"
        $number++
    }
    return $candidate
}

$root = Resolve-TargetRoot -Path $Target

foreach ($hook in $Hooks) {
    if ($hook -notin $KnownHooks) {
        throw "Unknown hook '$hook'. Choose from: $($KnownHooks -join ', ')."
    }
}

$sourceDir = Join-Path $LibDir 'hooks'
foreach ($hook in $Hooks) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceDir $hook) -PathType Leaf)) {
        throw "The source of hook '$hook' is missing from '$sourceDir'."
    }
}

# --git-common-dir is the shared .git folder, which is where hooks live even
# when the target is a linked worktree.
$commonDir = & git -C $root rev-parse --path-format=absolute --git-common-dir 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "git could not locate the git folder of '$root': $commonDir"
}
$hooksDir = Join-Path ([IO.Path]::GetFullPath(([string]$commonDir).Trim())) 'hooks'

# A configured hooks path replaces .git/hooks, so hooks copied there would
# never run; saying so beats installing something silently inert.
$hooksPath = ((& git -C $root config --get core.hooksPath 2>$null) -join '').Trim()
if ($hooksPath) {
    throw "core.hooksPath is set to '$hooksPath', so hooks in '$hooksDir' would never run. Unset it or install the hooks there by hand."
}

$driverBefore = ((& git -C $root config --local --get merge.ours.driver 2>$null) -join '').Trim()
$driverSet = $driverBefore -eq 'true'

$output = New-WorkflowOutput -Category 'hooks'
Write-OutputText -Text "Repo: $root" -Role Muted
Write-OutputText -Text "Hooks: $hooksDir" -Role Muted
Write-WorkflowStep -Output $output -Message $(if ($Apply) { 'Install the hooks' } else { 'Plan the hooks' })

$installed = @()
$backedUp = @()
$current = @()
foreach ($hook in $Hooks) {
    $sourcePath = Join-Path $sourceDir $hook
    $hookPath = Join-Path $hooksDir $hook
    $sourceText = Read-HookText -Path $sourcePath
    $exists = Test-Path -LiteralPath $hookPath -PathType Leaf

    if ($exists -and (Read-HookText -Path $hookPath) -ceq $sourceText) {
        $current += $hook
        Write-WorkflowMessage -Output $output -Message "current  $hook" -Role Muted
        continue
    }

    $backupPath = if ($exists) { Get-FreeBackupPath -HookPath $hookPath } else { $null }
    if (-not $Apply) {
        Write-WorkflowAction -Output $output -Message "install  $hook"
        if ($exists) {
            Write-WorkflowMessage -Output $output -Message "an existing $hook would be kept as $(Split-Path -Leaf $backupPath)" -Role Warning
        }
    } else {
        $null = New-Item -ItemType Directory -Path $hooksDir -Force
        if ($exists) {
            Copy-Item -LiteralPath $hookPath -Destination $backupPath
            Write-WorkflowMessage -Output $output -Message "kept the existing $hook as $(Split-Path -Leaf $backupPath)" -Role Warning
        }
        [IO.File]::WriteAllText($hookPath, $sourceText, [Text.UTF8Encoding]::new($false))
        Write-WorkflowResult -Output $output -Message "installed $hook"
    }
    $installed += $hook
    if ($exists) { $backedUp += (Split-Path -Leaf $backupPath) }
}

Write-WorkflowStep -Output $output -Message 'Register the TREE.md merge driver'
if ($driverSet) {
    Write-WorkflowMessage -Output $output -Message 'merge.ours.driver is already true' -Role Muted
} elseif (-not $Apply) {
    Write-WorkflowAction -Output $output -Message 'merge.ours.driver would be set to true in the repository config'
} else {
    # Local scope only: the driver is a property of this repository, never of
    # the machine, so it never goes into the global config.
    & git -C $root config --local merge.ours.driver true
    if ($LASTEXITCODE -ne 0) { throw "git could not set merge.ours.driver in the repository config of '$root'." }
    Write-WorkflowResult -Output $output -Message 'merge.ours.driver set to true'
}

$driverState = if ($driverSet) { 'already set' } elseif ($Apply) { 'set' } else { 'would be set' }
$none = 'none'
$nothingToDo = $installed.Count -eq 0 -and $driverSet
$sentence = if ($nothingToDo) {
    'The hooks are already installed and up to date.'
} elseif ($Apply) {
    "Installed $($installed.Count) hook(s), $($current.Count) already current."
} else {
    "Preview: $($installed.Count) hook(s) would be installed, $($current.Count) already current."
}
$details = [ordered]@{}
$details['Hooks directory'] = $hooksDir
$details[$(if ($Apply) { 'Installed' } else { 'Would install' })] = if ($installed.Count -gt 0) { $installed } else { $none }
$details['Already current'] = if ($current.Count -gt 0) { $current } else { $null }
$details[$(if ($Apply) { 'Backed up' } else { 'Would back up' })] = if ($backedUp.Count -gt 0) { $backedUp } else { $none }
$details['Merge driver'] = $driverState
Write-ActionSummary `
    -Workflow $Workflow `
    -Result $(if ($nothingToDo) { 'Skipped' } else { 'Success' }) `
    -Sentence $sentence `
    -Details $details `
    -Next $(if (-not $Apply -and -not $nothingToDo) { @('Run "Install git hooks" to install them.') } else { @() })
