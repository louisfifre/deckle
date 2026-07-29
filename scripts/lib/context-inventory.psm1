Set-StrictMode -Version Latest

function Get-ContextLoadingMode {
    param([Parameter(Mandatory)][string]$RelativePath)

    $name = [System.IO.Path]::GetFileName($RelativePath)
    if ($name -in @('AGENTS.md', 'CLAUDE.md')) { return 'Automatic instructions' }
    return 'On-demand references'
}

function Get-ContextDocumentType {
    param([Parameter(Mandatory)][string]$RelativePath)

    $name = [System.IO.Path]::GetFileName($RelativePath)
    if ($name -eq 'AGENTS.md') { return 'AGENTS' }
    if ($name -eq 'CLAUDE.md') { return 'CLAUDE' }
    if ($name -in @('CONTEXT.md', 'CONTEXT-MAP.md')) { return 'Context' }
    if ($name -eq 'JOURNAL.md') { return 'Journal' }
    if ($name -eq 'SKILL.md') { return 'Skill' }
    if ($RelativePath -match '(^|[\\/])\.agents[\\/]skills[\\/]') { return 'Skill reference' }
    if ($RelativePath -match '(^|[\\/])docs[\\/]adr[\\/]') { return 'ADR' }
    if ($RelativePath -match '(^|[\\/])docs[\\/]research[\\/]') { return 'Research' }
    if ($RelativePath -match '(^|[\\/])prompts?[\\/]') { return 'Prompt' }
    if ($name -eq 'README.md') { return 'README' }
    if ($name -eq 'CHANGELOG.md') { return 'Changelog' }
    if ($name -eq 'TREE.md') { return 'Generated index' }
    return 'Project document'
}

function Measure-MarkdownSections {
    param([Parameter(Mandatory)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Lines)

    $sections = 0
    $fenceCharacter = $null
    foreach ($line in $Lines) {
        if ($line -match '^\s{0,3}(`{3,}|~{3,})') {
            $character = $Matches[1][0]
            if ($null -eq $fenceCharacter) {
                $fenceCharacter = $character
            } elseif ($character -eq $fenceCharacter) {
                $fenceCharacter = $null
            }
            continue
        }
        if ($null -eq $fenceCharacter -and $line -match '^\s{0,3}#{1,6}(?:\s+|$)') {
            $sections++
        }
    }
    return $sections
}

function Measure-ContextDocument {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $fullPath = Join-Path $RepoRoot $RelativePath
    $text = [System.IO.File]::ReadAllText($fullPath)
    $bytes = (Get-Item -LiteralPath $fullPath -Force).Length
    $contentLines = [System.IO.File]::ReadAllLines($fullPath)

    [pscustomobject]@{
        LoadingMode     = Get-ContextLoadingMode -RelativePath $RelativePath
        DocumentType    = Get-ContextDocumentType -RelativePath $RelativePath
        Path            = $RelativePath -replace '\\', '/'
        Bytes           = $bytes
        Characters      = $text.Length
        Lines           = $contentLines.Count
        Sections        = Measure-MarkdownSections -Lines $contentLines
        EstimatedTokens = [Math]::Ceiling($text.Length / 4.0)
    }
}

function Get-RecentlyAddedMarkdownPaths {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [ValidateRange(1, 3650)][int]$Days = 30
    )

    $head = & git -C $RepoRoot rev-parse --verify HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $head) { return @() }

    $baseline = & git -C $RepoRoot rev-list -1 --before="$Days days ago" HEAD
    if ($LASTEXITCODE -ne 0) { throw "Could not resolve the Git baseline from $Days days ago." }
    if (-not $baseline) { return @() }

    $paths = @(& git -C $RepoRoot diff --name-only --diff-filter=A $baseline HEAD -- '*.md')
    if ($LASTEXITCODE -ne 0) { throw "Could not list Markdown files added in the last $Days days." }
    return @($paths | ForEach-Object { $_ -replace '\\', '/' })
}

function ConvertFrom-GitMarkdownLog {
    param([Parameter(Mandatory)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Lines)

    $dates = @{}
    $currentDate = $null
    foreach ($line in $Lines) {
        if ($line -match '^@@(\d{4}-\d{2}-\d{2})$') {
            $currentDate = $Matches[1]
            continue
        }
        if ($currentDate -and $line -match '\.md$') {
            $path = $line -replace '\\', '/'
            if (-not $dates.ContainsKey($path)) { $dates[$path] = $currentDate }
        }
    }
    return $dates
}

function Get-MarkdownLastModifiedDates {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $head = & git -C $RepoRoot rev-parse --verify HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $head) { return @{} }

    $log = @(& git -C $RepoRoot log --date=short '--format=@@%cs' --name-only -- '*.md')
    if ($LASTEXITCODE -ne 0) { throw 'Could not read Markdown modification dates from Git history.' }
    return ConvertFrom-GitMarkdownLog -Lines $log
}

