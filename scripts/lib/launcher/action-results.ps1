# Captured launcher action output and persistent menu result formatting.

function Get-DeckleActionLogLevel {
    param([Parameter(Mandatory)][string]$Message)

    $meaningfulMessage = $Message `
        -replace '(?i)\b0\s+error(?:\(s\)|s)?\b', '' `
        -replace '(?i)\b0\s+warning(?:\(s\)|s)?\b', ''
    if ($meaningfulMessage -match '(?i)\b(error|failed|failure)\b') { return 'Error' }
    if ($meaningfulMessage -match '(?i)\bwarning\b') { return 'Warning' }
    if ($Message -match '^\[summary\]') { return 'Summary' }
    if ($Message -match '^\[[a-z][a-z-]*\]') { return 'Step' }
    return 'Info'
}

function ConvertFrom-DeckleTerminalOutput {
    param([AllowNull()]$InputObject)

    $text = [string]$InputObject
    $escape = [string][char]27
    $text = $text `
        -replace "$escape\][^$([char]7)]*(?:$([char]7)|$escape\\)", '' `
        -replace "$escape\[[0-9;?]*[ -/]*[@-~]", ''
    return ($text `
        -replace "\r\n", "`n" `
        -replace "\r", "`n" `
        -replace "`t", '    ' `
        -replace '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]', '')
}

function ConvertTo-DeckleActionLogLines {
    param(
        [Parameter(Mandatory)]$InputObject,
        [Parameter(Mandatory)][string]$Source,
        [datetime]$Timestamp = (Get-Date)
    )

    $terminalText = ConvertFrom-DeckleTerminalOutput -InputObject $InputObject
    foreach ($message in @($terminalText -split "\n")) {
        if ([string]::IsNullOrWhiteSpace($message)) { continue }
        $level = Get-DeckleActionLogLevel -Message $message
        '{0}  {1,-7}  {2,-8}  {3}' -f $Timestamp.ToString('HH:mm:ss'), $level, $Source, $message.TrimEnd()
    }
}

function Get-DeckleActionResultTitle {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][ValidateSet('Running', 'Succeeded', 'Failed', 'Partial', 'Skipped')][string]$State,
        [Parameter(Mandatory)][timespan]$Elapsed
    )

    $seconds = $Elapsed.TotalSeconds.ToString('0.0', [Globalization.CultureInfo]::InvariantCulture)
    return '{0} {1} · {2} s' -f $Label, $State.ToLowerInvariant(), $seconds
}

function Invoke-DeckleMenuAction {
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][object[]]$MenuRows,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $refreshInterval = [timespan]::FromMilliseconds(100)
    $lastRefresh = [timespan]::Zero
    $succeeded = $true
    $reportedResult = $null

    $view = New-GridStatusView `
        -Header $Header `
        -Rows $MenuRows `
        -Title (Get-DeckleActionResultTitle -Label $Label -State Running -Elapsed $timer.Elapsed) `
        -Lines @('Waiting for output…') `
        -Footer 'Live output follows the latest line; controls return when the action completes' `
        -Follow

    try {
        & $Action *>&1 | ForEach-Object {
            $rawOutput = ConvertFrom-DeckleTerminalOutput -InputObject $_
            foreach ($rawLine in @($rawOutput -split "\n")) {
                if ($rawLine -match '^\s*Result\s*:\s*(Success|Failed|Partial|Skipped)\s*$') {
                    $reportedResult = $Matches[1]
                }
            }
            foreach ($line in @(ConvertTo-DeckleActionLogLines -InputObject $_ -Source $Source)) {
                $lines.Add($line)
            }
            if (($timer.Elapsed - $lastRefresh) -ge $refreshInterval) {
                $view = Update-GridStatusView `
                    -View $view `
                    -Title (Get-DeckleActionResultTitle -Label $Label -State Running -Elapsed $timer.Elapsed) `
                    -Lines @($lines) `
                    -Follow
                $lastRefresh = $timer.Elapsed
            }
        }
    } catch {
        $succeeded = $false
        foreach ($line in @(ConvertTo-DeckleActionLogLines -InputObject $_ -Source $Source)) {
            if ($lines.Count -eq 0 -or $lines[$lines.Count - 1] -ne $line) {
                $lines.Add($line)
            }
        }
    } finally {
        $timer.Stop()
    }

    if ($lines.Count -eq 0) {
        $lines.Add(('{0}  Info     {1,-8}  The action produced no console output.' -f (Get-Date).ToString('HH:mm:ss'), $Source))
    }

    $result = if (-not $succeeded) { 'Failed' } elseif ($reportedResult) { $reportedResult } else { 'Success' }
    $state = if ($result -ceq 'Success') { 'Succeeded' } else { $result }
    return [pscustomobject]@{
        Result    = $result
        Succeeded = $result -ceq 'Success'
        Title     = Get-DeckleActionResultTitle -Label $Label -State $state -Elapsed $timer.Elapsed
        Lines     = @($lines)
    }
}
