# Generic tracked-file inventory used by targeted repository scans.

Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'source-metrics.psm1') -Force

$script:RepositoryTextExtensions = @(
    '.cs', '.xaml', '.resw', '.csproj', '.xml', '.json', '.jsonl',
    '.md', '.txt', '.ps1', '.psm1', '.py', '.toml', '.csv', '.yml',
    '.yaml', '.props', '.targets', '.manifest', '.html', '.css', '.js',
    '.ts', '.tsx', '.jsx', '.sh', '.cmd', '.bat', '.sln', '.slnx'
)

function Get-RepositoryExtensionLabel {
    param([Parameter(Mandatory)][string]$RelativePath)

    $extension = [System.IO.Path]::GetExtension($RelativePath)
    if ([string]::IsNullOrWhiteSpace($extension)) { return '(none)' }
    return $extension.ToLowerInvariant()
}

function Get-RepositoryTopFolder {
    param([Parameter(Mandatory)][string]$RelativePath)

    $parts = @($RelativePath -split '/')
    if ($parts.Count -le 1) { return '(root)' }
    return $parts[0]
}

function Test-RepositoryPathInScope {
    param(
        [Parameter(Mandatory)][string]$RelativePath,
        [AllowEmptyString()][string]$Scope = ''
    )

    if ([string]::IsNullOrWhiteSpace($Scope) -or $Scope -eq '.') { return $true }
    $normalizedPath = $RelativePath.TrimStart('./').Replace('\', '/')
    $normalizedScope = $Scope.Trim().Trim('./').Replace('\', '/')
    return $normalizedPath.Equals($normalizedScope, [StringComparison]::OrdinalIgnoreCase) -or
        $normalizedPath.StartsWith($normalizedScope + '/', [StringComparison]::OrdinalIgnoreCase)
}

function Test-GeneratedSourcePath {
    param([Parameter(Mandatory)][string]$RelativePath)

    $parts = @($RelativePath -split '/')
    foreach ($part in $parts) {
        if ($part -in @('bin', 'obj', 'artifacts', '.vs', 'Generated Files', 'Properties')) { return $true }
    }
    return [System.IO.Path]::GetFileName($RelativePath) -match
        '(\.g\.cs|\.g\.i\.cs|\.xaml\.g\.cs|GlobalUsings\.g\.cs|AssemblyInfo\.cs)$'
}

function Get-TrackedRepositoryPaths {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $paths = @(& git -C $RepoRoot ls-files)
    if ($LASTEXITCODE -ne 0) { throw "Could not list tracked files under $RepoRoot." }
    return @($paths | ForEach-Object { $_.Replace('\', '/') })
}

function Get-RepositoryCandidates {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [AllowEmptyString()][string]$RelativePath = '',
        [string[]]$Extensions = @(),
        [ValidateSet('All', 'Text', 'Source', 'Documentation')]
        [string]$FileSet = 'All'
    )

    $normalizedExtensions = @($Extensions | ForEach-Object {
        if ($_.StartsWith('.')) { $_.ToLowerInvariant() } else { '.' + $_.ToLowerInvariant() }
    })

    foreach ($path in @(Get-TrackedRepositoryPaths -RepoRoot $RepoRoot)) {
        if (-not (Test-RepositoryPathInScope -RelativePath $path -Scope $RelativePath)) { continue }
        $extension = Get-RepositoryExtensionLabel -RelativePath $path
        if ($normalizedExtensions.Count -gt 0 -and $extension -notin $normalizedExtensions) { continue }

        $included = switch ($FileSet) {
            'Text'          { $extension -in $script:RepositoryTextExtensions }
            'Source'        {
                $extension -in @('.cs', '.xaml', '.resw') -and
                    -not (Test-GeneratedSourcePath -RelativePath $path)
            }
            'Documentation' { $extension -in @('.md', '.txt', '.adoc', '.rst') }
            default         { $true }
        }
        if (-not $included) { continue }

        [pscustomobject]@{
            Path      = $path
            Extension = $extension
            Scope     = Get-RepositoryTopFolder -RelativePath $path
        }
    }
}

function Measure-RepositoryCandidate {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)]$Candidate,
        [switch]$MeasureContent,
        [switch]$MeasureSource,
        [ValidateRange(1, [int64]::MaxValue)][int64]$ContentLimitBytes = 5MB
    )

    $fullPath = Join-Path $RepoRoot $Candidate.Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        return [pscustomobject]@{
            File = $null
            Diagnostic = "Tracked file is unavailable: $($Candidate.Path)"
        }
    }

    $item = Get-Item -LiteralPath $fullPath -Force
    $isLink = ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$item.LinkType)
    if ($item.PSIsContainer -and -not $isLink) {
        return [pscustomobject]@{
            File = $null
            Diagnostic = "Tracked path is not a file: $($Candidate.Path)"
        }
    }
    $fileLength = if ($item.PSIsContainer) { 0L } else { [int64]$item.Length }
    $rawLines = $null
    $sourceLines = $null
    $reswKeys = $null
    $contentSkipped = $null

    $canReadContent = -not $isLink -and
        $Candidate.Extension -in $script:RepositoryTextExtensions -and
        $fileLength -le $ContentLimitBytes

    if (($MeasureContent -or $MeasureSource) -and $canReadContent) {
        try {
            $lines = @([System.IO.File]::ReadAllLines($item.FullName))
            $rawLines = $lines.Count
            if ($MeasureSource) {
                switch ($Candidate.Extension) {
                    '.cs'   { $sourceLines = Measure-CSharpEffectiveLines -Lines $lines }
                    '.xaml' {
                        $sourceLines = @($lines | Where-Object {
                            $trimmed = $_.Trim()
                            $trimmed -ne '' -and -not $trimmed.StartsWith('<!--')
                        }).Count
                    }
                    '.resw' { $reswKeys = Measure-ReswEntries -Lines $lines }
                }
            }
        } catch {
            $contentSkipped = "Could not read text metrics for $($Candidate.Path): $($_.Exception.Message)"
        }
    } elseif ($isLink) {
        $contentSkipped = "Link counted without traversal: $($Candidate.Path)"
    } elseif (($MeasureContent -or $MeasureSource) -and $fileLength -gt $ContentLimitBytes) {
        $contentSkipped = "Content metrics skipped above $ContentLimitBytes bytes: $($Candidate.Path)"
    }

    return [pscustomobject]@{
        File = [pscustomobject]@{
            Path        = $Candidate.Path
            Extension   = $Candidate.Extension
            Scope       = $Candidate.Scope
            Bytes       = $fileLength
            Lines       = $rawLines
            SourceLines = $sourceLines
            ReswKeys    = $reswKeys
            IsLink      = $isLink
        }
        Diagnostic = $contentSkipped
    }
}

