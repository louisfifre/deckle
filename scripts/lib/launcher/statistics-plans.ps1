# Targeted maintenance scan specifications and validation.

function Format-ScanPlanCount {
    param($Value)
    return '{0:N0}' -f [int64]$Value
}

function Format-ScanPlanSize {
    param([int64]$Bytes)
    if ($Bytes -ge 1MB) { return '{0:N0} MB' -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return '{0:N0} KB' -f ($Bytes / 1KB) }
    return "$Bytes B"
}

function Get-MaintenanceScanGoals {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Repository', 'Context')]
        [string]$Kind
    )

    if ($Kind -eq 'Repository') {
        return @(
            [pscustomobject]@{ Label = 'Overview';         Value = 'overview' }
            [pscustomobject]@{ Label = 'Files to review';  Value = 'files-to-review' }
            [pscustomobject]@{ Label = 'Source metrics';    Value = 'source-metrics' }
            [pscustomobject]@{ Label = 'Custom scan…';      Value = 'custom'; Role = 'folder' }
        )
    }

    return @(
        [pscustomobject]@{ Label = 'Footprint';       Value = 'footprint' }
        [pscustomobject]@{ Label = 'Recent changes';  Value = 'recent-changes' }
        [pscustomobject]@{ Label = 'Custom scan…';    Value = 'custom'; Role = 'folder' }
    )
}

function New-MaintenanceScanSpecification {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Repository', 'Context')]
        [string]$Kind,
        [Parameter(Mandatory)][string]$Goal
    )

    $specification = [ordered]@{
        Kind              = $Kind
        Goal              = $Goal
        GoalLabel         = ''
        ScopePath         = ''
        FileSet           = if ($Kind -eq 'Repository') { 'All' } else { 'Markdown' }
        LoadingModes      = @()
        DocumentTypes     = @()
        Measurements      = @()
        GroupBy           = 'None'
        ThresholdProfile  = 'Off'
        ActivityDays      = 0
        Detail            = 'Summary'
    }

    switch ("$Kind/$Goal") {
        'Repository/overview' {
            $specification.GoalLabel = 'Overview'
            $specification.Measurements = @('Files', 'Bytes')
            $specification.GroupBy = 'Extension'
        }
        'Repository/files-to-review' {
            $specification.GoalLabel = 'Files to review'
            $specification.FileSet = 'Text'
            $specification.Measurements = @('Files', 'Bytes', 'Lines', 'SourceLines')
            $specification.ThresholdProfile = 'Standard'
            $specification.Detail = 'Findings'
        }
        'Repository/source-metrics' {
            $specification.GoalLabel = 'Source metrics'
            $specification.FileSet = 'Source'
            $specification.Measurements = @('Files', 'Bytes', 'Lines', 'SourceLines', 'ReswKeys')
            $specification.GroupBy = 'Extension'
        }
        'Repository/custom' {
            $specification.GoalLabel = 'Custom scan'
            $specification.FileSet = 'Text'
            $specification.Measurements = @('Files', 'Bytes', 'Lines')
            $specification.GroupBy = 'Extension'
        }
        'Context/footprint' {
            $specification.GoalLabel = 'Footprint'
            $specification.Measurements = @('Files', 'Bytes', 'Lines', 'Sections', 'EstimatedTokens')
            $specification.GroupBy = 'LoadingMode'
            $specification.ThresholdProfile = 'Standard'
            $specification.Detail = 'Findings'
        }
        'Context/recent-changes' {
            $specification.GoalLabel = 'Recent changes'
            $specification.Measurements = @('Files', 'Bytes', 'Lines', 'EstimatedTokens', 'GitActivity')
            $specification.GroupBy = 'DocumentType'
            $specification.ActivityDays = 30
            $specification.Detail = 'Findings'
        }
        'Context/custom' {
            $specification.GoalLabel = 'Custom scan'
            $specification.Measurements = @('Files', 'Bytes', 'Lines', 'Sections', 'EstimatedTokens')
            $specification.GroupBy = 'DocumentType'
        }
        default { throw "Unknown maintenance scan goal: $Kind/$Goal" }
    }

    return [pscustomobject]$specification
}

function Copy-MaintenanceScanSpecification {
    param([Parameter(Mandatory)]$Specification)

    return [pscustomobject][ordered]@{
        Kind             = $Specification.Kind
        Goal             = 'custom'
        GoalLabel        = 'Custom scan'
        ScopePath        = $Specification.ScopePath
        FileSet          = $Specification.FileSet
        LoadingModes     = @($Specification.LoadingModes)
        DocumentTypes    = @($Specification.DocumentTypes)
        Measurements     = @($Specification.Measurements)
        GroupBy          = $Specification.GroupBy
        ThresholdProfile = $Specification.ThresholdProfile
        ActivityDays     = $Specification.ActivityDays
        Detail           = $Specification.Detail
    }
}

