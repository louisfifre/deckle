# stats.ps1 - File inventory and line counts per Deckle module
#
# Walks every .csproj under <RepoRoot>\src\, then builds a single file
# inventory for each module. The console views are derived from that same
# inventory:
#   - long-file watch list (500+ lines, 1000+ lines)
#   - module summary, sorted by module name
#   - dynamic file-type counts found in modules
#   - detailed per-module file tree
#
# Generated files and build output are excluded: bin/, obj/, .vs/,
# __pycache__/, Generated Files/, Properties/, *.g.cs, *.g.i.cs, *.xaml.g.cs,
# GlobalUsings.g.cs, AssemblyInfo.cs.
#
# LOC is intentionally conservative:
#   - .cs strips blank lines and pure comment lines
#   - .xaml strips blank lines and single-line XML comments
#   - .resw is reported by raw line count plus <data> entry count
# Other text files get raw line counts only.

[CmdletBinding()]
param(
    # Override the target worktree. Defaults to the repo containing
    # this script (two levels up: scripts/lib -> scripts -> repo root).
    [string]$Target,

    # Interactive worktree picker via scripts/lib/_menu.psm1. Overrides
    # -Target. Useful when several worktrees are checked out.
    [switch]$Pick,

    # Files at or above this raw-line count are shown as watch items.
    [int]$WatchThreshold = 500,

    # Files at or above this raw-line count are shown as too large.
    [int]$TooLargeThreshold = 1000,

    # .resw files at or above this key count are shown as resource inventories.
    [int]$ResourceWatchThreshold = 300,

    # .resw files at or above this key count are considered oversized inventories.
    [int]$ResourceTooLargeThreshold = 500,

    # Write module and file inventory to this JSON path in addition to
    # the console output.
    [string]$Json
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot

if ($Pick) {
    Import-Module (Join-Path $ScriptDir '_menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}

Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray

$SrcDir = Join-Path $RepoRoot 'src'
if (-not (Test-Path $SrcDir)) { throw "src/ not found under $RepoRoot" }

$skipDirs       = @('bin', 'obj', '.git', '.vs', '__pycache__', '.pytest_cache', '.mypy_cache', '.ruff_cache', 'Generated Files', 'Properties')
$generatedRegex = '(\.g\.cs|\.g\.i\.cs|\.xaml\.g\.cs|GlobalUsings\.g\.cs|AssemblyInfo\.cs)$'
$textExtensions = @(
    '.cs', '.xaml', '.resw', '.csproj', '.xml', '.json', '.jsonl',
    '.md', '.txt', '.ps1', '.psm1', '.py', '.toml', '.csv', '.yml',
    '.yaml', '.props', '.targets', '.manifest'
)
$script:FileTablePathWidth = 50
$script:FileTableTotalWidth = 109

function Get-RelativePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )
    return [System.IO.Path]::GetRelativePath($Root, $Path)
}

function Get-ExtensionLabel {
    param([System.IO.FileInfo]$File)
    if ([string]::IsNullOrWhiteSpace($File.Extension)) { return '(none)' }
    return $File.Extension.ToLowerInvariant()
}

function Format-Size {
    param([int64]$Bytes)
    if     ($Bytes -ge 1GB) { return '{0:N1} GB' -f ($Bytes / 1GB) }
    elseif ($Bytes -ge 1MB) { return '{0:N1} MB' -f ($Bytes / 1MB) }
    elseif ($Bytes -ge 1KB) { return '{0:N1} KB' -f ($Bytes / 1KB) }
    else                    { return "$Bytes B" }
}

function Write-Section {
    param([Parameter(Mandatory)][string]$Title)
    Write-Host ""
    $bold = [char]27 + '[1m'
    $reset = [char]27 + '[0m'
    Write-Host ("{0}== {1} =={2}" -f $bold, $Title, $reset) -ForegroundColor Cyan
}

function Out-PlainTable {
    param(
        [Parameter(Mandatory, ValueFromPipeline)]$InputObject,
        [Parameter(Mandatory)]$Property
    )

    begin {
        $items = New-Object System.Collections.Generic.List[object]
        $oldHeaderStyle = $null
        $oldOutputRendering = $null
        $psStyleVar = Get-Variable -Name PSStyle -ErrorAction SilentlyContinue
        $canStyleTableHeader = $null -ne $psStyleVar -and $null -ne $PSStyle.Formatting
        if ($canStyleTableHeader) {
            $oldHeaderStyle = $PSStyle.Formatting.TableHeader
            $oldOutputRendering = $PSStyle.OutputRendering
            $PSStyle.Formatting.TableHeader = ''
            $PSStyle.OutputRendering = 'PlainText'
        }
    }
    process {
        $items.Add($InputObject) | Out-Null
    }
    end {
        try {
            $items | Format-Table -AutoSize -Property $Property | Out-Host
        } finally {
            if ($canStyleTableHeader) {
                $PSStyle.Formatting.TableHeader = $oldHeaderStyle
                $PSStyle.OutputRendering = $oldOutputRendering
            }
        }
    }
}

function Test-SkippedPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$RootForRelative
    )

    $rel = $Path.Substring($RootForRelative.Length).TrimStart('\','/')
    foreach ($part in ($rel -split '[\\/]')) {
        if ($skipDirs -contains $part) { return $true }
    }
    if ($Path -match $generatedRegex) { return $true }
    return $false
}

