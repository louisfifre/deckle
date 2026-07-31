# Shared end-of-action summaries for Deckle scripts.

. (Join-Path $PSScriptRoot 'script-output.ps1')

function Write-DeckleActionSummary {
    param(
        [Parameter(Mandatory)][string]$Workflow,
        [Parameter(Mandatory)][ValidateSet('Success', 'Failed', 'Partial', 'Skipped')][string]$Result,
        [Parameter(Mandatory)][string]$Sentence,
        [System.Collections.IDictionary]$Details,
        [string[]]$Next
    )

    $resultRole = switch ($Result) {
        'Success' { 'Success' }
        'Failed'  { 'Error' }
        'Partial' { 'Warning' }
        'Skipped' { 'Muted' }
    }

    Write-DeckleOutputLine -Segments @()
    Write-DeckleOutputLine -Segments @(
        New-DeckleOutputSegment -Text '[summary] ' -Role Category
        New-DeckleOutputSegment -Text $Sentence -Role Body
    )
    Write-DeckleOutputLine -Segments @()

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

    Write-DeckleOutputLine -Segments @(
        New-DeckleOutputSegment -Text ($rowFormat -f 'Workflow', $Workflow) -Role Body
    )
    $resultPrefix = $rowFormat -f 'Result', ''
    Write-DeckleOutputLine -Segments @(
        New-DeckleOutputSegment -Text $resultPrefix -Role Body
        New-DeckleOutputSegment -Text $Result -Role $resultRole
    )
    foreach ($row in $detailRows) {
        Write-DeckleOutputLine -Segments @(
            New-DeckleOutputSegment -Text ($rowFormat -f $row.Label, $row.Value) -Role Body
        )
    }

    if ($Next -and $Next.Count -gt 0) {
        Write-DeckleOutputLine -Segments @(
            New-DeckleOutputSegment -Text ($rowFormat -f 'Next', $Next[0]) -Role Muted
        )
        for ($i = 1; $i -lt $Next.Count; $i++) {
            Write-DeckleOutputLine -Segments @(
                New-DeckleOutputSegment -Text ($continuationFormat -f '', $Next[$i]) -Role Muted
            )
        }
    }
}