function Select-ContextInventoryPaths {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Paths,
        [AllowEmptyString()][string]$RelativePath = '',
        [string[]]$LoadingModes = @(),
        [string[]]$DocumentTypes = @()
    )

    $scope = $RelativePath.Trim().Trim('./').Replace('\', '/')
    foreach ($pathValue in $Paths) {
        $path = $pathValue.Replace('\', '/')
        if ($scope -and
            -not $path.Equals($scope, [StringComparison]::OrdinalIgnoreCase) -and
            -not $path.StartsWith($scope + '/', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        $loadingMode = Get-ContextLoadingMode -RelativePath $path
        if ($LoadingModes.Count -gt 0 -and $loadingMode -notin $LoadingModes) { continue }
        $documentType = Get-ContextDocumentType -RelativePath $path
        if ($DocumentTypes.Count -gt 0 -and $documentType -notin $DocumentTypes) { continue }
        [pscustomobject]@{
            Path         = $path
            LoadingMode  = $loadingMode
            DocumentType = $documentType
        }
    }
}

function Get-ContextInventory {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [AllowEmptyString()][string]$RelativePath = '',
        [string[]]$LoadingModes = @(),
        [string[]]$DocumentTypes = @(),
        [bool]$IncludeActivity = $true,
        [ValidateRange(1, 3650)][int[]]$ActivityDays = @(1, 7, 30),
        [ValidateRange(0, 3650)][int]$ActivityWindowDays = 0
    )

    $paths = @(& git -C $RepoRoot ls-files -- '*.md')
    if ($LASTEXITCODE -ne 0) { throw "Could not list tracked Markdown files under $RepoRoot." }

    $candidates = @(Select-ContextInventoryPaths -Paths $paths -RelativePath $RelativePath `
        -LoadingModes $LoadingModes -DocumentTypes $DocumentTypes)

    $recentPaths = @{}
    $requestedDays = @(@(1, 7, 30) + $(if ($ActivityWindowDays -gt 0) { $ActivityWindowDays }) | Select-Object -Unique)
    foreach ($days in $requestedDays) {
        $recentPaths[$days] = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        if ($IncludeActivity -and ($days -in $ActivityDays -or $days -eq $ActivityWindowDays)) {
            foreach ($path in @(Get-RecentlyAddedMarkdownPaths -RepoRoot $RepoRoot -Days $days)) {
                $recentPaths[$days].Add($path) | Out-Null
            }
        }
    }
    $modifiedDates = if ($IncludeActivity) { Get-MarkdownLastModifiedDates -RepoRoot $RepoRoot } else { @{} }

    if ($IncludeActivity -and $ActivityWindowDays -gt 0) {
        $cutoff = [DateTime]::Today.AddDays(-$ActivityWindowDays)
        $candidates = @($candidates | Where-Object {
            if ($recentPaths[$ActivityWindowDays].Contains($_.Path)) { return $true }
            $modified = [DateTime]::MinValue
            return $modifiedDates[$_.Path] -and
                [DateTime]::TryParse([string]$modifiedDates[$_.Path], [ref]$modified) -and
                $modified -ge $cutoff
        })
    }

    @($candidates | ForEach-Object {
        $fullPath = Join-Path $RepoRoot $_.Path
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { return }
        $item = Get-Item -LiteralPath $fullPath -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$item.LinkType)) {
            return
        }
        $document = Measure-ContextDocument -RepoRoot $RepoRoot -RelativePath $_.Path
        $document | Add-Member -NotePropertyName Modified -NotePropertyValue $modifiedDates[$document.Path]
        $document | Add-Member -NotePropertyName Added1Day -NotePropertyValue $recentPaths[1].Contains($document.Path)
        $document | Add-Member -NotePropertyName Added7Days -NotePropertyValue $recentPaths[7].Contains($document.Path)
        $document | Add-Member -NotePropertyName Added30Days -NotePropertyValue $recentPaths[30].Contains($document.Path)
        $document | Add-Member -NotePropertyName AddedInPeriod -NotePropertyValue $(
            $ActivityWindowDays -gt 0 -and $recentPaths[$ActivityWindowDays].Contains($document.Path))
        $document
    })
}

Export-ModuleMember -Function Get-ContextLoadingMode, Get-ContextDocumentType, Measure-MarkdownSections, Measure-ContextDocument, Get-RecentlyAddedMarkdownPaths, ConvertFrom-GitMarkdownLog, Get-MarkdownLastModifiedDates, Select-ContextInventoryPaths, Get-ContextInventory