function Get-FileLines {
    param([System.IO.FileInfo]$File)

    $ext = Get-ExtensionLabel -File $File
    if ($textExtensions -notcontains $ext) {
        return [pscustomobject]@{
            RawLines = $null
            Loc      = $null
            ReswKeys = $null
        }
    }

    $lines = @(Get-Content -LiteralPath $File.FullName -Encoding UTF8 -ErrorAction SilentlyContinue)
    $raw = $lines.Count
    $loc = $null
    $reswKeys = $null

    if ($ext -eq '.cs') {
        $loc = @($lines | Where-Object {
            $t = $_.Trim()
            ($t -ne '') -and
                -not $t.StartsWith('//') -and
                -not $t.StartsWith('/*') -and
                -not $t.StartsWith('*') -and
                -not $t.StartsWith('*/')
        }).Count
    } elseif ($ext -eq '.xaml') {
        $loc = @($lines | Where-Object {
            $t = $_.Trim()
            ($t -ne '') -and -not $t.StartsWith('<!--')
        }).Count
    } elseif ($ext -eq '.resw') {
        $reswKeys = @($lines | Where-Object { $_ -match '^\s*<data\s+name=' }).Count
    }

    return [pscustomobject]@{
        RawLines = $raw
        Loc      = $loc
        ReswKeys = $reswKeys
    }
}

function Measure-ModuleFiles {
    param(
        [Parameter(Mandatory)][string]$ModuleRoot,
        [Parameter(Mandatory)][string]$ModuleName
    )

    $files = Get-ChildItem -LiteralPath $ModuleRoot -Recurse -File -ErrorAction SilentlyContinue |
             Where-Object { -not (Test-SkippedPath -Path $_.FullName -RootForRelative $ModuleRoot) }

    foreach ($file in $files) {
        $lineInfo = Get-FileLines -File $file
        $ext = Get-ExtensionLabel -File $file
        $relModule = Get-RelativePath -Root $ModuleRoot -Path $file.FullName
        $relRepo = Get-RelativePath -Root $RepoRoot -Path $file.FullName
        $kind = if ($file.Name.EndsWith('.xaml.cs', [StringComparison]::OrdinalIgnoreCase)) {
            '.xaml.cs'
        } else {
            $ext
        }

        [pscustomobject]@{
            Module       = $ModuleName
            FullName     = $file.FullName
            RelativeRepo = $relRepo
            Relative     = $relModule
            Directory    = Split-Path $relModule -Parent
            Name         = $file.Name
            Extension    = $ext
            Kind         = $kind
            Bytes        = [int64]$file.Length
            RawLines     = $lineInfo.RawLines
            Loc          = $lineInfo.Loc
            ReswKeys     = $lineInfo.ReswKeys
        }
    }
}

