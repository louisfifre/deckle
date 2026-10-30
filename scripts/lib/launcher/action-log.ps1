# Stateful normalization of PowerShell and native action output.

. (Join-Path (Split-Path -Parent $PSScriptRoot) 'script-output.ps1')

function Get-ActionLogRole {
    param([AllowNull()]$InputObject)

    if ($InputObject -isnot [System.Management.Automation.InformationRecord] -or
        $InputObject.MessageData -isnot [System.Management.Automation.HostInformationMessage]) {
        return $null
    }

    $roleProperty = $InputObject.MessageData.PSObject.Properties['OutputRole']
    if ($null -ne $roleProperty -and $roleProperty.Value -in $script:OutputRoleColors.Keys) {
        return [string]$roleProperty.Value
    }

    try {
        $hostColor = [ConsoleColor]$InputObject.MessageData.ForegroundColor
        foreach ($role in @('Error', 'Warning', 'Success', 'Action', 'Heading', 'Category', 'Muted')) {
            if ($hostColor -eq (Get-OutputColor -Role $role)) { return $role }
        }
    } catch {
        # Compatibility output from a host without console colors has no role.
    }
    return $null
}

function Get-ActionLogStream {
    param([AllowNull()]$InputObject)

    if ($InputObject -isnot [System.Management.Automation.InformationRecord] -or
        $InputObject.MessageData -isnot [System.Management.Automation.HostInformationMessage]) {
        return $null
    }
    $streamProperty = $InputObject.MessageData.PSObject.Properties['OutputStream']
    if ($null -ne $streamProperty) { return [string]$streamProperty.Value }
    return $null
}

function Get-ActionLogLevel {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Message,
        [AllowNull()]$InputObject
    )

    if ($Message -match '^\[summary\]') { return 'Summary' }

    $role = Get-ActionLogRole -InputObject $InputObject
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

function ConvertFrom-TerminalOutput {
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

function Get-ActionLogColor {
    param(
        [Parameter(Mandatory)][string]$Level,
        [AllowNull()]$InputObject
    )

    $role = Get-ActionLogRole -InputObject $InputObject
    if ($role -and $role -ne 'Body') { return Get-OutputColor -Role $role }

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
        'Error'   { Get-OutputColor -Role Error }
        'Warning' { Get-OutputColor -Role Warning }
        'Step'    { Get-OutputColor -Role Category }
        default   { $null }
    }
    return $color
}

function Get-ActionLogLevelRank {
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

function New-ActionLogSegment {
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

function New-ActionLogRecord {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Segments,
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][datetime]$Timestamp
    )

    if ($Segments.Count -eq 0) { return $null }

    $message = (@($Segments | ForEach-Object Text) -join '').TrimEnd()
    if ([string]::IsNullOrWhiteSpace($message)) { return $null }

    $level = @($Segments | Sort-Object { Get-ActionLogLevelRank -Level $_.Level } -Descending)[0].Level
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
                Text = $Matches[1]; Role = 'Category'; ForegroundColor = Get-OutputColor -Role Category
            }
            [pscustomobject]@{ Text = $Matches[2]; Role = 'Body'; ForegroundColor = $null }
        )
    } elseif ($presentationSegments.Count -eq 1 -and $null -eq $presentationSegments[0].ForegroundColor) {
        $presentationSegments[0].ForegroundColor = Get-ActionLogColor -Level $level -InputObject $null
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

function New-ActionLogCollector {
    param([Parameter(Mandatory)][string]$Source)

    return [pscustomobject]@{
        Source          = $Source
        Records         = [System.Collections.Generic.List[object]]::new()
        PendingSegments = [System.Collections.Generic.List[object]]::new()
        ReportedResult  = $null
        InputCount      = 0
    }
}

function Add-ActionLogItem {
    param(
        [Parameter(Mandatory)]$Collector,
        [AllowNull()]$InputObject,
        [datetime]$Timestamp = (Get-Date)
    )

    $Collector.InputCount++
    $isHostOutput = $InputObject -is [System.Management.Automation.InformationRecord] -and
        $InputObject.MessageData -is [System.Management.Automation.HostInformationMessage]
    $terminalText = ConvertFrom-TerminalOutput -InputObject $InputObject
    $parts = @([regex]::Split($terminalText, "\n"))
    $role = Get-ActionLogRole -InputObject $InputObject
    $stream = Get-ActionLogStream -InputObject $InputObject
    $hostColor = Get-ActionLogColor -Level Info -InputObject $InputObject

    for ($partIndex = 0; $partIndex -lt $parts.Count; $partIndex++) {
        $part = $parts[$partIndex]
        if ($part.Length -gt 0) {
            $partLevel = Get-ActionLogLevel -Message $part -InputObject $InputObject
            $Collector.PendingSegments.Add((New-ActionLogSegment `
                -Text $part -Role $role -Stream $stream -ForegroundColor $hostColor -Level $partLevel))
        }

        $hasEmbeddedNewline = $partIndex -lt ($parts.Count - 1)
        $endsLogicalLine = $hasEmbeddedNewline -or -not $isHostOutput -or -not [bool]$InputObject.MessageData.NoNewLine
        if ($endsLogicalLine) {
            $record = New-ActionLogRecord `
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

function Complete-ActionLog {
    param(
        [Parameter(Mandatory)]$Collector,
        [datetime]$Timestamp = (Get-Date)
    )

    $record = New-ActionLogRecord `
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

function ConvertTo-ActionLogRecords {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$InputObject,
        [Parameter(Mandatory)][string]$Source,
        [datetime]$Timestamp = (Get-Date)
    )

    $collector = New-ActionLogCollector -Source $Source
    foreach ($item in $InputObject) {
        Add-ActionLogItem -Collector $collector -InputObject $item -Timestamp $Timestamp | Out-Null
    }
    Complete-ActionLog -Collector $collector -Timestamp $Timestamp | Out-Null
    return @($collector.Records)
}

function ConvertTo-ActionLogLines {
    param(
        [Parameter(Mandatory)]$InputObject,
        [Parameter(Mandatory)][string]$Source,
        [datetime]$Timestamp = (Get-Date)
    )

    foreach ($record in @(ConvertTo-ActionLogRecords -InputObject $InputObject -Source $Source -Timestamp $Timestamp)) {
        $record.Message
    }
}

function ConvertTo-ActionLogDisplayLines {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Records)

    foreach ($record in $Records) {
        [pscustomobject]@{
            Text            = $record.Message
            ForegroundColor = $record.ForegroundColor
            Segments        = @($record.Segments)
        }
    }
}
