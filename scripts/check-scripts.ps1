<#
.SYNOPSIS
    Check scripts
.DESCRIPTION
    Reads every script the menu could offer and reports the ones it would
    hide or that break the conventions: a voided, invalid or unparsable
    Header (PowerShell drops such a Header without any error), a -Pick
    parameter (the menu asks the questions), a -Target parameter that is not
    a string, or a file encoded with a byte order mark. A script without a
    Header stays hidden by choice and does not fail the check.
.NOTES
    section: Checks
    order: 900
#>

[CmdletBinding()]
param(
    [string]$Target
)

$ErrorActionPreference = 'Stop'
$LibDir = Join-Path $PSScriptRoot 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'script-discovery.psm1') -Force

$Workflow = 'Check scripts'

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Checking the scripts failed.' `
        -Details ([ordered]@{
            Error = $_.Exception.Message
        })
    throw
}

$RepoRoot = if ($Target) {
    (Resolve-Path -LiteralPath $Target).ProviderPath
} else {
    Split-Path -Parent $PSScriptRoot
}
$ScriptsRoot = Join-Path $RepoRoot 'scripts'
if (-not (Test-Path -LiteralPath $ScriptsRoot -PathType Container)) {
    throw "No scripts folder under '$RepoRoot'."
}

# The entry, the library and the tests are not offered by the menu and
# follow their own rules (menu.ps1 is 5.1-readable, the tests break Headers
# on purpose).
$ExemptFolders = @('lib', 'tests') | ForEach-Object { Join-Path $ScriptsRoot $_ }
$ExemptFiles = @('menu.ps1') | ForEach-Object { Join-Path $ScriptsRoot $_ }

function Test-ExemptPath {
    param([Parameter(Mandatory)][string]$Path)

    foreach ($file in $ExemptFiles) {
        if ($Path.Equals($file, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    foreach ($folder in $ExemptFolders) {
        if ($Path.StartsWith($folder + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Get-ScriptConventionFaults {
    # The rules a Header cannot tell: they read the file's bytes and its
    # parameter block.
    param([Parameter(Mandatory)][string]$Path)

    $faults = [System.Collections.Generic.List[string]]::new()
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $faults.Add('starts with a byte order mark; save it as UTF-8 without BOM')
    }
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { return @($faults) }
    $paramBlock = $ast.ParamBlock
    if ($null -eq $paramBlock) { return @($faults) }
    foreach ($parameter in $paramBlock.Parameters) {
        $name = $parameter.Name.VariablePath.UserPath
        if ($name -eq 'Pick') {
            $faults.Add('declares -Pick; the menu asks the questions, a script takes plain parameters')
        }
        if ($name -eq 'Target') {
            $typeName = if ($null -ne $parameter.StaticType) { $parameter.StaticType.FullName } else { 'System.Object' }
            if ($typeName -ne 'System.String') {
                $faults.Add("declares -Target as $typeName; the menu passes a path, so it must be [string]")
            }
        }
    }
    return @($faults)
}

$output = New-WorkflowOutput -Category 'scripts'
Write-OutputText -Text "Repo: $RepoRoot" -Role Muted

Write-WorkflowStep -Output $output -Message 'Read script headers'
$files = @(Get-ChildItem -LiteralPath $ScriptsRoot -Recurse -File |
    Where-Object { $_.Extension -ieq '.ps1' } |
    Where-Object { -not (Test-ExemptPath -Path $_.FullName) } |
    Sort-Object -Property FullName)
$headers = @($files | ForEach-Object { Read-ScriptHeader -Path $_.FullName })
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

Write-WorkflowStep -Output $output -Message 'Check the conventions'
$conventionFaults = 0
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($RepoRoot, $file.FullName).Replace('\', '/')
    foreach ($reason in @(Get-ScriptConventionFaults -Path $file.FullName)) {
        $conventionFaults++
        Write-WorkflowMessage -Output $output -Message "$relative $reason" -Role Error
    }
}
if ($conventionFaults -eq 0) {
    Write-WorkflowMessage -Output $output -Message 'Every script follows the conventions' -Role Success
}

Write-WorkflowStep -Output $output -Message 'Scripts hidden by choice (no header)'
$catalog = Get-ScriptCatalog -Path $ScriptsRoot -ExcludeFolder @('lib', 'tests')
$hiddenByChoice = @(Get-CatalogHiddenScripts -Catalog $catalog |
    Where-Object { $_.Status -eq 'NoHeader' -and $_.RelativePath -ne 'menu.ps1' })
if ($hiddenByChoice.Count -eq 0) {
    Write-WorkflowMessage -Output $output -Message 'None' -Role Muted
}
foreach ($entry in $hiddenByChoice) {
    Write-WorkflowMessage -Output $output -Message "scripts/$($entry.RelativePath)" -Role Muted
}

$faultCount = $faults.Count + $conventionFaults
$result = if ($faultCount -eq 0) { 'Success' } else { 'Failed' }
$sentence = if ($faultCount -eq 0) {
    "Every script is valid; $($hiddenByChoice.Count) script(s) stay hidden by choice."
} else {
    "$faultCount fault(s) hide a script or break the conventions."
}
Write-ActionSummary `
    -Workflow $Workflow `
    -Result $result `
    -Sentence $sentence `
    -Details ([ordered]@{
        Described = [string]$described.Count
        Hidden = [string]$hiddenByChoice.Count
        Faulty = [string]$faultCount
    })

if ($result -eq 'Failed') { exit 1 }