function Get-FileTypeRows {
    param($Files)

    $Files |
        Group-Object Extension |
        Sort-Object -Property @{ Expression = 'Count'; Descending = $true }, Name |
        ForEach-Object {
            $rawSum = ($_.Group | Where-Object { $null -ne $_.RawLines } | Measure-Object -Property RawLines -Sum).Sum
            $bytes = ($_.Group | Measure-Object -Property Bytes -Sum).Sum
            [pscustomobject]@{
                Type  = $_.Name
                Files = $_.Count
                Lines = if ($rawSum) { [int]$rawSum } else { 0 }
                Size  = Format-Size ([int64]$bytes)
            }
        }
}

function Format-FileTypeLine {
    param(
        $Row,
        [int]$TypeWidth = 16
    )

    if ($null -eq $Row) {
        return (' ' * ($TypeWidth + 28))
    }

    return (
        (Format-Cell -Text $Row.Type -Width $TypeWidth) + '  ' +
        (Format-Cell -Text (Format-ValueOrDash $Row.Files) -Width 5 -Right) + '  ' +
        (Format-Cell -Text (Format-ValueOrDash $Row.Lines) -Width 7 -Right) + '  ' +
        (Format-Cell -Text $Row.Size -Width 10 -Right)
    )
}

function Write-FileTypeTables {
    param(
        [Parameter(Mandatory)]$ModuleRows,
        [Parameter(Mandatory)]$RepoRows
    )

    $typeWidth = 16
    $leftTitle = 'Module roots'
    $rightTitle = 'Outside module roots'
    $leftWidth = $typeWidth + 28
    $separator = '  │  '

    Write-Host (Format-Cell -Text $leftTitle -Width $leftWidth) -ForegroundColor DarkCyan -NoNewline
    Write-Host $separator -ForegroundColor DarkGray -NoNewline
    Write-Host $rightTitle -ForegroundColor DarkCyan
    Write-Host ((Format-FileTypeLine -Row ([pscustomobject]@{ Type='Type'; Files='Files'; Lines='Lines'; Size='Size' }) -TypeWidth $typeWidth) + $separator +
                (Format-FileTypeLine -Row ([pscustomobject]@{ Type='Type'; Files='Files'; Lines='Lines'; Size='Size' }) -TypeWidth $typeWidth))
    Write-Host ((Format-FileTypeLine -Row ([pscustomobject]@{ Type='----'; Files='-----'; Lines='-----'; Size='----' }) -TypeWidth $typeWidth) + $separator +
                (Format-FileTypeLine -Row ([pscustomobject]@{ Type='----'; Files='-----'; Lines='-----'; Size='----' }) -TypeWidth $typeWidth))

    $moduleRowsArray = @($ModuleRows)
    $repoRowsArray = @($RepoRows)
    $max = [Math]::Max($moduleRowsArray.Count, $repoRowsArray.Count)
    for ($i = 0; $i -lt $max; $i++) {
        $left = if ($i -lt $moduleRowsArray.Count) { $moduleRowsArray[$i] } else { $null }
        $right = if ($i -lt $repoRowsArray.Count) { $repoRowsArray[$i] } else { $null }
        Write-Host ((Format-FileTypeLine -Row $left -TypeWidth $typeWidth) + $separator +
                    (Format-FileTypeLine -Row $right -TypeWidth $typeWidth))
    }
}