function Get-MaintenanceThresholds {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Repository', 'Context')]
        [string]$Kind,
        [ValidateSet('Off', 'Standard', 'Sensitive')]
        [string]$Profile = 'Off'
    )

    if ($Profile -eq 'Off') {
        return [pscustomobject]@{
            BytesWarning = 0L; BytesCritical = 0L
            LinesWarning = 0L; LinesCritical = 0L
            SourceLinesWarning = 0L; SourceLinesCritical = 0L
            TokensWarning = 0L; TokensCritical = 0L
        }
    }

    if ($Kind -eq 'Context') {
        if ($Profile -eq 'Sensitive') {
            return [pscustomobject]@{
                BytesWarning = 12KB; BytesCritical = 24KB
                LinesWarning = 200; LinesCritical = 400
                SourceLinesWarning = 0; SourceLinesCritical = 0
                TokensWarning = 3000; TokensCritical = 6000
            }
        }
        return [pscustomobject]@{
            BytesWarning = 20KB; BytesCritical = 40KB
            LinesWarning = 300; LinesCritical = 600
            SourceLinesWarning = 0; SourceLinesCritical = 0
            TokensWarning = 5000; TokensCritical = 10000
        }
    }

    if ($Profile -eq 'Sensitive') {
        return [pscustomobject]@{
            BytesWarning = 128KB; BytesCritical = 512KB
            LinesWarning = 300; LinesCritical = 600
            SourceLinesWarning = 250; SourceLinesCritical = 500
            TokensWarning = 0; TokensCritical = 0
        }
    }
    return [pscustomobject]@{
        BytesWarning = 256KB; BytesCritical = 1MB
        LinesWarning = 500; LinesCritical = 1000
        SourceLinesWarning = 400; SourceLinesCritical = 600
        TokensWarning = 0; TokensCritical = 0
    }
}

