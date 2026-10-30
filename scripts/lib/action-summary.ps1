# Shared end-of-action summaries for the scripts the launcher runs.

. (Join-Path $PSScriptRoot 'script-output.ps1')

function Write-ActionSummary {
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

    Write-OutputLine -Segments @()
    Write-OutputLine -Segments @(
        New-OutputSegment -Text '[summary] ' -Role Category
        New-OutputSegment -Text $Sentence -Role Body
    )
    Write-OutputLine -Segments @()

    $detailRows = @()
    if ($Details) {
        foreach ($key in $Details.Keys) {
            $value = $Details[$key]
            # A numeric 0 equals '' under PowerShell's loose comparison, so
            # the test goes through the string form to keep "Removed: 0".
            if ($null -eq $value -or [string]::IsNullOrEmpty([string]$value)) { continue }
            if ($value -is [array]) { $value = $value -join ', ' }
            $detailRows += [pscustomobject]@{ Label = [string]$key; Value = $value }
        }
    }

    $labels = @('Workflow', 'Result') + @($detailRows | ForEach-Object Label)
    if ($Next -and $Next.Count -gt 0) { $labels += 'Next' }
    $labelWidth = [Math]::Max(14, ($labels | Measure-Object -Property Length -Maximum).Maximum)
    $rowFormat = '  {0,-' + $labelWidth + '}: {1}'
    $continuationFormat = '  {0,-' + $labelWidth + '}  {1}'

    Write-OutputLine -Segments @(
        New-OutputSegment -Text ($rowFormat -f 'Workflow', $Workflow) -Role Body
    )
    $resultPrefix = $rowFormat -f 'Result', ''
    Write-OutputLine -Segments @(
        New-OutputSegment -Text $resultPrefix -Role Body
        New-OutputSegment -Text $Result -Role $resultRole
    )
    foreach ($row in $detailRows) {
        Write-OutputLine -Segments @(
            New-OutputSegment -Text ($rowFormat -f $row.Label, $row.Value) -Role Body
        )
    }

    if ($Next -and $Next.Count -gt 0) {
        Write-OutputLine -Segments @(
            New-OutputSegment -Text ($rowFormat -f 'Next', $Next[0]) -Role Muted
        )
        for ($i = 1; $i -lt $Next.Count; $i++) {
            Write-OutputLine -Segments @(
                New-OutputSegment -Text ($continuationFormat -f '', $Next[$i]) -Role Muted
            )
        }
    }
}
