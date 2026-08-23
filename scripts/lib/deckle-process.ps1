# Shared Deckle process control: find the running instance that lives in a
# build output directory, and stop it for scripts that replace the locked
# artifacts under artifacts\bin\Deckle.App.

# A running Deckle.exe holds its executable and every loaded assembly open, so
# a build writing into the directory it was launched from cannot replace the
# outputs that changed. Returns the running Deckle processes whose executable
# lives in -Directory or below it; an empty array means the build is free to
# write there. A process whose path cannot be read is not reported.
function Get-DeckleProcessInDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Directory
    )

    $root = [System.IO.Path]::GetFullPath($Directory).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    return @(Get-Process -Name Deckle -ErrorAction SilentlyContinue | Where-Object {
        $path = try { $_.Path } catch { $null }
        $path -and $path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)
    })
}

function Stop-DeckleProcess {
    [CmdletBinding()]
    param(
        [scriptblock]$WriteEvent = { param([string]$Role, [string]$Message) Write-Host $Message },
        [int]$TimeoutSeconds = 10
    )

    $runningDeckle = @(Get-Process -Name Deckle -ErrorAction SilentlyContinue)
    if ($runningDeckle.Count -eq 0) {
        & $WriteEvent 'Muted' 'No running Deckle.exe found'
        return
    }

    foreach ($proc in $runningDeckle) {
        & $WriteEvent 'Action' "Killing Deckle PID $($proc.Id)"
        try {
            $proc | Stop-Process -Force -ErrorAction Stop
        } catch {
            $stillExists = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
            if ($stillExists) { throw }
        }
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $stillRunning = @(Get-Process -Name Deckle -ErrorAction SilentlyContinue)
        if ($stillRunning.Count -eq 0) { break }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)

    if ($stillRunning.Count -gt 0) {
        $pids = ($stillRunning | ForEach-Object { $_.Id }) -join ', '
        throw "Deckle.exe did not exit within $TimeoutSeconds second(s): PID $pids"
    }

    $ids = @($runningDeckle | ForEach-Object { $_.Id })
    $stoppedPids = ($ids | ForEach-Object { $_ }) -join ', '
    & $WriteEvent 'Success' "Stopped Deckle.exe (PID $stoppedPids)"
}
