# Stateful normalization of PowerShell and native action output.

. (Join-Path (Split-Path -Parent $PSScriptRoot) 'script-output.ps1')

function Get-DeckleActionLogRole {
    param([AllowNull()]$InputObject)

    if ($InputObject -isnot [System.Management.Automation.InformationRecord] -or
        $InputObject.MessageData -isnot [System.Management.Automation.HostInformationMessage]) {
        return $null
    }

    $roleProperty = $InputObject.MessageData.PSObject.Properties['DeckleRole']
    if ($null -ne $roleProperty -and $roleProperty.Value -in $script:DeckleOutputColors.Keys) {
        return [string]$roleProperty.Value
    }

    try {
        $hostColor = [ConsoleColor]$InputObject.MessageData.ForegroundColor
        foreach ($role in @('Error', 'Warning', 'Success', 'Action', 'Heading', 'Category', 'Muted')) {
            if ($hostColor -eq (Get-DeckleOutputColor -Role $role)) { return $role }
        }
    } catch {
        # Compatibility output from a host without console colors has no role.
    }
    return $null
}

function Get-DeckleActionLogStream {
    param([AllowNull()]$InputObject)

    if ($InputObject -isnot [System.Management.Automation.InformationRecord] -or
        $InputObject.MessageData -isnot [System.Management.Automation.HostInformationMessage]) {
        return $null
    }
    $streamProperty = $InputObject.MessageData.PSObject.Properties['DeckleStream']
    if ($null -ne $streamProperty) { return [string]$streamProperty.Value }
    return $null
}

function Get-DeckleActionLogLevel {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Message,
        [AllowNull()]$InputObject
    )

    if ($Message -match '^\[summary\]') { return 'Summary' }

    $role = Get-DeckleActionLogRole -InputObject $InputObject
    if ($role -eq 'Error') { return 'Error' }
    if ($role -eq 'Warning') { return 'Warning' }
    if ($role -in @('Category', 'Heading')) { return 'Step' }

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
        [AllowNull()]$InputObject
    )

    $role = Get-DeckleActionLogRole -InputObject $InputObject
    if ($role -and $role -ne 'Body') { return Get-DeckleOutputColor -Role $role }

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

    $rank = switch ($Level) {
        'Error'   { 5 }
        'Warning' { 4 }
        'Summary' { 3 }
        'Step'    { 2 }
        default   { 1 }
    }
    return $rank
}

function New-DeckleActionLogSegment {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [AllowNull()][string]$Role,
        [AllowNull()][string]$Stream,
        [AllowNull()]$ForegroundColor,
        [Parameter(Mandatory)][string]$Level
    )

    return [pscustomobject]@{
        Text            = $Text
        Role            = $Role
        Stream          = $Stream
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
            Role            = $_.Role
            ForegroundColor = $_.ForegroundColor
        }
    })

    if ($presentationSegments.Count -eq 1 -and
        $null -eq $presentationSegments[0].ForegroundColor -and
        $message -match '^(\[[a-z][a-z-]*\]\s*)(.*)$') {
        $presentationSegments = @(
            [pscustomobject]@{
                Text = $Matches[1]; Role = 'Category'; ForegroundColor = Get-DeckleOutputColor -Role Category
            }
            [pscustomobject]@{ Text = $Matches[2]; Role = 'Body'; ForegroundColor = $null }
        )
    } elseif ($presentationSegments.Count -eq 1 -and $null -eq $presentationSegments[0].ForegroundColor) {
        $presentationSegments[0].ForegroundColor = Get-DeckleActionLogColor -Level $level -InputObject $null
    }

    $lastSegment = $presentationSegments[$presentationSegments.Count - 1]
    $lastSegment.Text = $lastSegment.Text.TrimEnd()
    $lineColor = if ($presentationSegments.Count -eq 1) { $presentationSegments[0].ForegroundColor } else { $null }
    $streams = @($Segments | ForEach-Object Stream | Where-Object { $_ } | Select-Object -Unique)
    $stream = if ($streams.Count -eq 1) { $streams[0] } elseif ($streams.Count -gt 1) { 'Mixed' } else { $null }

    return [pscustomobject]@{
        Timestamp       = $Timestamp
        Level           = $level
        Source          = $Source
        Stream          = $stream
        Message         = $message
        ForegroundColor = $lineColor
        Segments        = $presentationSegments
    }
}

