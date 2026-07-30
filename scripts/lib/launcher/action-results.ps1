# Captured launcher action output and persistent menu result formatting.

function Get-DeckleActionLogLevel {
    param(
        [Parameter(Mandatory)][string]$Message,
        [AllowNull()]$InputObject
    )

    if ($Message -match '^\[summary\]') { return 'Summary' }

    if ($InputObject -is [System.Management.Automation.InformationRecord] -and
        $InputObject.MessageData -is [System.Management.Automation.HostInformationMessage]) {
        switch ([string]$InputObject.MessageData.ForegroundColor) {
            'Red'     { return 'Error' }
            'Yellow'  { return 'Warning' }
            'Cyan'    { return 'Step' }
            'DarkCyan'{ return 'Step' }
        }
    }

    $meaningfulMessage = $Message `
        -replace '(?i)\b0\s+error(?:\(s\)|s)?\b', '' `
        -replace '(?i)\b0\s+warning(?:\(s\)|s)?\b', ''
    if ($meaningfulMessage -match '(?i)\b(error|failed|failure)\b') { return 'Error' }
    if ($meaningfulMessage -match '(?i)\bwarning\b') { return 'Warning' }
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

function Get-DeckleActionLogColor {
    param(
        [Parameter(Mandatory)][string]$Level,
        [Parameter(Mandatory)][string]$Message,
        [AllowNull()]$InputObject
    )

    if ($InputObject -is [System.Management.Automation.InformationRecord] -and
        $InputObject.MessageData -is [System.Management.Automation.HostInformationMessage]) {
        return [ConsoleColor]$InputObject.MessageData.ForegroundColor
    }

    if ($Message -match '^\s*Deckle\.\S+\s+->\s+\S.*$') {
        return [ConsoleColor]::Green
    }

    $color = switch ($Level) {
        'Error'   { [ConsoleColor]::Red }
        'Warning' { [ConsoleColor]::Yellow }
        'Step'    { [ConsoleColor]::Cyan }
        default   { [ConsoleColor]::Gray }
    }
    return $color
}

function ConvertTo-DeckleActionLogRecords {
    param(
        [Parameter(Mandatory)]$InputObject,
        [Parameter(Mandatory)][string]$Source,
        [datetime]$Timestamp = (Get-Date)
    )

    $terminalText = ConvertFrom-DeckleTerminalOutput -InputObject $InputObject
    foreach ($message in @($terminalText -split "\n")) {
        if ([string]::IsNullOrWhiteSpace($message)) { continue }
        $level = Get-DeckleActionLogLevel -Message $message -InputObject $InputObject
        [pscustomobject]@{
            Timestamp = $Timestamp
            Level     = $level
            Source    = $Source
            Message   = $message.TrimEnd()
            ForegroundColor = Get-DeckleActionLogColor -Level $level -Message $message -InputObject $InputObject
        }
    }
}

function Set-DeckleActionCursorVisible {
    param([Parameter(Mandatory)][bool]$Visible)

    if ([Console]::IsOutputRedirected) { return }
    try {
        [Console]::CursorVisible = $Visible
    } catch {
        # Non-interactive hosts can reject cursor visibility changes.
    }
}

function ConvertTo-DeckleActionLogLines {
    param(
        [Parameter(Mandatory)]$InputObject,
        [Parameter(Mandatory)][string]$Source,
        [datetime]$Timestamp = (Get-Date)
    )

    foreach ($record in @(ConvertTo-DeckleActionLogRecords -InputObject $InputObject -Source $Source -Timestamp $Timestamp)) {
        $record.Message
    }
}

function ConvertTo-DeckleActionLogDisplayLines {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Records
    )

    foreach ($record in $Records) {
        [pscustomobject]@{
            Text            = $record.Message
            ForegroundColor = $record.ForegroundColor
        }
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

    $records = [System.Collections.Generic.List[object]]::new()
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $refreshInterval = [timespan]::FromMilliseconds(100)
    $lastRefresh = [timespan]::Zero
    $succeeded = $true
    $reportedResult = $null

    Set-DeckleActionCursorVisible -Visible $false
    try {
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
                foreach ($record in @(ConvertTo-DeckleActionLogRecords -InputObject $_ -Source $Source)) {
                    $records.Add($record)
                }
                if (($timer.Elapsed - $lastRefresh) -ge $refreshInterval) {
                    $view = Update-GridStatusView `
                        -View $view `
                        -Title (Get-DeckleActionResultTitle -Label $Label -State Running -Elapsed $timer.Elapsed) `
                        -Lines @(ConvertTo-DeckleActionLogDisplayLines -Records @($records)) `
                        -Follow
                    $lastRefresh = $timer.Elapsed
                }
            }
        } catch {
            $succeeded = $false
            foreach ($record in @(ConvertTo-DeckleActionLogRecords -InputObject $_ -Source $Source)) {
                if ($records.Count -eq 0 -or $records[$records.Count - 1].Message -ne $record.Message) {
                    $records.Add($record)
                }
            }
        }
    } finally {
        $timer.Stop()
        Set-DeckleActionCursorVisible -Visible $true
    }

    if ($records.Count -eq 0) {
        $records.Add([pscustomobject]@{
            Timestamp = Get-Date
            Level     = 'Info'
            Source    = $Source
            Message   = 'The action produced no console output.'
            ForegroundColor = [ConsoleColor]::Gray
        })
    }

    $result = if (-not $succeeded) { 'Failed' } elseif ($reportedResult) { $reportedResult } else { 'Success' }
    $state = if ($result -ceq 'Success') { 'Succeeded' } else { $result }
    return [pscustomobject]@{
        Result    = $result
        Succeeded = $result -ceq 'Success'
        Title     = Get-DeckleActionResultTitle -Label $Label -State $state -Elapsed $timer.Elapsed
        Lines     = @(ConvertTo-DeckleActionLogDisplayLines -Records @($records))
        LogRecords = @($records)
    }
}