function Write-ThresholdLine {
    param(
        [string]$Marker,
        [ConsoleColor]$Color,
        $File
    )

    Write-Host ("  {0,5} lines  " -f $File.RawLines) -NoNewline
    Write-Host ("{0,-8}" -f $Marker) -ForegroundColor $Color -NoNewline
    Write-Host (" {0}" -f $File.RelativeRepo)
}

function Write-ResourceThresholdLine {
    param(
        [string]$Marker,
        [ConsoleColor]$Color,
        $File
    )

    Write-Host ("  {0,5} keys   " -f $File.ReswKeys) -NoNewline
    Write-Host ("{0,-8}" -f $Marker) -ForegroundColor $Color -NoNewline
    Write-Host (" {0} ({1} raw XML lines)" -f $File.RelativeRepo, $File.RawLines)
}

function Get-PathParts {
    param([Parameter(Mandatory)][string]$Path)
    return @($Path -split '[\\/]')
}

function Test-PathPrefix {
    param(
        [string[]]$Parts,
        [string[]]$Prefix
    )

    if ($Parts.Count -lt $Prefix.Count) { return $false }
    for ($i = 0; $i -lt $Prefix.Count; $i++) {
        if ($Parts[$i] -ne $Prefix[$i]) { return $false }
    }
    return $true
}

function Format-ValueOrDash {
    param($Value)
    if ($null -eq $Value) { return '-' }
    if ($Value -is [int] -or $Value -is [long] -or $Value -is [double]) {
        return '{0:N0}' -f $Value
    }
    return [string]$Value
}

function Format-Cell {
    param(
        [AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][int]$Width,
        [switch]$Right
    )

    if ($null -eq $Text) { $Text = '' }
    if ($Text.Length -gt $Width) {
        $Text = $Text.Substring(0, [Math]::Max(0, $Width - 3)) + '...'
    }
    if ($Right) { return $Text.PadLeft($Width) }
    return $Text.PadRight($Width)
}

function Write-FileTableHeader {
    $header = (
        (Format-Cell -Text '  Path'  -Width $script:FileTablePathWidth) + '  ' +
        (Format-Cell -Text 'Type'  -Width 10) + '  ' +
        (Format-Cell -Text 'Lines' -Width 8 -Right) + '  ' +
        (Format-Cell -Text 'LOC'   -Width 8 -Right) + '  ' +
        (Format-Cell -Text 'Size'  -Width 10 -Right) + '  ' +
        (Format-Cell -Text 'Keys'  -Width 6 -Right) + '  ' +
        'Watch'
    )
    $underLine = (
        (Format-Cell -Text '  ----' -Width $script:FileTablePathWidth) + '  ' +
        (Format-Cell -Text '----' -Width 10) + '  ' +
        (Format-Cell -Text '-----' -Width 8 -Right) + '  ' +
        (Format-Cell -Text '---'  -Width 8 -Right) + '  ' +
        (Format-Cell -Text '----' -Width 10 -Right) + '  ' +
        (Format-Cell -Text '----' -Width 6 -Right) + '  ' +
        '-----'
    )
    Write-Host $header -ForegroundColor DarkGray
    Write-Host $underLine -ForegroundColor DarkGray
}