function Get-MaintenanceScopeLabel {
    param([AllowEmptyString()][string]$ScopePath)
    if ([string]::IsNullOrWhiteSpace($ScopePath)) { return 'Tracked repository' }
    return ($ScopePath.Replace('\', '/').TrimEnd('/') + '/')
}

function Get-MaintenanceFileSetLabel {
    param([Parameter(Mandatory)]$Specification)

    if ($Specification.Kind -eq 'Context') {
        if ($Specification.LoadingModes.Count -eq 1) { return $Specification.LoadingModes[0] }
        if ($Specification.DocumentTypes.Count -gt 0) { return @($Specification.DocumentTypes) -join ', ' }
        return 'Tracked Markdown'
    }

    switch ($Specification.FileSet) {
        'Text'          { return 'Supported text files' }
        'Source'        { return 'C#, XAML, and RESW' }
        'Documentation' { return 'Documentation files' }
        default         { return 'All tracked files' }
    }
}

function Get-MaintenanceGroupingLabel {
    param([Parameter(Mandatory)][string]$GroupBy)
    switch ($GroupBy) {
        'TopFolder'    { return 'Top-level folder' }
        'LoadingMode'  { return 'Loading mode' }
        'DocumentType' { return 'Document type' }
        'None'         { return 'None' }
        default        { return $GroupBy }
    }
}

function Get-MaintenanceThresholdLabel {
    param([Parameter(Mandatory)]$Specification)
    if ($Specification.ThresholdProfile -eq 'Off') { return 'Off' }
    $thresholds = Get-MaintenanceThresholds -Kind $Specification.Kind -Profile $Specification.ThresholdProfile
    if ($Specification.Kind -eq 'Context') {
        return '{0} · {1} / {2} tokens' -f `
            $Specification.ThresholdProfile,
            (Format-ScanPlanCount $thresholds.TokensWarning),
            (Format-ScanPlanCount $thresholds.TokensCritical)
    }
    return '{0} · {1} / {2} lines' -f `
        $Specification.ThresholdProfile,
        (Format-ScanPlanCount $thresholds.LinesWarning),
        (Format-ScanPlanCount $thresholds.LinesCritical)
}

function Get-MaintenanceScanReviewLines {
    param(
        [Parameter(Mandatory)]$Specification,
        [Parameter(Mandatory)][string]$Worktree
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @(
        ('Goal         {0}' -f $Specification.GoalLabel)
        ('Scope        {0}' -f (Get-MaintenanceScopeLabel -ScopePath $Specification.ScopePath))
        $(if ($Specification.Kind -eq 'Context') {
            'Documents    {0}' -f (Get-MaintenanceFileSetLabel -Specification $Specification)
        } else {
            'Files        {0}' -f (Get-MaintenanceFileSetLabel -Specification $Specification)
        })
        ('Measures     {0}' -f (@($Specification.Measurements) -join ', '))
        ('Grouping     {0}' -f (Get-MaintenanceGroupingLabel -GroupBy $Specification.GroupBy))
        ('Thresholds   {0}' -f (Get-MaintenanceThresholdLabel -Specification $Specification))
        $(if ($Specification.ActivityDays -gt 0) { 'Period        Last {0} days' -f $Specification.ActivityDays })
        ('Worktree     {0}' -f $Worktree)
        ''
        'Read-only. Only tracked files in this scope will be inspected.'
    ) | Where-Object { $null -ne $_ }) { $lines.Add($line) }

    if ($Specification.ThresholdProfile -ne 'Off') {
        $thresholds = Get-MaintenanceThresholds -Kind $Specification.Kind -Profile $Specification.ThresholdProfile
        if ($Specification.Kind -eq 'Context') {
            $lines.Insert($lines.Count - 2, ('Limits       {0} / {1}; {2} / {3} lines' -f `
                (Format-ScanPlanSize $thresholds.BytesWarning),
                (Format-ScanPlanSize $thresholds.BytesCritical),
                (Format-ScanPlanCount $thresholds.LinesWarning),
                (Format-ScanPlanCount $thresholds.LinesCritical)))
            $lines.Insert($lines.Count - 2, ('             {0} / {1} estimated tokens' -f `
                (Format-ScanPlanCount $thresholds.TokensWarning),
                (Format-ScanPlanCount $thresholds.TokensCritical)))
        } else {
            $lines.Insert($lines.Count - 2, ('Limits       {0} / {1}; {2} / {3} lines' -f `
                (Format-ScanPlanSize $thresholds.BytesWarning),
                (Format-ScanPlanSize $thresholds.BytesCritical),
                (Format-ScanPlanCount $thresholds.LinesWarning),
                (Format-ScanPlanCount $thresholds.LinesCritical)))
            $lines.Insert($lines.Count - 2, ('             {0} / {1} source lines' -f `
                (Format-ScanPlanCount $thresholds.SourceLinesWarning),
                (Format-ScanPlanCount $thresholds.SourceLinesCritical)))
        }
    }
    return @($lines)
}

function Resolve-MaintenanceScanSpecification {
    param(
        [Parameter(Mandatory)]$Specification,
        [Parameter(Mandatory)][string]$Worktree
    )

    $resolved = Copy-MaintenanceScanSpecification -Specification $Specification
    $resolved.Goal = $Specification.Goal
    $resolved.GoalLabel = $Specification.GoalLabel
    $root = (Resolve-Path -LiteralPath $Worktree).Path
    $scope = [string]$Specification.ScopePath
    if ([string]::IsNullOrWhiteSpace($scope) -or $scope -eq '.') {
        $resolved.ScopePath = ''
        return $resolved
    }

    if ([System.IO.Path]::IsPathFullyQualified($scope) -or $scope.StartsWith('\') -or $scope.StartsWith('/')) {
        throw 'Scan scope must be a path relative to the selected worktree.'
    }

    $parts = @($scope.Replace('\', '/') -split '/' | Where-Object { $_ -and $_ -ne '.' })
    if ($parts -contains '..') { throw 'Scan scope cannot leave the selected worktree.' }
    if ($parts.Count -eq 0) {
        $resolved.ScopePath = ''
        return $resolved
    }
    if ($parts[0] -eq '.git') { throw 'The .git directory is outside maintenance scan boundaries.' }
    if ($parts -contains 'AppData.lnk') { throw 'AppData.lnk is outside maintenance scan boundaries.' }

    $relative = $parts -join [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $root $relative))
    $rootBoundary = $root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootBoundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Scan scope cannot leave the selected worktree.'
    }
    if (-not (Test-Path -LiteralPath $fullPath)) { throw "Scan scope does not exist: $($parts -join '/')" }

    $current = $root
    foreach ($part in $parts) {
        $current = Join-Path $current $part
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$item.LinkType)) {
            throw "Scan scope cannot traverse a link or junction: $($parts -join '/')"
        }
    }

    $resolved.ScopePath = $parts -join '/'
    return $resolved
}