function New-RepositoryFinding {
    param(
        [Parameter(Mandatory)]$File,
        [Parameter(Mandatory)][string]$Measure,
        [Parameter(Mandatory)][int64]$Value,
        [Parameter(Mandatory)][int64]$Warning,
        [Parameter(Mandatory)][int64]$Critical
    )

    if ($Warning -le 0 -or $Value -lt $Warning) { return $null }
    $level = if ($Critical -gt 0 -and $Value -ge $Critical) { 'Critical' } else { 'Review' }
    $threshold = if ($level -eq 'Critical') { $Critical } else { $Warning }
    return [pscustomobject]@{
        Category  = 'Threshold'
        Path      = $File.Path
        Measure   = $Measure
        Value     = $Value
        Threshold = $threshold
        Level     = $level
    }
}

function Get-RepositoryGroupRows {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Files,
        [ValidateSet('None', 'Extension', 'TopFolder')]
        [string]$GroupBy = 'None'
    )

    if ($GroupBy -eq 'None') { return @() }
    $property = if ($GroupBy -eq 'Extension') { 'Extension' } else { 'Scope' }
    return @($Files | Group-Object -Property $property | Sort-Object -Property @(
        @{ Expression = 'Count'; Descending = $true },
        @{ Expression = 'Name'; Descending = $false }
    ) | ForEach-Object {
        $measuredLines = @($_.Group | Where-Object { $null -ne $_.Lines })
        $measuredSource = @($_.Group | Where-Object { $null -ne $_.SourceLines })
        [pscustomobject]@{
            Name        = $_.Name
            Files       = $_.Count
            Bytes       = Get-RepositorySum -Items @($_.Group) -Property Bytes
            Lines       = Get-RepositorySum -Items $measuredLines -Property Lines
            SourceLines = Get-RepositorySum -Items $measuredSource -Property SourceLines
            ReswKeys    = Get-RepositorySum -Items @($_.Group | Where-Object { $null -ne $_.ReswKeys }) -Property ReswKeys
        }
    })
}

