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

function ConvertTo-RepositoryStatisticsLines {
    param([Parameter(Mandatory)]$Statistics)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("Worktree  $($Statistics.Worktree)")
    $lines.Add('')
    $lines.Add(('Repository {0} files  /  {1}  /  {2} lines' -f `
        (Format-MaintenanceCount $Statistics.Repository.Files),
        (Format-MaintenanceSize $Statistics.Repository.Bytes),
        (Format-MaintenanceCount $Statistics.Repository.Lines)))
    $lines.Add(('Scopes     {0}' -f (@($Statistics.Repository.Scopes) -join ', ')))
    $lines.Add('')
    $lines.Add(('Modules   {0,-8} Files     {1}' -f `
        (Format-MaintenanceCount $Statistics.Modules.Count),
        (Format-MaintenanceCount $Statistics.Totals.Files)))
    $lines.Add(('C# LOC    {0,-8} XAML LOC  {1}' -f `
        (Format-MaintenanceCount $Statistics.Totals.LocCs),
        (Format-MaintenanceCount $Statistics.Totals.LocXaml)))
    $lines.Add(('Total LOC {0,-8} RESW keys {1}' -f `
        (Format-MaintenanceCount $Statistics.Totals.LocTotal),
        (Format-MaintenanceCount $Statistics.Totals.ReswKeys)))
    $lines.Add('')
    $lines.Add(('Files over threshold  {0}' -f (Format-MaintenanceCount $Statistics.LargeFiles.Count)))
    foreach ($file in $Statistics.LargeFiles) {
        $measure = if ($file.Extension -eq '.cs') {
            '{0} LOC' -f (Format-MaintenanceCount $file.Loc)
        } else {
            '{0} lines' -f (Format-MaintenanceCount $file.RawLines)
        }
        $lines.Add(('  {0,-12} {1}' -f $measure, $file.RelativeRepo))
    }
    $lines.Add(('Resource inventories  {0}' -f (Format-MaintenanceCount $Statistics.ResourceFiles.Count)))
    foreach ($file in $Statistics.ResourceFiles) {
        $lines.Add(('  {0,-12} {1}' -f ("$($file.ReswKeys) keys"), $file.RelativeRepo))
    }
    $lines.Add('')
    $lines.Add('File types')
    foreach ($type in $Statistics.Repository.Types) {
        $lines.Add(('  {0,-12} {1,8} files  /  {2,10} lines  /  {3}' -f `
            $type.Type,
            (Format-MaintenanceCount $type.Files),
            (Format-MaintenanceCount $type.Lines),
            (Format-MaintenanceSize $type.Bytes)))
    }
    return @($lines)
}

function Get-RepositoryStatisticsLines {
    param(
        [Parameter(Mandatory)][string]$Worktree,
        [Parameter(Mandatory)][string]$LibDir
    )

    $statistics = & (Join-Path $LibDir 'stats.ps1') -Target $Worktree -PassThru 6>$null
    return @(ConvertTo-RepositoryStatisticsLines -Statistics $statistics)
}

function ConvertTo-ContextStatisticsLines {
    param([Parameter(Mandatory)]$Statistics)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("Worktree  $($Statistics.Worktree)")
    $lines.Add('')
    $lines.Add(('Documents {0,-8} Sections  {1}' -f `
        (Format-MaintenanceCount $Statistics.Totals.Documents),
        (Format-MaintenanceCount $Statistics.Totals.Sections)))
    $lines.Add(('Lines     {0,-8} Size      {1}' -f `
        (Format-MaintenanceCount $Statistics.Totals.Lines),
        (Format-MaintenanceSize $Statistics.Totals.Bytes)))
    $lines.Add(('Est. tokens {0}' -f (Format-MaintenanceCount $Statistics.Totals.EstimatedTokens)))
    $lines.Add(('Added      {0} in 1d  /  {1} in 7d  /  {2} in 30d' -f `
        (Format-MaintenanceCount $Statistics.Totals.Added1Day),
        (Format-MaintenanceCount $Statistics.Totals.Added7Days),
        (Format-MaintenanceCount $Statistics.Totals.Added30Days)))
    $lines.Add('')
    foreach ($group in $Statistics.Groups) {
        $lines.Add($group.LoadingMode)
        $lines.Add(('  {0} documents  /  {1} lines  /  {2}  /  ~{3} tokens' -f `
            (Format-MaintenanceCount $group.Files),
            (Format-MaintenanceCount $group.Lines),
            (Format-MaintenanceSize $group.Bytes),
            (Format-MaintenanceCount $group.EstimatedTokens)))
    }
    return @($lines)
}

function Get-ContextStatisticsLines {
    param(
        [Parameter(Mandatory)][string]$Worktree,
        [Parameter(Mandatory)][string]$LibDir
    )

    $statistics = & (Join-Path $LibDir 'inspect-context.ps1') -Target $Worktree -PassThru 6>$null
    return @(ConvertTo-ContextStatisticsLines -Statistics $statistics)
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

function Invoke-MaintenanceStatisticsScan {
    param(
        [ValidateSet('Repository', 'Context')]
        [string]$Kind,
        [Parameter(Mandatory)][string]$Worktree,
        [Parameter(Mandatory)][string]$LibDir
    )

    try {
        $lines = if ($Kind -eq 'Repository') {
            @(Get-RepositoryStatisticsLines -Worktree $Worktree -LibDir $LibDir)
        } else {
            @(Get-ContextStatisticsLines -Worktree $Worktree -LibDir $LibDir)
        }
        return [pscustomobject]@{ Succeeded = $true; Lines = $lines }
    } catch {
        return [pscustomobject]@{ Succeeded = $false; Lines = @(Get-MaintenanceFailureLines -ErrorRecord $_) }
    }
}
