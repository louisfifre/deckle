# .NET build server inspection and shutdown.

function Format-ByteSize {
    param([int64]$Bytes)
    if     ($Bytes -ge 1GB) { '{0:N1} GB' -f ($Bytes / 1GB) }
    elseif ($Bytes -ge 1MB) { '{0:N1} MB' -f ($Bytes / 1MB) }
    elseif ($Bytes -ge 1KB) { '{0:N1} KB' -f ($Bytes / 1KB) }
    else                    { "$Bytes B" }
}

# Classifies one process from its name and command line. Kept apart from the
# process listing so the rule is testable on plain strings. A dotnet.exe is a
# build server only when its command line proves it; it is never guessed,
# because the same executable also runs the user's own applications.
function Get-BuildServerKind {
    param(
        [Parameter(Mandatory)][string]$ProcessName,
        [AllowEmptyString()][string]$CommandLine = ''
    )

    if ($ProcessName -eq 'VBCSCompiler') { return 'Roslyn' }
    if ($ProcessName -eq 'dotnet' -and (
        $CommandLine -match 'MSBuild\.dll' -or
        $CommandLine -match '/nodemode:1'
    )) { return 'MSBuild' }
    return $null
}

function Get-DotnetBuildServerProcesses {
    $processes = @(Get-Process -Name dotnet,VBCSCompiler -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) { return @() }

    $commandLines = @{}
    try {
        Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe' OR Name = 'VBCSCompiler.exe'" |
            ForEach-Object { $commandLines[[int]$_.ProcessId] = [string]$_.CommandLine }
    } catch {
        # Keep the cleanup usable in restricted shells. Without command lines
        # VBCSCompiler is still identified, but dotnet.exe is never guessed.
    }

    foreach ($process in $processes) {
        $commandLine = if ($commandLines.ContainsKey($process.Id)) { $commandLines[$process.Id] } else { '' }
        $kind = Get-BuildServerKind -ProcessName $process.ProcessName -CommandLine $commandLine
        if ($null -eq $kind) { continue }

        [pscustomobject]@{
            Id          = $process.Id
            Name        = $process.ProcessName
            Kind        = $kind
            WorkingSet  = [int64]$process.WorkingSet64
            CommandLine = $commandLine
        }
    }
}

function Format-DotnetBuildServerCount {
    param(
        [int]$Count,
        [int64]$WorkingSet
    )
    if ($Count -eq 0) { return 'None' }
    return ("{0} process(es), {1}" -f $Count, (Format-ByteSize $WorkingSet))
}

function Format-DotnetBuildServerList {
    param(
        [object[]]$Processes,
        [int]$Limit = 8
    )

    if (-not $Processes -or $Processes.Count -eq 0) { return 'None' }

    $items = @($Processes | Select-Object -First $Limit | ForEach-Object {
        "{0}#{1} ({2}, {3})" -f $_.Name, $_.Id, $_.Kind, (Format-ByteSize $_.WorkingSet)
    })
    $remaining = $Processes.Count - $items.Count
    if ($remaining -gt 0) { $items += "+ $remaining more" }
    return ($items -join ', ')
}

function Stop-DotnetBuildServers {
    [CmdletBinding()]
    param()

    $before = @(Get-DotnetBuildServerProcesses)

    $commandOutput = @(& dotnet build-server shutdown 2>&1)
    $exitCode = $LASTEXITCODE
    Start-Sleep -Milliseconds 500

    $after = @(Get-DotnetBuildServerProcesses)
    $afterIds = @{}
    foreach ($process in $after) { $afterIds[$process.Id] = $true }

    $stopped = @($before | Where-Object { -not $afterIds.ContainsKey($_.Id) })
    $succeeded = $exitCode -eq 0 -or $after.Count -eq 0

    [pscustomobject]@{
        ExitCode         = $exitCode
        Succeeded        = $succeeded
        Before           = $before
        After            = $after
        Stopped          = $stopped
        BeforeCount      = $before.Count
        StoppedCount     = $stopped.Count
        RemainingCount   = $after.Count
        BeforeWorkingSet = [int64](($before | Measure-Object WorkingSet -Sum).Sum ?? 0)
        StoppedWorkingSet = [int64](($stopped | Measure-Object WorkingSet -Sum).Sum ?? 0)
        RemainingWorkingSet = [int64](($after | Measure-Object WorkingSet -Sum).Sum ?? 0)
        BeforeSummary    = Format-DotnetBuildServerCount -Count $before.Count -WorkingSet ([int64](($before | Measure-Object WorkingSet -Sum).Sum ?? 0))
        StoppedSummary   = Format-DotnetBuildServerCount -Count $stopped.Count -WorkingSet ([int64](($stopped | Measure-Object WorkingSet -Sum).Sum ?? 0))
        RemainingSummary = Format-DotnetBuildServerCount -Count $after.Count -WorkingSet ([int64](($after | Measure-Object WorkingSet -Sum).Sum ?? 0))
        StoppedList      = Format-DotnetBuildServerList -Processes $stopped
        RemainingList    = Format-DotnetBuildServerList -Processes $after
        CommandOutput    = @($commandOutput | ForEach-Object { [string]$_ })
    }
}
