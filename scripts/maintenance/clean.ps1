<#
.SYNOPSIS
    Clean build outputs
.DESCRIPTION
    Removes the build output folders of every language present in the
    repository, each only when it passes three locks: it is a known output
    folder of a language the repository uses, git ignores it, and it holds no
    tracked file. Without the apply switch it only lists what it would remove
    and what it keeps.
.NOTES
    section: Cleanup
    order: 200
    action: Preview build outputs |
    action: Clean build outputs | -Apply | danger | Deletes the build output folders that pass the three locks.
#>

[CmdletBinding()]
param(
    [string]$Target,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$LibDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'build-outputs.ps1')

$Workflow = 'Clean build outputs'

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Cleaning build outputs failed.' `
        -Details ([ordered]@{
            Error = $_.Exception.Message
        })
    throw
}

$RepoRoot = if ($Target) {
    (Resolve-Path -LiteralPath $Target).Path
} else {
    $toplevel = & git -C $PSScriptRoot rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace("$toplevel")) {
        throw "Git could not find the repository root from '$PSScriptRoot'. Pass -Target with the repository root."
    }
    ("$toplevel").Trim()
}
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)

$output = New-WorkflowOutput -Category 'clean'
Write-OutputText -Text "Repo: $RepoRoot" -Role Muted

Write-WorkflowStep -Output $output -Message 'Detect the languages of the repository'
$languages = @(Get-PresentLanguages -RepositoryRoot $RepoRoot)
$languageNames = @($languages | ForEach-Object Name)
if ($languages.Count -eq 0) {
    Write-WorkflowMessage -Output $output -Message 'No known language found in the tracked files' -Role Muted
} else {
    Write-WorkflowMessage -Output $output -Message ($languageNames -join ', ')
}

Write-WorkflowStep -Output $output -Message 'Find build output folders and check the three locks'
$candidates = @(Get-BuildOutputCandidates -RepositoryRoot $RepoRoot -Languages $languages | ForEach-Object {
    $locks = Test-BuildOutputLocks -RepositoryRoot $RepoRoot -Path $_.Path -Languages $languages
    [pscustomobject]@{
        Path         = $_.Path
        RelativePath = $_.RelativePath
        Languages    = $_.Languages
        Locks        = $locks
        Bytes        = Get-BuildOutputSize -Path $_.Path
    }
})
if ($candidates.Count -eq 0) {
    Write-WorkflowMessage -Output $output -Message 'No build output folder found' -Role Muted
}

$lockLabels = @{
    KnownOutput   = 'not a known output folder'
    GitIgnored    = 'git does not ignore it'
    NoTrackedFile = 'it holds a tracked file'
}
$removable = @($candidates | Where-Object { $_.Locks.Passed })
$kept = @($candidates | Where-Object { -not $_.Locks.Passed })
foreach ($candidate in $removable) {
    Write-WorkflowMessage -Output $output -Message "$($candidate.RelativePath)  $(Format-BuildOutputSize -Bytes $candidate.Bytes)  [passes the three locks]" -Role Success
}
foreach ($candidate in $kept) {
    Write-WorkflowMessage -Output $output -Message "$($candidate.RelativePath)  $(Format-BuildOutputSize -Bytes $candidate.Bytes)  [kept, $($lockLabels[$candidate.Locks.FailedLock])]" -Role Warning
}

$removed = @()
$failures = @()
if ($Apply -and $removable.Count -gt 0) {
    Write-WorkflowStep -Output $output -Message 'Remove the build output folders'
    foreach ($candidate in $removable) {
        try {
            Remove-BuildOutput -RepositoryRoot $RepoRoot -Path $candidate.Path
            $removed += $candidate
            Write-WorkflowResult -Output $output -Message "Removed $($candidate.RelativePath)"
        } catch {
            $failures += [pscustomobject]@{ Candidate = $candidate; Message = $_.Exception.Message }
            Write-WorkflowMessage -Output $output -Message "Could not remove $($candidate.RelativePath): $($_.Exception.Message)" -Role Error
        }
    }
}

$removableBytes = [int64](($removable | Measure-Object -Property Bytes -Sum).Sum ?? 0)
$freedBytes = [int64](($removed | Measure-Object -Property Bytes -Sum).Sum ?? 0)

$result = if ($failures.Count -gt 0) { 'Partial' } else { 'Success' }
$sentence = if ($failures.Count -gt 0) {
    "Removed $($removed.Count) build output folder(s); $($failures.Count) could not be removed."
} elseif ($candidates.Count -eq 0) {
    'No build output folder to clean.'
} elseif ($Apply) {
    "Removed $($removed.Count) build output folder(s), freeing $(Format-BuildOutputSize -Bytes $freedBytes)."
} else {
    "Would remove $($removable.Count) build output folder(s), freeing $(Format-BuildOutputSize -Bytes $removableBytes). Nothing was changed."
}
$details = [ordered]@{
    Mode      = $(if ($Apply) { 'Apply' } else { 'Preview' })
    Languages = $(if ($languageNames.Count -gt 0) { $languageNames -join ', ' } else { 'None' })
    Candidates = [string]$candidates.Count
}
if ($Apply) {
    $details['Removed'] = [string]$removed.Count
    $details['Freed'] = Format-BuildOutputSize -Bytes $freedBytes
    if ($failures.Count -gt 0) { $details['Failed'] = [string]$failures.Count }
} else {
    $details['Would remove'] = [string]$removable.Count
    $details['Would free'] = Format-BuildOutputSize -Bytes $removableBytes
}
$details['Kept'] = [string]$kept.Count

$next = @()
if (-not $Apply -and $removable.Count -gt 0) { $next += 'Run the clean action to remove them.' }
if ($failures.Count -gt 0) { $next += 'Close the programs that use these folders, then run it again.' }

Write-ActionSummary `
    -Workflow $Workflow `
    -Result $result `
    -Sentence $sentence `
    -Details $details `
    -Next $next

if ($result -eq 'Partial') { exit 1 }