function Write-FileTableRow {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$PathText,
        [Parameter(Mandatory)]$File
    )

    $marker = $null
    $markerColor = [ConsoleColor]::Gray
    if ($File.Extension -eq '.resw' -and $null -ne $File.ReswKeys) {
        if ($File.ReswKeys -ge $ResourceTooLargeThreshold) {
            $marker = '500k+'
            $markerColor = [ConsoleColor]::Red
        } elseif ($File.ReswKeys -ge $ResourceWatchThreshold) {
            $marker = '300k+'
            $markerColor = [ConsoleColor]::Yellow
        }
    } elseif ($null -ne $File.RawLines -and $File.RawLines -ge $TooLargeThreshold) {
            $marker = '1000+'
            $markerColor = [ConsoleColor]::Red
    } elseif ($null -ne $File.RawLines -and $File.RawLines -ge $WatchThreshold) {
            $marker = '500+'
            $markerColor = [ConsoleColor]::Yellow
    }

    $line = (
        (Format-Cell -Text $PathText -Width $script:FileTablePathWidth) + '  ' +
        (Format-Cell -Text $File.Kind -Width 10) + '  ' +
        (Format-Cell -Text (Format-ValueOrDash $File.RawLines) -Width 8 -Right) + '  ' +
        (Format-Cell -Text (Format-ValueOrDash $File.Loc) -Width 8 -Right) + '  ' +
        (Format-Cell -Text (Format-Size $File.Bytes) -Width 10 -Right) + '  ' +
        (Format-Cell -Text (Format-ValueOrDash $File.ReswKeys) -Width 6 -Right) + '  '
    )
    Write-Host $line -NoNewline
    if ($marker) {
        Write-Host $marker -ForegroundColor $markerColor
    } else {
        Write-Host ''
    }
}

function Write-FolderTableRow {
    param([Parameter(Mandatory)][string]$PathText)
    Write-Host (Format-Cell -Text $PathText -Width $script:FileTablePathWidth) -ForegroundColor DarkGray
}

function Write-ModuleFileTableLevel {
    param(
        [Parameter(Mandatory)]$Files,
        [string[]]$ParentParts = @(),
        [Parameter()][AllowEmptyString()][string]$Prefix = '',
        [switch]$RootLevel
    )

    $depth = $ParentParts.Count
    $dirs = New-Object System.Collections.Generic.HashSet[string]
    $fileItems = New-Object System.Collections.Generic.List[object]

    foreach ($file in $Files) {
        $parts = Get-PathParts -Path $file.Relative
        if (-not (Test-PathPrefix -Parts $parts -Prefix $ParentParts)) { continue }

        if ($parts.Count -eq ($depth + 1)) {
            $fileItems.Add($file) | Out-Null
        } elseif ($parts.Count -gt ($depth + 1)) {
            $dirs.Add($parts[$depth]) | Out-Null
        }
    }

    $fileRows = @($fileItems | Sort-Object Name | ForEach-Object {
        [pscustomobject]@{ Kind = 'file'; Name = $_.Name; File = $_ }
    })
    $dirRows = @($dirs | Sort-Object | ForEach-Object {
        [pscustomobject]@{ Kind = 'dir'; Name = $_; File = $null }
    })
    $items = @($fileRows + $dirRows)

    if ($RootLevel) {
        foreach ($item in $fileRows) {
            Write-FileTableRow -PathText ("  {0}" -f $item.File.Name) -File $item.File
        }
        foreach ($item in $dirRows) {
            Write-FolderTableRow -PathText ("  {0}/" -f $item.Name)
            Write-ModuleFileTableLevel -Files $Files -ParentParts ($ParentParts + $item.Name) -Prefix '    '
        }
        return
    }

    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $last = ($i -eq ($items.Count - 1))
        $branch = if ($last) { '└── ' } else { '├── ' }
        $childPrefix = if ($last) { '    ' } else { '│   ' }

        if ($item.Kind -eq 'dir') {
            Write-FolderTableRow -PathText ("{0}{1}{2}/" -f $Prefix, $branch, $item.Name)
            Write-ModuleFileTableLevel -Files $Files -ParentParts ($ParentParts + $item.Name) -Prefix ($Prefix + $childPrefix)
        } else {
            Write-FileTableRow -PathText ("{0}{1}{2}" -f $Prefix, $branch, $item.File.Name) -File $item.File
        }
    }
}

