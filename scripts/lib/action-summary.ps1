# Shared end-of-action summaries for Deckle scripts.

function Write-DeckleActionSummary {
    param(
        [Parameter(Mandatory)][string]$Workflow,
        [Parameter(Mandatory)][ValidateSet('Success', 'Failed', 'Partial', 'Skipped')][string]$Result,
        [Parameter(Mandatory)][string]$Sentence,
        [System.Collections.IDictionary]$Details,
        [string[]]$Next
    )

    $color = switch ($Result) {
        'Success' { [ConsoleColor]::Green }
        'Failed'  { [ConsoleColor]::Red }
        'Partial' { [ConsoleColor]::Yellow }
        'Skipped' { [ConsoleColor]::DarkGray }
    }

    Write-Host ''
    Write-Host '[summary] ' -ForegroundColor $color -NoNewline
    Write-Host $Sentence -ForegroundColor $color
    Write-Host ''

    $detailRows = @()
    if ($Details) {
        foreach ($key in $Details.Keys) {
            $value = $Details[$key]
            if ($null -eq $value -or $value -eq '') { continue }
            if ($value -is [array]) { $value = $value -join ', ' }
            $detailRows += [pscustomobject]@{ Label = [string]$key; Value = $value }
        }
    }

    $labels = @('Workflow', 'Result') + @($detailRows | ForEach-Object Label)
    if ($Next -and $Next.Count -gt 0) { $labels += 'Next' }
    $labelWidth = [Math]::Max(14, ($labels | Measure-Object -Property Length -Maximum).Maximum)
    $rowFormat = '  {0,-' + $labelWidth + '}: {1}'
    $continuationFormat = '  {0,-' + $labelWidth + '}  {1}'

    Write-Host ($rowFormat -f 'Workflow', $Workflow)
    Write-Host ($rowFormat -f 'Result', $Result) -ForegroundColor $color
    foreach ($row in $detailRows) {
        Write-Host ($rowFormat -f $row.Label, $row.Value)
    }

    if ($Next -and $Next.Count -gt 0) {
        Write-Host ($rowFormat -f 'Next', $Next[0]) -ForegroundColor DarkGray
        for ($i = 1; $i -lt $Next.Count; $i++) {
            Write-Host ($continuationFormat -f '', $Next[$i]) -ForegroundColor DarkGray
        }
    }
}
