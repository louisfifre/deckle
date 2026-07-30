# Text sweep of the exposables carried by the source tree.
#
# An exposable is a code value someone could act on — a persisted setting, a
# hardcoded tuning constant, a default, a Playground memory-only parameter —
# whether or not any surface exposes it today. The sweep reads text only: no
# build, no reflection, no runtime. Every entry keeps its file and line so the
# arbitration that follows can go back to the source.

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,
    [string]$OutputPath = '',
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ModifierPattern = 'public|internal|protected|private'
$script:TypePattern = '[\w\.\?]+(?:<[^>()=;]*>)?(?:\[\])?\??'
$script:TypeDeclarationPattern =
    '^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|protected|private|static|sealed|abstract|partial|readonly|record|file)\s+)*' +
    '(?:class|struct|record|interface)\s+(?<name>\w+)'
$script:PropertyPattern =
    "^\s*(?<modifier>$script:ModifierPattern)\s+(?:(?<static>static)\s+)?(?:(?:required|virtual|override|new|sealed|partial)\s+)*" +
    "(?<type>$script:TypePattern)\s+(?<name>\w+)\s*\{\s*(?:get|init|set)\b[^}]*\}\s*(?:=\s*(?<value>.+?)\s*;)?\s*(?://.*)?$"
$script:FieldPattern =
    "^\s*(?:(?<modifier>$script:ModifierPattern)\s+)?(?<qualifiers>(?:const|static|readonly|volatile)\s+)*" +
    "(?<type>$script:TypePattern)\s+(?<name>\w+)\s*(?:=\s*(?<value>.+?)\s*)?;\s*(?://.*)?$"
$script:LiteralValuePattern = '^(?:-?\d[\w\.]*|true|false|null|"[^"]*"|''[^'']*''|\w+\.\w+)$'
$script:IgnoredFieldNames = @('value', 'result', 'index', 'count', 'i', 'j')

function Test-ExposableSourcePath {
    param([Parameter(Mandatory)][string]$RelativePath)

    foreach ($part in @($RelativePath -split '/')) {
        if ($part -in @('bin', 'obj', 'artifacts', '.vs', 'Generated Files')) { return $false }
    }
    return [System.IO.Path]::GetFileName($RelativePath) -notmatch '(\.g\.cs|\.g\.i\.cs|\.xaml\.g\.cs|GlobalUsings\.g\.cs|AssemblyInfo\.cs)$'
}

function Get-ExposableModuleName {
    param([Parameter(Mandatory)][string]$RelativePath)

    $parts = @($RelativePath -split '/')
    if ($parts.Count -ge 2 -and $parts[0] -eq 'src') { return $parts[1] }
    return $parts[0]
}

function Get-ExposableKind {
    # The kind says where the value lives and how it survives a restart, not
    # what it tunes: persistence is what arbitration needs to know first.
    param(
        [Parameter(Mandatory)][string]$Module,
        [Parameter(Mandatory)][AllowEmptyString()][string]$DeclaringType,
        [Parameter(Mandatory)][string]$Declaration,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Qualifiers
    )

    if ($Module -eq 'Deckle.Playground') { return 'playground-parameter' }
    if ($Qualifiers -match 'const|static') { return 'tuning-constant' }
    if ($DeclaringType -match 'Settings$|Settings[A-Z]|Options$|Profile$') { return 'persisted-setting' }
    if ($Declaration -eq 'property') { return 'default-value' }
    return 'tuning-constant'
}

