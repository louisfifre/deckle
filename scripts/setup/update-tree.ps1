<#
.SYNOPSIS
    Regenerate TREE.md
.DESCRIPTION
    Writes the repository tree from git ls-files, annotating Markdown files with
    their front matter and scripts with their Header. Serves the pre-commit hook
    and direct runs. The preview reports whether TREE.md would change and writes
    nothing.
.NOTES
    section: Repository
    order: 120
    action: Preview tree |
    action: Regenerate TREE.md | -Apply
#>

[CmdletBinding()]
param(
    [string]$Target,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$LibDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'script-discovery.psm1') -Force

$Workflow = 'Regenerate TREE.md'

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Regenerating TREE.md failed.' `
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

# git writes UTF-8 paths, and PowerShell would decode a native command's output
# with the console code page, which turns an accented name into noise and makes
# TREE.md differ from one machine to the next. Reading the pipe as UTF-8 here
# keeps the file the same everywhere without touching global console state.
function Get-TrackedFiles {
    param([Parameter(Mandatory)][string]$Root)

    $info = [Diagnostics.ProcessStartInfo]::new('git')
    foreach ($argument in @('-C', $Root, '-c', 'core.quotepath=false', 'ls-files', '-z')) {
        $info.ArgumentList.Add($argument)
    }
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $info.UseShellExecute = $false

    $process = [Diagnostics.Process]::Start($info)
    $errorText = $process.StandardError.ReadToEndAsync()
    $outputText = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "git could not list the tracked files of '$Root': $($errorText.Result.Trim())"
    }
    return @($outputText -split "`0" | Where-Object { $_ -ne '' })
}

function Format-TreeSuffix {
    param([string]$Name, [string]$Description)

    if (-not $Name -and -not $Description) { return '' }

    if ($Description -and $Description.Length -gt 80) {
        $Description = $Description.Substring(0, 79).TrimEnd() + '…'
    }

    $parts = @($Name, $Description) | Where-Object { $_ }
    $tail = $parts -join ' '
    if (-not $tail) { return '' }
    return "  — $tail"
}

function Format-MarkdownSuffix {
    param([string]$AbsolutePath)

    $frontMatter = Get-MarkdownFrontMatter -Path $AbsolutePath
    if (-not $frontMatter) { return '' }
    return Format-TreeSuffix -Name $frontMatter['name'] -Description $frontMatter['description']
}

# A script Header plays the role of front matter: its synopsis names the
# file and its description describes it, so TREE.md reads both alike.
function Format-ScriptSuffix {
    param([string]$AbsolutePath)

    if (-not (Test-Path -LiteralPath $AbsolutePath -PathType Leaf)) { return '' }
    $header = Read-ScriptHeader -Path $AbsolutePath
    if ($header.Status -ne 'Described') { return '' }
    return Format-TreeSuffix -Name $header.Label -Description $header.Description
}

function New-Node {
    return @{
        Dirs  = [System.Collections.Specialized.OrderedDictionary]::new([System.StringComparer]::OrdinalIgnoreCase)
        Files = [System.Collections.Generic.List[string]]::new()
    }
}

function Add-Path {
    param($Node, [string[]]$Parts)

    if ($Parts.Count -eq 1) {
        $Node.Files.Add($Parts[0])
        return
    }
    $directory = $Parts[0]
    if (-not $Node.Dirs.Contains($directory)) {
        $Node.Dirs[$directory] = New-Node
    }
    Add-Path $Node.Dirs[$directory] $Parts[1..($Parts.Count - 1)]
}

# Ordinal sorting keeps the order identical on every machine; Sort-Object would
# follow the culture of the one that runs it.
function Get-OrderedNames {
    param([string[]]$Names)

    $sorted = [string[]]@($Names)
    [Array]::Sort($sorted, [System.StringComparer]::OrdinalIgnoreCase)
    return $sorted
}

