# Maintenance result models and compact console formatting.
function Format-MaintenanceCount {
    param($Value)
    return '{0:N0}' -f [int64]$Value
}

function Format-MaintenanceSize {
    param([int64]$Bytes)
    if ($Bytes -ge 1GB) { return '{0:N1} GB' -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return '{0:N1} MB' -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return '{0:N1} KB' -f ($Bytes / 1KB) }
    return "$Bytes B"
}

function Get-MaintenanceFailureLines {
    param([Parameter(Mandatory)]$ErrorRecord)

    return @(
        'The scan could not complete.'
        ''
        $ErrorRecord.Exception.Message
        ''
        'No cleanup action was run. You can retry or go back.'
    )
}

function Get-MaintenanceSum {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Items,
        [Parameter(Mandatory)][string]$Property
    )

    if ($Items.Count -eq 0) { return 0L }
    return [int64](($Items | Measure-Object -Property $Property -Sum).Sum ?? 0)
}

function Get-ContextScanGroups {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Documents,
        [ValidateSet('None', 'LoadingMode', 'DocumentType')]
        [string]$GroupBy
    )

    if ($GroupBy -eq 'None') { return @() }
    return @($Documents | Group-Object -Property $GroupBy | Sort-Object Count -Descending | ForEach-Object {
        [pscustomobject]@{
            Name            = $_.Name
            Files           = $_.Count
            Bytes           = Get-MaintenanceSum -Items @($_.Group) -Property Bytes
            Lines           = Get-MaintenanceSum -Items @($_.Group) -Property Lines
            Sections        = Get-MaintenanceSum -Items @($_.Group) -Property Sections
            EstimatedTokens = Get-MaintenanceSum -Items @($_.Group) -Property EstimatedTokens
        }
    })
}

function Add-ContextThresholdFinding {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Findings,
        [Parameter(Mandatory)]$Document,
        [Parameter(Mandatory)][string]$Measure,
        [Parameter(Mandatory)][int64]$Value,
        [Parameter(Mandatory)][int64]$Warning,
        [Parameter(Mandatory)][int64]$Critical
    )

    if ($Warning -le 0 -or $Value -lt $Warning) { return }
    $level = if ($Critical -gt 0 -and $Value -ge $Critical) { 'Critical' } else { 'Review' }
    $Findings.Add([pscustomobject]@{
        Category  = 'Threshold'
        Path      = $Document.Path
        Measure   = $Measure
        Value     = $Value
        Threshold = if ($level -eq 'Critical') { $Critical } else { $Warning }
        Level     = $level
    })
}

