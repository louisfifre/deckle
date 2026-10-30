<#
.SYNOPSIS
    Scaffold repository files
.DESCRIPTION
    Creates the standard folders and files a repository starts with, and never
    overwrites what exists: whatever is already there is kept as it is. The
    preview lists what would be created and what is kept.
.NOTES
    section: Repository
    order: 100
    action: Preview scaffold |
    action: Scaffold repository files | -Apply
#>

[CmdletBinding()]
param(
    [string]$Target,
    [switch]$Apply,
    [string[]]$Folders = @('docs', 'src', 'tests'),
    [string]$License = 'MIT',
    [string]$Name,
    [string]$Holder
)

$ErrorActionPreference = 'Stop'
$LibDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')

$Workflow = 'Scaffold repository files'

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Scaffolding the repository failed.' `
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

# The licenses are validated here rather than with [ValidateSet]: a scalar
# [ValidateSet] would turn into menu Variants, and the menu offers none.
function Resolve-LicenseChoice {
    param([string]$Value)

    $known = @{ 'mit' = 'MIT'; 'gpl-3.0' = 'GPL-3.0'; 'none' = 'None' }
    $key = $Value.Trim().ToLowerInvariant()
    if (-not $known.ContainsKey($key)) {
        throw "Unknown license '$Value'. Choose MIT, GPL-3.0 or None."
    }
    return $known[$key]
}

function Test-RelativeFolder {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) { return $false }
    foreach ($part in ($Path -split '[\\/]')) {
        if ($part -eq '..') { return $false }
    }
    return $true
}

function Read-Template {
    param([Parameter(Mandatory)][string]$TemplateName)

    $path = Join-Path $LibDir "templates\$TemplateName"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Template '$TemplateName' is missing from '$(Join-Path $LibDir 'templates')'."
    }
    # Templates are stored with whatever line ending the checkout gave them;
    # the files they create always start with LF and let .gitattributes decide.
    return [IO.File]::ReadAllText($path) -replace "`r`n", "`n"
}

function Expand-Template {
    param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][hashtable]$Values)

    foreach ($key in $Values.Keys) {
        $Text = $Text.Replace("{{$key}}", [string]$Values[$key])
    }
    return $Text
}

$root = Resolve-TargetRoot -Path $Target
$licenseChoice = Resolve-LicenseChoice -Value $License
$projectName = if ($Name) { $Name } else { Split-Path -Leaf $root }

foreach ($folder in $Folders) {
    if (-not (Test-RelativeFolder -Path $folder)) {
        throw "Folder '$folder' must be a relative path inside the repository."
    }
}

$templateFiles = [ordered]@{
    'AGENTS.md'       = 'AGENTS.md'
    'CLAUDE.md'       = 'CLAUDE.md'
    '.gitignore'      = 'gitignore.template'
    '.gitattributes'  = 'gitattributes.template'
    'CONTRIBUTING.md' = 'CONTRIBUTING.md'
}
if ($licenseChoice -eq 'MIT') { $templateFiles['LICENSE'] = 'LICENSE-MIT' }
if ($licenseChoice -eq 'GPL-3.0') { $templateFiles['LICENSE'] = 'LICENSE-GPL-3.0' }

# Read every template and settle every value before anything is written, so a
# missing template or holder stops the run with the repository untouched.
$contents = @{}
$holderValue = $Holder
foreach ($file in $templateFiles.Keys) {
    $contents[$file] = Read-Template -TemplateName $templateFiles[$file]
}
if (($contents.Values | Where-Object { $_.Contains('{{Holder}}') }) -and [string]::IsNullOrWhiteSpace($holderValue)) {
    $holderValue = ((& git -C $root config user.name 2>$null) -join '').Trim()
    if ([string]::IsNullOrWhiteSpace($holderValue)) {
        throw "The $licenseChoice license needs a copyright holder, and git has no user.name for '$root'. Pass -Holder or set user.name."
    }
}
$values = @{ Name = $projectName; Year = (Get-Date).Year; Holder = $holderValue }

$plan = @()
foreach ($folder in $Folders) {
    $path = Join-Path $root $folder
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        throw "'$folder' exists as a file, so the folder cannot be created."
    }
    $plan += [pscustomobject]@{
        Kind     = 'Folder'
        Display  = ($folder.Replace('\', '/').TrimEnd('/')) + '/'
        Path     = $path
        Exists   = Test-Path -LiteralPath $path -PathType Container
        Template = $null
    }
}
foreach ($file in $templateFiles.Keys) {
    $path = Join-Path $root $file
    if (Test-Path -LiteralPath $path -PathType Container) {
        throw "'$file' exists as a folder, so the file cannot be created."
    }
    $plan += [pscustomobject]@{
        Kind     = 'File'
        Display  = $file
        Path     = $path
        Exists   = Test-Path -LiteralPath $path -PathType Leaf
        Template = $file
    }
}

$output = New-WorkflowOutput -Category 'scaffold'
Write-OutputText -Text "Repo: $root" -Role Muted
Write-WorkflowStep -Output $output -Message $(if ($Apply) { 'Create the standard files' } else { 'Plan the standard files' })

$created = @()
$kept = @()
foreach ($item in $plan) {
    if ($item.Exists) {
        $kept += $item.Display
        Write-WorkflowMessage -Output $output -Message "kept     $($item.Display)" -Role Muted
        continue
    }
    $created += $item.Display
    if (-not $Apply) {
        Write-WorkflowAction -Output $output -Message "create   $($item.Display)"
        continue
    }
    if ($item.Kind -eq 'Folder') {
        $null = New-Item -ItemType Directory -Path $item.Path
        # git cannot track an empty folder, so a placeholder file holds it.
        [IO.File]::WriteAllText((Join-Path $item.Path '.gitkeep'), '', [Text.UTF8Encoding]::new($false))
    } else {
        $text = Expand-Template -Text $contents[$item.Template] -Values $values
        [IO.File]::WriteAllText($item.Path, $text, [Text.UTF8Encoding]::new($false))
    }
    Write-WorkflowResult -Output $output -Message "created  $($item.Display)"
}

$createdLabel = if ($Apply) { 'Created' } else { 'Would create' }
$none = 'none'
$sentence = if ($created.Count -eq 0) {
    "Nothing to scaffold: all $($kept.Count) item(s) already exist."
} elseif ($Apply) {
    "Created $($created.Count) item(s), kept $($kept.Count)."
} else {
    "Preview: $($created.Count) item(s) would be created, $($kept.Count) kept."
}
$details = [ordered]@{}
$details[$createdLabel] = if ($created.Count -gt 0) { $created } else { $none }
$details['Kept'] = if ($kept.Count -gt 0) { $kept } else { $none }
$details['License'] = $licenseChoice
Write-ActionSummary `
    -Workflow $Workflow `
    -Result $(if ($created.Count -eq 0) { 'Skipped' } else { 'Success' }) `
    -Sentence $sentence `
    -Details $details `
    -Next $(if (-not $Apply -and $created.Count -gt 0) { @('Run "Scaffold repository files" to create them.') } else { @() })
