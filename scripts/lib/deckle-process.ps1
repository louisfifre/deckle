# Shared Deckle process control: find the running instance that lives in a
# build output directory, and stop it for scripts that replace the locked
# artifacts under artifacts\bin\Deckle.App.

# A running Deckle.exe holds its executable and every loaded assembly open, so
# a build writing into the directory it was launched from cannot replace the
# outputs that changed. A process whose path cannot be read is not reported.
function Get-DeckleProcessInDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        # The executable's process name: Deckle, or DeckleMail / DeckleFolders.
        [string]$ProcessName = 'Deckle'
    )

    $root = [System.IO.Path]::GetFullPath($Directory).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    return @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object {
        $path = try { $_.Path } catch { $null }
        $path -and $path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)
    })
}

function Stop-DeckleProcess {
    [CmdletBinding()]
    param(
        [scriptblock]$WriteEvent = { param([string]$Role, [string]$Message) Write-Host $Message },
        # Stops the processes of this executable only: the apps of the suite run side by side.
        [string]$ProcessName = 'Deckle',
        [int]$TimeoutSeconds = 10
    )

    $runningDeckle = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if ($runningDeckle.Count -eq 0) {
        & $WriteEvent 'Muted' "No running $ProcessName.exe found"
        return
    }

    foreach ($proc in $runningDeckle) {
        & $WriteEvent 'Action' "Killing $ProcessName PID $($proc.Id)"
        try {
            $proc | Stop-Process -Force -ErrorAction Stop
        } catch {
            $stillExists = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
            if ($stillExists) { throw }
        }
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $stillRunning = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
        if ($stillRunning.Count -eq 0) { break }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)

    if ($stillRunning.Count -gt 0) {
        $pids = ($stillRunning | ForEach-Object { $_.Id }) -join ', '
        throw "$ProcessName.exe did not exit within $TimeoutSeconds second(s): PID $pids"
    }

    $ids = @($runningDeckle | ForEach-Object { $_.Id })
    $stoppedPids = ($ids | ForEach-Object { $_ }) -join ', '
    & $WriteEvent 'Success' "Stopped $ProcessName.exe (PID $stoppedPids)"
}
