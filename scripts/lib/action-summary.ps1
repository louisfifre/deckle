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
    Write-Host ('  {0,-14}: {1}' -f 'Workflow', $Workflow)
    Write-Host ('  {0,-14}: {1}' -f 'Result', $Result) -ForegroundColor $color

    if ($Details) {
        foreach ($key in $Details.Keys) {
            $value = $Details[$key]
            if ($null -eq $value -or $value -eq '') { continue }
            if ($value -is [array]) { $value = $value -join ', ' }
            Write-Host ('  {0,-14}: {1}' -f $key, $value)
        }
    }

    if ($Next -and $Next.Count -gt 0) {
        Write-Host ('  {0,-14}: {1}' -f 'Next', $Next[0]) -ForegroundColor DarkGray
        for ($i = 1; $i -lt $Next.Count; $i++) {
            Write-Host ('  {0,-14}  {1}' -f '', $Next[$i]) -ForegroundColor DarkGray
        }
    }
}