function Test-UnderAnyRoot {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string[]]$Roots
    )

    foreach ($root in $Roots) {
        $prefix = $root.TrimEnd('\','/') + [System.IO.Path]::DirectorySeparatorChar
        if ($Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Get-RepoTypeFiles {
    param([string[]]$ExcludedRoots)

    Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            -not (Test-SkippedPath -Path $_.FullName -RootForRelative $RepoRoot) -and
            -not (Test-UnderAnyRoot -Path $_.FullName -Roots $ExcludedRoots)
        } |
        ForEach-Object {
            $lineInfo = Get-FileLines -File $_
            $relRepo = Get-RelativePath -Root $RepoRoot -Path $_.FullName
            $parts = Get-PathParts -Path $relRepo
            [pscustomobject]@{
                Scope     = if ($parts.Count -gt 1) { $parts[0] } else { '(repo root)' }
                Extension = Get-ExtensionLabel -File $_
                Bytes     = [int64]$_.Length
                RawLines  = $lineInfo.RawLines
            }
        }
}

# Walk every csproj under src/.
$csprojs = Get-ChildItem -LiteralPath $SrcDir -Recurse -Filter '*.csproj' -ErrorAction SilentlyContinue |
           Where-Object { -not (Test-SkippedPath -Path $_.FullName -RootForRelative $SrcDir) }
if (-not $csprojs) { throw "No .csproj found under $SrcDir" }

$moduleFiles = foreach ($csproj in $csprojs) {
    Measure-ModuleFiles -ModuleRoot $csproj.Directory.FullName -ModuleName $csproj.Directory.Name
}

$rows = foreach ($group in ($moduleFiles | Group-Object Module)) {
    $files = @($group.Group)
    $locCs = ($files | Where-Object { $_.Extension -eq '.cs' } | Measure-Object -Property Loc -Sum).Sum
    $locXaml = ($files | Where-Object { $_.Extension -eq '.xaml' } | Measure-Object -Property Loc -Sum).Sum
    $reswKeys = ($files | Where-Object { $null -ne $_.ReswKeys } | Measure-Object -Property ReswKeys -Sum).Sum
    if (-not $locCs) { $locCs = 0 }
    if (-not $locXaml) { $locXaml = 0 }
    if (-not $reswKeys) { $reswKeys = 0 }
    $fileCount = $files.Count
    $typeSummary = ($files |
        Group-Object Extension |
        Sort-Object -Property @{ Expression = 'Count'; Descending = $true }, Name |
        ForEach-Object { "{0}={1}" -f $_.Name, $_.Count }) -join ' '

    [pscustomobject]@{
        Module      = $group.Name
        Files       = $fileCount
        Types       = $typeSummary
        LocCs       = [int]$locCs
        LocXaml     = [int]$locXaml
        LocTotal    = [int]($locCs + $locXaml)
        ReswKeys    = [int]$reswKeys
        _files      = $files
    }
}

$rows = $rows | Sort-Object Module

# Long file watch list.
$longFiles = @($moduleFiles |
    Where-Object { $_.Extension -ne '.resw' -and $null -ne $_.RawLines -and $_.RawLines -ge $WatchThreshold } |
    Sort-Object -Property @{ Expression = 'RawLines'; Descending = $true }, RelativeRepo)

if ($longFiles.Count -gt 0) {
    Write-Section "Files over threshold (non-resource text)"
    foreach ($file in $longFiles) {
        if ($file.RawLines -ge $TooLargeThreshold) {
            Write-ThresholdLine -Marker '1000+' -Color Red -File $file
        } else {
            Write-ThresholdLine -Marker '500+' -Color Yellow -File $file
        }
    }
}

$resourceFiles = @($moduleFiles |
    Where-Object { $_.Extension -eq '.resw' -and $null -ne $_.ReswKeys -and $_.ReswKeys -ge $ResourceWatchThreshold } |
    Sort-Object -Property @{ Expression = 'ReswKeys'; Descending = $true }, RelativeRepo)

if ($resourceFiles.Count -gt 0) {
    Write-Section "Resource inventories (.resw)"
    foreach ($file in $resourceFiles) {
        if ($file.ReswKeys -ge $ResourceTooLargeThreshold) {
            Write-ResourceThresholdLine -Marker '500k+' -Color Red -File $file
        } else {
            Write-ResourceThresholdLine -Marker '300k+' -Color Yellow -File $file
        }
    }
}

# Module table.
Write-Section "Module summary (src modules)"
$rows | Out-PlainTable -Property @(
    @{Name='Module';     Expression={$_.Module}},
    @{Name='Files';      Expression={$_.Files}; Alignment='Right'},
    @{Name='LOC cs';     Expression={'{0:N0}' -f $_.LocCs}; Alignment='Right'},
    @{Name='LOC xaml';   Expression={'{0:N0}' -f $_.LocXaml}; Alignment='Right'},
    @{Name='LOC total';  Expression={'{0:N0}' -f $_.LocTotal}; Alignment='Right'},
    @{Name='resw keys';  Expression={$_.ReswKeys}; Alignment='Right'}
)

$tot = [pscustomobject]@{
    Files    = ($rows | Measure-Object -Property Files    -Sum).Sum
    LocCs    = ($rows | Measure-Object -Property LocCs    -Sum).Sum
    LocXaml  = ($rows | Measure-Object -Property LocXaml  -Sum).Sum
    LocTotal = ($rows | Measure-Object -Property LocTotal -Sum).Sum
    ReswKeys = ($rows | Measure-Object -Property ReswKeys -Sum).Sum
}
Write-Host ("Module inventory total: {0} file(s)  /  LOC: {1:N0} cs + {2:N0} xaml = {3:N0}  /  resw keys: {4:N0}" -f `
    $tot.Files, $tot.LocCs, $tot.LocXaml, $tot.LocTotal, $tot.ReswKeys) -ForegroundColor Cyan
Write-Host ""

# Dynamic type summary.
Write-Section "File types"
$moduleTypeRows = @(Get-FileTypeRows -Files $moduleFiles)
$moduleRootPaths = @($csprojs | ForEach-Object { $_.Directory.FullName })
$repoTypeFiles = @(Get-RepoTypeFiles -ExcludedRoots $moduleRootPaths)
$scopes = @($repoTypeFiles | Group-Object Scope | Sort-Object Name | ForEach-Object { $_.Name })
$repoTypeRows = @(Get-FileTypeRows -Files $repoTypeFiles)
Write-Host ("Outside scope: {0}" -f ($scopes -join ', ')) -ForegroundColor DarkGray
Write-Host ""
Write-FileTypeTables -ModuleRows $moduleTypeRows -RepoRows $repoTypeRows

# Hierarchical file tree.
Write-Section "Module details (per-file table)"
foreach ($row in $rows) {
    Write-Host ""
    $heading = "--- {0} " -f $row.Module
    Write-Host ($heading.PadRight($script:FileTableTotalWidth, '-')) -ForegroundColor DarkCyan
    Write-Host ("  Summary: {0} file(s) | LOC: {1:N0} cs + {2:N0} xaml = {3:N0} | resw keys: {4:N0}" -f `
        $row.Files, $row.LocCs, $row.LocXaml, $row.LocTotal, $row.ReswKeys) -ForegroundColor DarkGray
    Write-Host ("  Types:   {0}" -f $row.Types) -ForegroundColor DarkGray
    Write-Host ""
    Write-FileTableHeader
    Write-ModuleFileTableLevel -Files @($row._files | Sort-Object Relative) -RootLevel
    Write-Host ""
}

# Optional JSON dump.
if ($Json) {
    $jsonRows = $rows | Select-Object Module, Files, LocCs, LocXaml, LocTotal, ReswKeys, Types
    $jsonFiles = $moduleFiles | Select-Object Module, RelativeRepo, Kind, Extension, Bytes, RawLines, Loc, ReswKeys
    [pscustomobject]@{
        Modules = $jsonRows
        Files   = $jsonFiles
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $Json -Encoding UTF8
    Write-Host "Wrote $Json" -ForegroundColor DarkGray
}