function Get-RepositorySum {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Items,
        [Parameter(Mandatory)][string]$Property
    )

    if ($Items.Count -eq 0) { return 0L }
    return [int64](($Items | Measure-Object -Property $Property -Sum).Sum ?? 0)
}

function Invoke-RepositoryInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [AllowEmptyString()][string]$RelativePath = '',
        [string[]]$Extensions = @(),
        [ValidateSet('All', 'Text', 'Source', 'Documentation')]
        [string]$FileSet = 'All',
        [switch]$MeasureContent,
        [switch]$MeasureSource,
        [ValidateSet('None', 'Extension', 'TopFolder')]
        [string]$GroupBy = 'None',
        [int64]$BytesWarning = 0,
        [int64]$BytesCritical = 0,
        [int64]$LinesWarning = 0,
        [int64]$LinesCritical = 0,
        [int64]$SourceLinesWarning = 0,
        [int64]$SourceLinesCritical = 0
    )

    $root = (Resolve-Path -LiteralPath $RepoRoot).Path
    $files = [System.Collections.Generic.List[object]]::new()
    $diagnostics = [System.Collections.Generic.List[string]]::new()
    foreach ($candidate in @(Get-RepositoryCandidates -RepoRoot $root -RelativePath $RelativePath -Extensions $Extensions -FileSet $FileSet)) {
        $measurement = Measure-RepositoryCandidate -RepoRoot $root -Candidate $candidate -MeasureContent:$MeasureContent -MeasureSource:$MeasureSource
        if ($measurement.File) { $files.Add($measurement.File) }
        if ($measurement.Diagnostic) { $diagnostics.Add($measurement.Diagnostic) }
    }

    $fileArray = @($files)
    $findings = [System.Collections.Generic.List[object]]::new()
    foreach ($file in $fileArray) {
        foreach ($finding in @(
            (New-RepositoryFinding -File $file -Measure Bytes -Value $file.Bytes -Warning $BytesWarning -Critical $BytesCritical)
            $(if ($null -ne $file.Lines) { New-RepositoryFinding -File $file -Measure Lines -Value $file.Lines -Warning $LinesWarning -Critical $LinesCritical })
            $(if ($null -ne $file.SourceLines) { New-RepositoryFinding -File $file -Measure SourceLines -Value $file.SourceLines -Warning $SourceLinesWarning -Critical $SourceLinesCritical })
        )) {
            if ($finding) { $findings.Add($finding) }
        }
    }

    $lineFiles = @($fileArray | Where-Object { $null -ne $_.Lines })
    $sourceFiles = @($fileArray | Where-Object { $null -ne $_.SourceLines })
    $resourceFiles = @($fileArray | Where-Object { $null -ne $_.ReswKeys })
    return [pscustomobject]@{
        Files = $fileArray
        Totals = [pscustomobject]@{
            Files         = $fileArray.Count
            Bytes         = Get-RepositorySum -Items $fileArray -Property Bytes
            Lines         = Get-RepositorySum -Items $lineFiles -Property Lines
            SourceLines   = Get-RepositorySum -Items $sourceFiles -Property SourceLines
            ReswKeys      = Get-RepositorySum -Items $resourceFiles -Property ReswKeys
            MeasuredFiles = $lineFiles.Count
            LinkedFiles   = @($fileArray | Where-Object IsLink).Count
        }
        Groups      = @(Get-RepositoryGroupRows -Files $fileArray -GroupBy $GroupBy)
        Findings    = @($findings | Sort-Object `
            @{ Expression = { if ($_.Level -eq 'Critical') { 2 } else { 1 } }; Descending = $true }, `
            @{ Expression = 'Value'; Descending = $true })
        Diagnostics = @($diagnostics)
    }
}

Export-ModuleMember -Function Test-RepositoryPathInScope, Get-RepositoryCandidates, Invoke-RepositoryInventory
