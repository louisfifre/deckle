<#
.SYNOPSIS
    Check script headers
.DESCRIPTION
    Lists the scripts the menu hides and the headers that are voided. A voided,
    invalid or unparsable Header fails the check, because PowerShell drops such
    a Header without any error; a script without a Header stays hidden by choice
    and does not fail it.
.NOTES
    section: Checks
    order: 430
#>

[CmdletBinding()]
param(
    [string]$Target
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent (Split-Path -Parent $ScriptDir)) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'script-discovery.psm1') -Force

$Workflow = 'Check script headers'

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Checking script headers failed.' `
        -Details ([ordered]@{
            Error = $_.Exception.Message
        })
    throw
}

$RepoRoot = if ($Target) {
    (Resolve-Path -LiteralPath $Target).Path
} else {
    Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $ScriptDir))
}
$ScriptsRoot = Join-Path $RepoRoot 'scripts'
$TestsRoot = Join-Path $ScriptsRoot 'tests'
$CommandsRoot = Join-Path $ScriptsRoot 'commands'
if (-not (Test-Path -LiteralPath $ScriptsRoot -PathType Container)) {
    throw "No scripts folder under '$RepoRoot'."
}

$output = New-WorkflowOutput -Category 'headers'
Write-OutputText -Text "Repo: $RepoRoot" -Role Muted

Write-WorkflowStep -Output $output -Message 'Read script headers'
# Tests are exempt: their fixtures break Headers on purpose.
$headers = @(Get-ChildItem -LiteralPath $ScriptsRoot -Recurse -File |
    Where-Object { $_.Extension -ieq '.ps1' -or $_.Extension -ieq '.psm1' } |
    Where-Object { -not $_.FullName.StartsWith($TestsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) } |
    Sort-Object -Property FullName |
    ForEach-Object { Read-ScriptHeader -Path $_.FullName })
$described = @($headers | Where-Object Status -eq 'Described')
$faults = @($headers | Where-Object { $_.Status -in @('VoidedHeader', 'InvalidHeader', 'ParseError') })
Write-WorkflowMessage -Output $output -Message "$($headers.Count) script(s) read, $($described.Count) described"
if ($faults.Count -eq 0) {
    Write-WorkflowMessage -Output $output -Message 'No voided or invalid header' -Role Success
}
foreach ($fault in $faults) {
    $relative = [IO.Path]::GetRelativePath($RepoRoot, $fault.Path).Replace('\', '/')
    Write-WorkflowMessage -Output $output -Message "$relative [$($fault.Status)] $($fault.Reason)" -Role Error
}

Write-WorkflowStep -Output $output -Message 'Commands hidden by choice (no header)'
$hiddenByChoice = @()
if (Test-Path -LiteralPath $CommandsRoot -PathType Container) {
    $catalog = Get-ScriptCatalog -Path $CommandsRoot
    $hiddenByChoice = @(Get-CatalogHiddenScripts -Catalog $catalog | Where-Object Status -eq 'NoHeader')
}
if ($hiddenByChoice.Count -eq 0) {
    Write-WorkflowMessage -Output $output -Message 'None' -Role Muted
}
foreach ($entry in $hiddenByChoice) {
    Write-WorkflowMessage -Output $output -Message "commands/$($entry.RelativePath)" -Role Muted
}

$result = if ($faults.Count -eq 0) { 'Success' } else { 'Failed' }
$sentence = if ($faults.Count -eq 0) {
    "Every script header is valid; $($hiddenByChoice.Count) command(s) stay hidden by choice."
} else {
    "$($faults.Count) script header(s) are voided or invalid and hide their script."
}
Write-ActionSummary `
    -Workflow $Workflow `
    -Result $result `
    -Sentence $sentence `
    -Details ([ordered]@{
        Described = [string]$described.Count
        Hidden = [string]$hiddenByChoice.Count
        Faulty = [string]$faults.Count
    })

if ($result -eq 'Failed') { exit 1 }