function Read-ExposableSourceFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $module = Get-ExposableModuleName -RelativePath $RelativePath
    $lines = @([System.IO.File]::ReadAllLines((Join-Path $RepoRoot $RelativePath)))
    $declaringType = ''

    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('//') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('/*')) { continue }

        $typeMatch = [regex]::Match($line, $script:TypeDeclarationPattern)
        if ($typeMatch.Success) { $declaringType = $typeMatch.Groups['name'].Value }

        $declaration = ''
        $match = [regex]::Match($line, $script:PropertyPattern)
        if ($match.Success) {
            $declaration = 'property'
        } else {
            $match = [regex]::Match($line, $script:FieldPattern)
            if (-not $match.Success) { continue }
            $declaration = 'field'
            $qualifiers = $match.Groups['qualifiers'].Value
            # A bare local (`var x = …`, `int total = 0;`) is not an exposable:
            # keep fields that are declared members or compile-time constants.
            if ($match.Groups['modifier'].Value -eq '' -and $qualifiers -notmatch 'const|static') { continue }
            if ($match.Groups['type'].Value -in @('var', 'return', 'new')) { continue }
            if ($match.Groups['name'].Value.ToLowerInvariant() -in $script:IgnoredFieldNames) { continue }
        }

        $value = if ($match.Groups['value'].Success) { $match.Groups['value'].Value } else { '' }
        $qualifiers = if ($declaration -eq 'field') { $match.Groups['qualifiers'].Value.Trim() } else { $match.Groups['static'].Value }
        # A constant whose value is a computed expression is machinery, not a
        # knob — only literals and enum members read as something to act on.
        if ($declaration -eq 'field' -and $qualifiers -match 'const|static' -and $value -notmatch $script:LiteralValuePattern) { continue }

        $name = $match.Groups['name'].Value
        $kind = Get-ExposableKind -Module $module -DeclaringType $declaringType -Declaration $declaration -Qualifiers $qualifiers
        # Mutable instance state, injected collaborators and ETW event ids
        # carry no value anyone would act on — they only bury the real knobs.
        if ($name.StartsWith('_')) { continue }
        if ($value -eq '' -and $kind -ne 'persisted-setting') { continue }
        if ($declaringType -match 'Source$' -and $name -match '^(Evt|Task|Keyword)[A-Z]') { continue }

        [pscustomobject]@{
            name        = $name
            kind        = $kind
            type        = $match.Groups['type'].Value
            value       = $value
            declaration = $declaration
            owner       = $declaringType
            module      = $module
            location    = "$RelativePath`:$($index + 1)"
        }
    }
}

function Read-ExposableJsonFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$RelativePath
    )

    # JSON defaults shipped with the source tree read one leaf per line; the
    # sweep stays textual so a line number survives for every entry.
    $module = Get-ExposableModuleName -RelativePath $RelativePath
    $lines = @([System.IO.File]::ReadAllLines((Join-Path $RepoRoot $RelativePath)))
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $match = [regex]::Match($lines[$index], '^\s*"(?<name>[^"]+)"\s*:\s*(?<value>-?\d[\d\.eE\-\+]*|true|false|null|"[^"]*")\s*,?\s*$')
        if (-not $match.Success) { continue }

        [pscustomobject]@{
            name        = $match.Groups['name'].Value
            kind        = 'json-default'
            type        = 'json'
            value       = $match.Groups['value'].Value
            declaration = 'json-leaf'
            owner       = [System.IO.Path]::GetFileNameWithoutExtension($RelativePath)
            module      = $module
            location    = "$RelativePath`:$($index + 1)"
        }
    }
}

function Invoke-ExposableSweep {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)

    $tracked = @(& git -C $RepoRoot ls-files 'src')
    if ($LASTEXITCODE -ne 0) { throw "Could not list tracked files under $RepoRoot." }

    foreach ($path in @($tracked | ForEach-Object { $_.Replace('\', '/') })) {
        if (-not (Test-ExposableSourcePath -RelativePath $path)) { continue }
        switch ([System.IO.Path]::GetExtension($path).ToLowerInvariant()) {
            '.cs'   { Read-ExposableSourceFile -RepoRoot $RepoRoot -RelativePath $path }
            '.json' { Read-ExposableJsonFile -RepoRoot $RepoRoot -RelativePath $path }
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $root = (Resolve-Path -LiteralPath $RepoRoot).Path
    $destination = if ($OutputPath -ne '') { $OutputPath } else { Join-Path $root 'exposables-raw.jsonl' }
    $entries = @(Invoke-ExposableSweep -RepoRoot $root)
    Set-Content -LiteralPath $destination -Value @($entries | ForEach-Object { $_ | ConvertTo-Json -Compress }) -Encoding utf8

    if (-not $Quiet) {
        Write-Host "Exposables swept: $($entries.Count) -> $destination"
        $entries | Group-Object module | Sort-Object Count -Descending |
            ForEach-Object { Write-Host ("  {0,-32} {1,5}" -f $_.Name, $_.Count) }
        $entries | Group-Object kind | Sort-Object Count -Descending |
            ForEach-Object { Write-Host ("  [{0}] {1}" -f $_.Name, $_.Count) }
    }
}