function Invoke-ContextTargetedInventory {
    param(
        [Parameter(Mandatory)]$Specification,
        [Parameter(Mandatory)][string]$Worktree,
        [Parameter(Mandatory)][string]$LibDir
    )

    Import-Module (Join-Path $LibDir 'context-inventory.psm1') -Force
    $includeActivity = $Specification.Measurements -contains 'GitActivity'
    $documents = @(Get-ContextInventory `
        -RepoRoot $Worktree `
        -RelativePath $Specification.ScopePath `
        -LoadingModes @($Specification.LoadingModes) `
        -DocumentTypes @($Specification.DocumentTypes) `
        -IncludeActivity:$includeActivity `
        -ActivityWindowDays $(if ($includeActivity) { $Specification.ActivityDays } else { 0 }))

    $findings = [System.Collections.Generic.List[object]]::new()
    $thresholds = Get-MaintenanceThresholds -Kind Context -Profile $Specification.ThresholdProfile
    foreach ($document in $documents) {
        Add-ContextThresholdFinding -Findings $findings -Document $document -Measure Bytes `
            -Value $document.Bytes -Warning $thresholds.BytesWarning -Critical $thresholds.BytesCritical
        Add-ContextThresholdFinding -Findings $findings -Document $document -Measure Lines `
            -Value $document.Lines -Warning $thresholds.LinesWarning -Critical $thresholds.LinesCritical
        Add-ContextThresholdFinding -Findings $findings -Document $document -Measure EstimatedTokens `
            -Value $document.EstimatedTokens -Warning $thresholds.TokensWarning -Critical $thresholds.TokensCritical
    }

    if ($includeActivity) {
        foreach ($document in $documents) {
            if ($document.AddedInPeriod) {
                $findings.Add([pscustomobject]@{
                    Category = 'Activity'; Path = $document.Path; Measure = 'Added'
                    Value = 'Added'; Threshold = "Last $($Specification.ActivityDays) days"; Level = 'Recent'
                })
                continue
            }
            if ($document.Modified) {
                $findings.Add([pscustomobject]@{
                    Category = 'Activity'; Path = $document.Path; Measure = 'Modified'
                    Value = [string]$document.Modified; Threshold = "Last $($Specification.ActivityDays) days"; Level = 'Recent'
                })
            }
        }
    }

    return [pscustomobject]@{
        Kind          = 'Context'
        Worktree      = $Worktree
        Specification = $Specification
        Totals        = [pscustomobject]@{
            Files           = $documents.Count
            Bytes           = Get-MaintenanceSum -Items $documents -Property Bytes
            Lines           = Get-MaintenanceSum -Items $documents -Property Lines
            Sections        = Get-MaintenanceSum -Items $documents -Property Sections
            EstimatedTokens = Get-MaintenanceSum -Items $documents -Property EstimatedTokens
        }
        Groups        = @(Get-ContextScanGroups -Documents $documents -GroupBy $Specification.GroupBy)
        Findings      = @($findings)
        Items         = $documents
        Diagnostics   = @()
    }
}

function Invoke-RepositoryTargetedInventory {
    param(
        [Parameter(Mandatory)]$Specification,
        [Parameter(Mandatory)][string]$Worktree,
        [Parameter(Mandatory)][string]$LibDir
    )

    Import-Module (Join-Path $LibDir 'repository-inventory.psm1') -Force
    $thresholds = Get-MaintenanceThresholds -Kind Repository -Profile $Specification.ThresholdProfile
    $inventory = Invoke-RepositoryInventory `
        -RepoRoot $Worktree `
        -RelativePath $Specification.ScopePath `
        -FileSet $Specification.FileSet `
        -MeasureContent:($Specification.Measurements -contains 'Lines') `
        -MeasureSource:($Specification.Measurements -contains 'SourceLines' -or $Specification.Measurements -contains 'ReswKeys') `
        -GroupBy $Specification.GroupBy `
        -BytesWarning $thresholds.BytesWarning `
        -BytesCritical $thresholds.BytesCritical `
        -LinesWarning $thresholds.LinesWarning `
        -LinesCritical $thresholds.LinesCritical `
        -SourceLinesWarning $thresholds.SourceLinesWarning `
        -SourceLinesCritical $thresholds.SourceLinesCritical

    return [pscustomobject]@{
        Kind          = 'Repository'
        Worktree      = $Worktree
        Specification = $Specification
        Totals        = $inventory.Totals
        Groups        = @($inventory.Groups)
        Findings      = @($inventory.Findings)
        Items         = @($inventory.Files)
        Diagnostics   = @($inventory.Diagnostics)
    }
}

function Format-MaintenanceFindingValue {
    param([Parameter(Mandatory)]$Finding)
    switch ($Finding.Measure) {
        'Bytes' { return Format-MaintenanceSize $Finding.Value }
        'EstimatedTokens' { return ('~{0} tokens' -f (Format-MaintenanceCount $Finding.Value)) }
        'SourceLines' { return ('{0} source lines' -f (Format-MaintenanceCount $Finding.Value)) }
        'Lines' { return ('{0} lines' -f (Format-MaintenanceCount $Finding.Value)) }
        default { return [string]$Finding.Value }
    }
}

function ConvertTo-TargetedStatisticsLines {
    param([Parameter(Mandatory)]$Result)

    $specification = $Result.Specification
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("Goal      $($specification.GoalLabel)")
    $lines.Add("Scope     $(Get-MaintenanceScopeLabel -ScopePath $specification.ScopePath)")
    $lines.Add("Worktree  $($Result.Worktree)")
    $lines.Add('')

    if ($Result.Kind -eq 'Repository') {
        $lines.Add(('Files     {0,-10} Size       {1}' -f `
            (Format-MaintenanceCount $Result.Totals.Files),
            (Format-MaintenanceSize $Result.Totals.Bytes)))
        if ($specification.Measurements -contains 'Lines') {
            $lines.Add(('Text      {0,-10} Lines      {1}' -f `
                (Format-MaintenanceCount $Result.Totals.MeasuredFiles),
                (Format-MaintenanceCount $Result.Totals.Lines)))
        }
        if ($specification.Measurements -contains 'SourceLines') {
            $lines.Add(('Source LOC {0,-10} RESW keys  {1}' -f `
                (Format-MaintenanceCount $Result.Totals.SourceLines),
                (Format-MaintenanceCount $Result.Totals.ReswKeys)))
        }
        if ($Result.Totals.LinkedFiles -gt 0) {
            $lines.Add(('Links      {0} counted without traversal' -f (Format-MaintenanceCount $Result.Totals.LinkedFiles)))
        }
    } else {
        $lines.Add(('Documents {0,-10} Size       {1}' -f `
            (Format-MaintenanceCount $Result.Totals.Files),
            (Format-MaintenanceSize $Result.Totals.Bytes)))
        $lines.Add(('Lines     {0,-10} Sections   {1}' -f `
            (Format-MaintenanceCount $Result.Totals.Lines),
            (Format-MaintenanceCount $Result.Totals.Sections)))
        $lines.Add(('Estimated tokens  {0}' -f (Format-MaintenanceCount $Result.Totals.EstimatedTokens)))
    }

    if ($Result.Groups.Count -gt 0) {
        $lines.Add('')
        $lines.Add("Grouped by $(Get-MaintenanceGroupingLabel -GroupBy $specification.GroupBy)")
        foreach ($group in $Result.Groups) {
            $suffix = if ($Result.Kind -eq 'Context') {
                '{0} documents  /  {1}  /  ~{2} tokens' -f `
                    (Format-MaintenanceCount $group.Files),
                    (Format-MaintenanceSize $group.Bytes),
                    (Format-MaintenanceCount $group.EstimatedTokens)
            } elseif ($specification.Measurements -notcontains 'Lines') {
                '{0} files  /  {1}' -f `
                    (Format-MaintenanceCount $group.Files),
                    (Format-MaintenanceSize $group.Bytes)
            } else {
                '{0} files  /  {1}  /  {2} lines' -f `
                    (Format-MaintenanceCount $group.Files),
                    (Format-MaintenanceSize $group.Bytes),
                    (Format-MaintenanceCount $group.Lines)
            }
            $lines.Add(('  {0,-18} {1}' -f $group.Name, $suffix))
        }
    }

    if ($specification.Goal -eq 'footprint' -and $Result.Items.Count -gt 0) {
        $lines.Add('')
        $lines.Add('Largest documents')
        foreach ($document in @($Result.Items | Sort-Object EstimatedTokens -Descending | Select-Object -First 10)) {
            $lines.Add(('  {0,-14} {1}' -f ('~{0} tokens' -f (Format-MaintenanceCount $document.EstimatedTokens)), $document.Path))
        }
    }

    $activityFindings = @($Result.Findings | Where-Object Category -eq 'Activity')
    $thresholdFindings = @($Result.Findings | Where-Object Category -eq 'Threshold')
    if ($activityFindings.Count -gt 0) {
        $lines.Add('')
        $lines.Add('Recent documents')
        foreach ($finding in @($activityFindings | Sort-Object `
            @{ Expression = { if ($_.Measure -eq 'Added') { 1 } else { 0 } }; Descending = $true }, `
            @{ Expression = 'Value'; Descending = $true })) {
            $lines.Add(('  {0,-18} {1}' -f (Format-MaintenanceFindingValue -Finding $finding), $finding.Path))
        }
    }
    if ($thresholdFindings.Count -gt 0) {
        $lines.Add('')
        $lines.Add($(if ($Result.Kind -eq 'Context') { 'Documents to review' } else { 'Files to review' }))
        foreach ($finding in @($thresholdFindings | Sort-Object `
            @{ Expression = { if ($_.Level -eq 'Critical') { 2 } elseif ($_.Level -eq 'Review') { 1 } else { 0 } }; Descending = $true }, `
            @{ Expression = 'Value'; Descending = $true })) {
            $lines.Add(('  {0,-18} {1}' -f (Format-MaintenanceFindingValue -Finding $finding), $finding.Path))
        }
    }
    if ($activityFindings.Count -eq 0 -and $thresholdFindings.Count -eq 0 -and $specification.Detail -eq 'Findings') {
        $lines.Add('')
        $lines.Add('No matching findings in this scope.')
    }

    if ($Result.Diagnostics.Count -gt 0) {
        $lines.Add('')
        $lines.Add('Safety notes')
        foreach ($diagnostic in $Result.Diagnostics) { $lines.Add("  $diagnostic") }
    }
    return @($lines)
}

function Invoke-MaintenanceTargetedScan {
    param(
        [Parameter(Mandatory)]$Specification,
        [Parameter(Mandatory)][string]$Worktree,
        [Parameter(Mandatory)][string]$LibDir
    )

    try {
        $result = if ($Specification.Kind -eq 'Repository') {
            Invoke-RepositoryTargetedInventory -Specification $Specification -Worktree $Worktree -LibDir $LibDir
        } else {
            Invoke-ContextTargetedInventory -Specification $Specification -Worktree $Worktree -LibDir $LibDir
        }
        return [pscustomobject]@{
            Succeeded = $true
            Title = "$($Specification.Kind) · $($Specification.GoalLabel)"
            Lines = @(ConvertTo-TargetedStatisticsLines -Result $result)
            Result = $result
        }
    } catch {
        return [pscustomobject]@{
            Succeeded = $false
            Title = "$($Specification.Kind) scan failed"
            Lines = @(Get-MaintenanceFailureLines -ErrorRecord $_)
            Result = $null
        }
    }
}
