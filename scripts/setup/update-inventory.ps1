# update-inventory.ps1 - Regenerate scripts/INVENTORY.md.
#
# Lists every tracked script under scripts/ with its sentence, its actions and
# its parameters, read from its Header by the same reader as the menu, so a
# shell or an agent learns which script answers its need from one short file
# instead of opening each script. A script without a Header still appears,
# described by its first comment line, because agents call such scripts too
# (a build wrapper with a mandatory parameter, for one). lib/ and tests/ are
# left out: they are not run on their own.
#
# Serves the pre-commit hook and direct runs, like update-tree.ps1. The
# preview reports whether the file would change and writes nothing. No
# timestamp, so an unchanged set of scripts regenerates a byte-identical file.
#
# Kept out of the launcher menu on purpose, with a plain comment instead of a
# Header: the pre-commit hook regenerates the inventory at every commit.

[CmdletBinding()]
param(
    [string]$Target,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$LibDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'script-discovery.psm1') -Force

$Workflow = 'Regenerate the script inventory'

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Regenerating scripts/INVENTORY.md failed.' `
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

# The index, not the disk: inside the pre-commit hook it already holds a new
# script about to be committed, and it never holds a stray local file.
function Get-TrackedScripts {
    param([Parameter(Mandatory)][string]$Root)

    $paths = @(& git -C $Root -c core.quotepath=false ls-files -- 'scripts/*.ps1' 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git could not list the scripts of '$Root': $($paths -join ' ')"
    }
    $kept = @($paths | Where-Object {
        $_ -and $_ -notmatch '^scripts/(lib|tests)/' -and (Test-Path -LiteralPath (Join-Path $Root $_) -PathType Leaf)
    })
    # Folder first, then name, so each folder is one block; ordinal keys keep
    # the order identical on every machine.
    $keys = [string[]]@($kept | ForEach-Object {
        $slash = $_.LastIndexOf('/')
        $_.Substring(0, $slash) + "`u{0001}" + $_.Substring($slash + 1)
    })
    $sorted = [string[]]@($kept)
    [Array]::Sort($keys, $sorted, [System.StringComparer]::OrdinalIgnoreCase)
    return $sorted
}

# The first sentence is enough to choose a script; the rest stays in the
# script for whoever opens it, which keeps the inventory short.
function Get-FirstSentence {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }
    $text = ($Text -replace '\s+', ' ').Trim()
    $match = [regex]::Match($text, '^.+?[.!?](?=\s|$)')
    if ($match.Success) { return $match.Value }
    return $text
}

# A script without a Header opens, by convention, with `# name.ps1 - sentence`.
function Get-LeadingCommentSentence {
    param([Parameter(Mandatory)][string]$Path)

    # The first comment paragraph, joined, since a sentence wraps over lines.
    # A line holding the file name alone is a title, not a sentence: the
    # paragraph starts after it.
    $paragraph = [System.Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadLines($Path)) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith('#!') -or $trimmed -match '^#\s*Requires') { continue }
        if (-not $trimmed.StartsWith('#')) {
            if ($paragraph.Count -gt 0 -or $trimmed -ne '') { break }
            continue
        }
        $comment = $trimmed.TrimStart('#').Trim()
        if ($paragraph.Count -eq 0) {
            $comment = $comment -replace '^[\w.-]+\.ps1\s*([-—:]\s*)?', ''
            if ($comment -eq '') { continue }
        } elseif ($comment -eq '') {
            break
        }
        $paragraph.Add($comment)
    }
    return Get-FirstSentence -Text ($paragraph -join ' ')
}

function Format-Parameter {
    param(
        [Parameter(Mandatory)][object]$Parameter,
        [AllowEmptyCollection()][string[]]$Variants = @()
    )

    $text = "-$($Parameter.Name)"
    if ($Variants.Count -gt 0) {
        $text += ' ' + ($Variants -join '|')
    } elseif (-not $Parameter.IsSwitch -and $Parameter.TypeName) {
        $text += " <$($Parameter.TypeName)>"
    }
    if ($Parameter.IsMandatory) { $text += ' (required)' }
    return $text
}

