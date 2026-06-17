# Shared Deckle process control for scripts that need exclusive access to
# artifacts\bin\Deckle.App.

function Stop-DeckleProcess {
    [CmdletBinding()]
    param(
        [scriptblock]$WriteOk = { param([string]$Message) Write-Host $Message -ForegroundColor Green },
        [scriptblock]$WriteWarn = { param([string]$Message) Write-Host $Message -ForegroundColor Yellow },
        [int]$TimeoutSeconds = 10
    )

    $runningDeckle = @(Get-Process -Name Deckle -ErrorAction SilentlyContinue)
    if ($runningDeckle.Count -eq 0) {
        & $WriteOk 'No running Deckle.exe found'
        return
    }

    foreach ($proc in $runningDeckle) {
        & $WriteWarn "Killing Deckle PID $($proc.Id)"
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
    & $WriteOk "Stopped Deckle.exe (PID $stoppedPids)"
}