function New-DeckleActionLogCollector {
    param([Parameter(Mandatory)][string]$Source)

    return [pscustomobject]@{
        Source          = $Source
        Records         = [System.Collections.Generic.List[object]]::new()
        PendingSegments = [System.Collections.Generic.List[object]]::new()
        ReportedResult  = $null
        InputCount      = 0
    }
}

function Add-DeckleActionLogItem {
    param(
        [Parameter(Mandatory)]$Collector,
        [AllowNull()]$InputObject,
        [datetime]$Timestamp = (Get-Date)
    )

    $Collector.InputCount++
    $isHostOutput = $InputObject -is [System.Management.Automation.InformationRecord] -and
        $InputObject.MessageData -is [System.Management.Automation.HostInformationMessage]
    $terminalText = ConvertFrom-DeckleTerminalOutput -InputObject $InputObject
    $parts = @([regex]::Split($terminalText, "\n"))
    $role = Get-DeckleActionLogRole -InputObject $InputObject
    $stream = Get-DeckleActionLogStream -InputObject $InputObject
    $hostColor = Get-DeckleActionLogColor -Level Info -InputObject $InputObject

    for ($partIndex = 0; $partIndex -lt $parts.Count; $partIndex++) {
        $part = $parts[$partIndex]
        if ($part.Length -gt 0) {
            $partLevel = Get-DeckleActionLogLevel -Message $part -InputObject $InputObject
            $Collector.PendingSegments.Add((New-DeckleActionLogSegment `
                -Text $part -Role $role -Stream $stream -ForegroundColor $hostColor -Level $partLevel))
        }

        $hasEmbeddedNewline = $partIndex -lt ($parts.Count - 1)
        $endsLogicalLine = $hasEmbeddedNewline -or -not $isHostOutput -or -not [bool]$InputObject.MessageData.NoNewLine
        if ($endsLogicalLine) {
            $record = New-DeckleActionLogRecord `
                -Segments @($Collector.PendingSegments) -Source $Collector.Source -Timestamp $Timestamp
            if ($null -ne $record) {
                $Collector.Records.Add($record)
                if ($record.Message -match '^\s*Result\s*:\s*(Success|Failed|Partial|Skipped)\s*$') {
                    $Collector.ReportedResult = $Matches[1]
                }
                $record
            }
            $Collector.PendingSegments.Clear()
        }
    }
}

function Complete-DeckleActionLog {
    param(
        [Parameter(Mandatory)]$Collector,
        [datetime]$Timestamp = (Get-Date)
    )

    $record = New-DeckleActionLogRecord `
        -Segments @($Collector.PendingSegments) -Source $Collector.Source -Timestamp $Timestamp
    if ($null -ne $record) {
        $Collector.Records.Add($record)
        if ($record.Message -match '^\s*Result\s*:\s*(Success|Failed|Partial|Skipped)\s*$') {
            $Collector.ReportedResult = $Matches[1]
        }
        $record
    }
    $Collector.PendingSegments.Clear()
}

function ConvertTo-DeckleActionLogRecords {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$InputObject,
        [Parameter(Mandatory)][string]$Source,
        [datetime]$Timestamp = (Get-Date)
    )

    $collector = New-DeckleActionLogCollector -Source $Source
    foreach ($item in $InputObject) {
        Add-DeckleActionLogItem -Collector $collector -InputObject $item -Timestamp $Timestamp | Out-Null
    }
    Complete-DeckleActionLog -Collector $collector -Timestamp $Timestamp | Out-Null
    return @($collector.Records)
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
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Records)

    foreach ($record in $Records) {
        [pscustomobject]@{
            Text            = $record.Message
            ForegroundColor = $record.ForegroundColor
            Segments        = @($record.Segments)
        }
    }
}
