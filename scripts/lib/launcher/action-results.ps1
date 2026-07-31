# Launcher action output forwarding and persistent result formatting.

. (Join-Path (Split-Path -Parent $PSScriptRoot) 'script-output.ps1')

function Get-DeckleActionLogLevel {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Message,
        [AllowNull()]$InputObject
    )

    if ($Message -match '^\[summary\]') { return 'Summary' }

    if ($InputObject -is [System.Management.Automation.InformationRecord] -and
        $InputObject.MessageData -is [System.Management.Automation.HostInformationMessage]) {
        $hostColor = [ConsoleColor]$InputObject.MessageData.ForegroundColor
        if ($hostColor -eq (Get-DeckleOutputColor -Role Error)) { return 'Error' }
        if ($hostColor -eq (Get-DeckleOutputColor -Role Warning)) { return 'Warning' }
        if ($hostColor -eq (Get-DeckleOutputColor -Role Category)) { return 'Step' }
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
        [Parameter(Mandatory)][AllowEmptyString()][string]$Message,
        [AllowNull()]$InputObject
    )

    if ($InputObject -is [System.Management.Automation.InformationRecord] -and
        $InputObject.MessageData -is [System.Management.Automation.HostInformationMessage]) {
        try {
            $hostColor = [ConsoleColor]$InputObject.MessageData.ForegroundColor
            if ($hostColor -ne [Console]::ForegroundColor) { return $hostColor }
        } catch {
            # A host without console colors leaves body output uncolored.
        }
    }

    $color = switch ($Level) {
        'Error'   { Get-DeckleOutputColor -Role Error }
        'Warning' { Get-DeckleOutputColor -Role Warning }
        'Step'    { Get-DeckleOutputColor -Role Category }
        default   { $null }
    }
    return $color
}

function Get-DeckleActionLogLevelRank {
    param([Parameter(Mandatory)][string]$Level)

    switch ($Level) {
        'Error'   { 5 }
        'Warning' { 4 }
        'Summary' { 3 }
        'Step'    { 2 }
        default   { 1 }
    }
}

function New-DeckleActionLogSegment {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [AllowNull()]$ForegroundColor,
        [Parameter(Mandatory)][string]$Level
    )

    return [pscustomobject]@{
        Text            = $Text
        ForegroundColor = $ForegroundColor
        Level           = $Level
    }
}

function New-DeckleActionLogRecord {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Segments,
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][datetime]$Timestamp
    )

    if ($Segments.Count -eq 0) { return $null }

    $message = (@($Segments | ForEach-Object Text) -join '').TrimEnd()
    if ([string]::IsNullOrWhiteSpace($message)) { return $null }

    $level = @($Segments | Sort-Object { Get-DeckleActionLogLevelRank -Level $_.Level } -Descending)[0].Level
    $presentationSegments = @($Segments | ForEach-Object {
        [pscustomobject]@{
            Text            = [string]$_.Text
            ForegroundColor = $_.ForegroundColor
        }
    })

    if ($presentationSegments.Count -eq 1 -and
        $null -eq $presentationSegments[0].ForegroundColor -and
        $message -match '^(\[[a-z][a-z-]*\]\s*)(.*)$') {
        $presentationSegments = @(
            [pscustomobject]@{ Text = $Matches[1]; ForegroundColor = Get-DeckleOutputColor -Role Category }
            [pscustomobject]@{ Text = $Matches[2]; ForegroundColor = $null }
        )
    } elseif ($presentationSegments.Count -eq 1 -and $null -eq $presentationSegments[0].ForegroundColor) {
        $presentationSegments[0].ForegroundColor = Get-DeckleActionLogColor -Level $level -Message $message -InputObject $null
    }

    $lastSegment = $presentationSegments[$presentationSegments.Count - 1]
    $lastSegment.Text = $lastSegment.Text.TrimEnd()
    $lineColor = if ($presentationSegments.Count -eq 1) { $presentationSegments[0].ForegroundColor } else { $null }

    return [pscustomobject]@{
        Timestamp       = $Timestamp
        Level           = $level
        Source          = $Source
        Message         = $message
        ForegroundColor = $lineColor
        Segments        = $presentationSegments
    }
}

function ConvertTo-DeckleActionLogRecords {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$InputObject,
        [Parameter(Mandatory)][string]$Source,
        [datetime]$Timestamp = (Get-Date)
    )

    $pendingSegments = [System.Collections.Generic.List[object]]::new()

    foreach ($item in $InputObject) {
        $isHostOutput = $item -is [System.Management.Automation.InformationRecord] -and
            $item.MessageData -is [System.Management.Automation.HostInformationMessage]
        $terminalText = ConvertFrom-DeckleTerminalOutput -InputObject $item
        $parts = @([regex]::Split($terminalText, "\n"))
        $hostColor = Get-DeckleActionLogColor -Level Info -Message $terminalText -InputObject $item

        for ($partIndex = 0; $partIndex -lt $parts.Count; $partIndex++) {
            $part = $parts[$partIndex]
            if ($part.Length -gt 0) {
                $partLevel = Get-DeckleActionLogLevel -Message $part -InputObject $item
                $pendingSegments.Add((New-DeckleActionLogSegment -Text $part -ForegroundColor $hostColor -Level $partLevel))
            }

            $hasEmbeddedNewline = $partIndex -lt ($parts.Count - 1)
            $endsLogicalLine = $hasEmbeddedNewline -or -not $isHostOutput -or -not [bool]$item.MessageData.NoNewLine
            if ($endsLogicalLine) {
                $record = New-DeckleActionLogRecord -Segments @($pendingSegments) -Source $Source -Timestamp $Timestamp
                if ($null -ne $record) { $record }
                $pendingSegments.Clear()
            }
        }
    }

    $record = New-DeckleActionLogRecord -Segments @($pendingSegments) -Source $Source -Timestamp $Timestamp
    if ($null -ne $record) { $record }
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
            Segments        = @($record.Segments)
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

    $capturedOutput = [System.Collections.Generic.List[object]]::new()
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $succeeded = $true
    $reportedResult = $null
    $console = $null

    try {
        $console = Start-MenuActionConsole -Header "$Header · Running…"
        & $Action *>&1 | ForEach-Object {
            $capturedOutput.Add($_)
            Write-MenuActionOutput -InputObject $_
        }
    } catch {
        $succeeded = $false
        $capturedOutput.Add($_)
        Write-MenuActionOutput -InputObject $_
    } finally {
        $timer.Stop()
        Stop-MenuActionConsole -Console $console
    }

    $records = [System.Collections.Generic.List[object]]::new()
    foreach ($record in @(ConvertTo-DeckleActionLogRecords -InputObject @($capturedOutput) -Source $Source)) {
        if ($records.Count -eq 0 -or $records[$records.Count - 1].Message -ne $record.Message) {
            $records.Add($record)
        }
        if ($record.Message -match '^\s*Result\s*:\s*(Success|Failed|Partial|Skipped)\s*$') {
            $reportedResult = $Matches[1]
        }
    }

    if ($records.Count -eq 0) {
        $records.Add([pscustomobject]@{
            Timestamp = Get-Date
            Level     = 'Info'
            Source    = $Source
            Message   = 'The action produced no console output.'
            ForegroundColor = $null
            Segments  = @([pscustomobject]@{ Text = 'The action produced no console output.'; ForegroundColor = $null })
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