function Format-ScriptEntry {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $absolute = Join-Path $Root $RelativePath
    $header = Read-ScriptHeader -Path $absolute
    $shortPath = $RelativePath.Substring('scripts/'.Length)
    $lines = [System.Collections.Generic.List[string]]::new()

    if ($header.Status -eq 'Described') {
        $sentence = Get-FirstSentence -Text $header.Description
        $label = $header.Label.TrimEnd('.')
        $lines.Add("- ``$shortPath`` — **$label.** $sentence".TrimEnd())
    } else {
        $sentence = Get-LeadingCommentSentence -Path $absolute
        if (-not $sentence) { $sentence = 'No description.' }
        $lines.Add("- ``$shortPath`` — $sentence (not in the menu)")
    }

    $actions = @($header.Actions)
    if ($actions.Count -gt 0) {
        $parts = foreach ($action in $actions) {
            $arguments = if ($action.Arguments) { " ``$($action.Arguments)``" } else { '' }
            $danger = if ($action.Danger) { ' (danger)' } else { '' }
            "$($action.Label)$arguments$danger"
        }
        $lines.Add("  - Actions: $($parts -join ' · ')")
    }
    # The Variants are the values of the script's one ValidateSet parameter,
    # the only set the Header reader keeps.
    $variantValues = [string[]]@($header.Variants | ForEach-Object { $_.Value })
    $parameters = @($header.Parameters)
    if ($parameters.Count -gt 0) {
        $formatted = foreach ($parameter in $parameters) {
            $values = if ($parameter.Name -eq $header.VariantParameter) { $variantValues } else { @() }
            Format-Parameter -Parameter $parameter -Variants $values
        }
        $lines.Add("  - Parameters: $(@($formatted) -join ', ')")
    }
    return $lines
}

$repoRoot = Resolve-TargetRoot -Path $Target
$outputFile = Join-Path $repoRoot 'scripts\INVENTORY.md'
$output = New-WorkflowOutput -Category 'inventory'

Write-OutputText -Text "Repo: $repoRoot" -Role Muted
Write-OutputText -Text "Inventory: $outputFile" -Role Muted

Write-WorkflowStep -Output $output -Message 'Read the script Headers'
$scripts = @(Get-TrackedScripts -Root $repoRoot)
Write-WorkflowMessage -Output $output -Message "$($scripts.Count) script(s)"

$body = [System.Collections.Generic.List[string]]::new()
$currentFolder = $null
foreach ($script in $scripts) {
    $folder = Split-Path -Parent $script
    $folder = ($folder -replace '\\', '/') + '/'
    if ($folder -ne $currentFolder) {
        if ($null -ne $currentFolder) { $body.Add('') }
        $body.Add("## ``$folder``")
        $body.Add('')
        $currentFolder = $folder
    }
    foreach ($line in (Format-ScriptEntry -Root $repoRoot -RelativePath $script)) { $body.Add($line) }
}

$content = @(
    '# Scripts'
    ''
    '_Generated from the scripts'' Headers by `scripts/setup/update-inventory.ps1` at each commit — do not edit by hand._'
    ''
    'Run one with `pwsh -NoProfile -File scripts/<path> [arguments]`. An action names the arguments the menu passes; one marked danger changes or deletes something, so read its script first. When no script fits, use your own command.'
    ''
    ($body -join "`n")
    ''
) -join "`n"

$existing = if (Test-Path -LiteralPath $outputFile -PathType Leaf) {
    [IO.File]::ReadAllText($outputFile) -replace "`r`n", "`n"
} else {
    $null
}
$changed = $existing -cne $content

Write-WorkflowStep -Output $output -Message $(if ($Apply) { 'Write the inventory' } else { 'Compare with the inventory' })
if ($changed) {
    $state = if ($null -eq $existing) { 'INVENTORY.md does not exist yet' } else { 'INVENTORY.md differs from the scripts' }
    Write-WorkflowAction -Output $output -Message $state
} else {
    Write-WorkflowMessage -Output $output -Message 'INVENTORY.md is current' -Role Muted
}
if ($Apply -and $changed) {
    [IO.File]::WriteAllText($outputFile, $content, [Text.UTF8Encoding]::new($false))
    Write-WorkflowResult -Output $output -Message 'INVENTORY.md updated'
}

$sentence = if ($Apply) {
    if ($changed) { 'scripts/INVENTORY.md regenerated.' } else { 'scripts/INVENTORY.md was already current.' }
} else {
    if ($changed) { 'Preview: scripts/INVENTORY.md would change.' } else { 'Preview: scripts/INVENTORY.md is current.' }
}
Write-ActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence $sentence `
    -Details ([ordered]@{
        Scripts = [string]$scripts.Count
        Changed = $(if ($changed) { 'yes' } else { 'no' })
        Output  = $outputFile
    }) `
    -Next $(if (-not $Apply -and $changed) { @('Run update-inventory.ps1 -Apply to write it.') } else { @() })