function Add-NodeLines {
    param($Node, [string]$Prefix, [string]$RelativeDir, [string]$Root, $Lines)

    $entries = @(
        foreach ($name in (Get-OrderedNames -Names @($Node.Dirs.Keys))) { [pscustomobject]@{ Name = $name; IsDir = $true } }
        foreach ($name in (Get-OrderedNames -Names @($Node.Files))) { [pscustomobject]@{ Name = $name; IsDir = $false } }
    )

    for ($index = 0; $index -lt $entries.Count; $index++) {
        $entry = $entries[$index]
        $isLast = $index -eq $entries.Count - 1
        $connector = if ($isLast) { '└── ' } else { '├── ' }
        $indent = if ($isLast) { '    ' } else { '│   ' }
        $relative = if ($RelativeDir) { "$RelativeDir/$($entry.Name)" } else { $entry.Name }

        if ($entry.IsDir) {
            $Lines.Add("$Prefix$connector$($entry.Name)/")
            Add-NodeLines -Node $Node.Dirs[$entry.Name] -Prefix "$Prefix$indent" -RelativeDir $relative -Root $Root -Lines $Lines
            continue
        }

        $suffix = ''
        if ($entry.Name -like '*.md') {
            $suffix = Format-MarkdownSuffix -AbsolutePath (Join-Path $Root $relative)
        } elseif ($entry.Name -like '*.ps1' -or $entry.Name -like '*.psm1') {
            $suffix = Format-ScriptSuffix -AbsolutePath (Join-Path $Root $relative)
        }
        $Lines.Add("$Prefix$connector$($entry.Name)$suffix")
    }
}

$repoRoot = Resolve-TargetRoot -Path $Target
$outputFile = Join-Path $repoRoot 'TREE.md'
$output = New-WorkflowOutput -Category 'tree'

Write-OutputText -Text "Repo: $repoRoot" -Role Muted
Write-OutputText -Text "TREE: $outputFile" -Role Muted

Write-WorkflowStep -Output $output -Message 'Collect tracked files'
# TREE.md lists itself whether or not it is tracked yet: otherwise the first
# generation would differ from every later one and change the file once more.
$files = @(Get-TrackedFiles -Root $repoRoot) + @('TREE.md') | Select-Object -Unique
Write-WorkflowMessage -Output $output -Message "$($files.Count) file(s) in the tree"

$root = New-Node
foreach ($file in $files) {
    Add-Path $root ($file -split '/')
}
$lines = [System.Collections.Generic.List[string]]::new()
Add-NodeLines -Node $root -Prefix '' -RelativeDir '' -Root $repoRoot -Lines $lines
$body = $lines -join "`n"

# No wall-clock timestamp on purpose: it would make every regeneration differ
# and collide across branches at merge time. Without it an unchanged tree
# regenerates a byte-identical file; git history already records when it
# changed.
$title = Split-Path -Leaf $repoRoot
$fence = '```'
$content = @(
    "# Tree — $title"
    '_Generated from `git ls-files` — do not edit by hand._'
    ''
    $fence
    $body
    $fence
    ''
) -join "`n"

# A checkout may have turned the file's line endings into CRLF; comparing
# without them keeps that from counting as a change.
$existing = if (Test-Path -LiteralPath $outputFile -PathType Leaf) {
    [IO.File]::ReadAllText($outputFile) -replace "`r`n", "`n"
} else {
    $null
}
$changed = $existing -cne $content

Write-WorkflowStep -Output $output -Message $(if ($Apply) { 'Write TREE.md' } else { 'Compare with TREE.md' })
if ($changed) {
    $state = if ($null -eq $existing) { 'TREE.md does not exist yet' } else { 'TREE.md differs from the tree' }
    Write-WorkflowAction -Output $output -Message $state
} else {
    Write-WorkflowMessage -Output $output -Message 'TREE.md is current' -Role Muted
}
if ($Apply -and $changed) {
    [IO.File]::WriteAllText($outputFile, $content, [Text.UTF8Encoding]::new($false))
    Write-WorkflowResult -Output $output -Message 'TREE.md updated'
}

$sentence = if ($Apply) {
    if ($changed) { 'TREE.md regenerated.' } else { 'TREE.md was already current.' }
} else {
    if ($changed) { 'Preview: TREE.md would change.' } else { 'Preview: TREE.md is current.' }
}
$next = if (-not $Apply -and $changed) { @('Run "Regenerate TREE.md" to write it.') } else { @() }
Write-ActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence $sentence `
    -Details ([ordered]@{
        Files   = [string]$files.Count
        Changed = $(if ($changed) { 'yes' } else { 'no' })
        Output  = $outputFile
    }) `
    -Next $next
