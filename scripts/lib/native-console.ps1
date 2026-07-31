# Native process invocation that preserves each output line for launcher history.

. (Join-Path $PSScriptRoot 'script-output.ps1')

function Write-DeckleNativeConsoleLine {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][ValidateSet('StdOut', 'StdErr')][string]$Stream
    )

    Write-DeckleOutputFragment `
        -Text $Text `
        -Role Body `
        -Tags @('Deckle.Output', 'Deckle.Native') `
        -Metadata @{ DeckleStream = $Stream }
}

function Invoke-DeckleConsoleProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @()
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) {
        $startInfo.ArgumentList.Add([string]$argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $stdoutDone = $false
        $stderrDone = $false
        $stdoutTask = $process.StandardOutput.ReadLineAsync()
        $stderrTask = $process.StandardError.ReadLineAsync()

        while (-not $stdoutDone -or -not $stderrDone) {
            $madeProgress = $false

            if (-not $stdoutDone -and $stdoutTask.IsCompleted) {
                $line = $stdoutTask.GetAwaiter().GetResult()
                if ($null -eq $line) {
                    $stdoutDone = $true
                } else {
                    Write-DeckleNativeConsoleLine -Text $line -Stream StdOut
                    $stdoutTask = $process.StandardOutput.ReadLineAsync()
                }
                $madeProgress = $true
            }

            if (-not $stderrDone -and $stderrTask.IsCompleted) {
                $line = $stderrTask.GetAwaiter().GetResult()
                if ($null -eq $line) {
                    $stderrDone = $true
                } else {
                    Write-DeckleNativeConsoleLine -Text $line -Stream StdErr
                    $stderrTask = $process.StandardError.ReadLineAsync()
                }
                $madeProgress = $true
            }

            if (-not $madeProgress) {
                Start-Sleep -Milliseconds 10
            }
        }

        $process.WaitForExit()
        return $process.ExitCode
    } finally {
        $process.Dispose()
    }
}
