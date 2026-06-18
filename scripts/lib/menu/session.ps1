# Menu terminal session lifecycle.
$script:MenuSessionDepth = 0; $script:MenuSessionUsesAlternateScreen = $false

function Write-Ansi {
    param([Parameter(Mandatory)][string]$Sequence)
    if ([Console]::IsOutputRedirected) { return }
    try {
        Write-Host $Sequence -NoNewline
    } catch {
        # Keep non-standard hosts usable; they can still use append mode.
    }
}

function Clear-MenuScreen {
    if ([Console]::IsOutputRedirected) { return }
    try {
        [Console]::Clear()
    } catch {
        Write-Host ""
    }
}

function Start-MenuSession {
    [CmdletBinding()]
    param([switch]$AlternateScreen)

    $script:MenuSessionDepth++
    if ($script:MenuSessionDepth -gt 1) { return }

    $script:MenuSessionUsesAlternateScreen = [bool]$AlternateScreen -and -not [Console]::IsOutputRedirected
    if ($script:MenuSessionUsesAlternateScreen) {
        Write-Ansi "$([char]27)[?1049h"
    }
    Clear-MenuScreen
}

function Stop-MenuSession {
    [CmdletBinding()]
    param()

    if ($script:MenuSessionDepth -le 0) { return }
    $script:MenuSessionDepth--
    if ($script:MenuSessionDepth -gt 0) { return }

    [Console]::CursorVisible = $true
    Write-Ansi "$([char]27)[0m"
    if ($script:MenuSessionUsesAlternateScreen) {
        Write-Ansi "$([char]27)[?1049l"
    }
    $script:MenuSessionUsesAlternateScreen = $false
}

function Suspend-MenuSession {
    [CmdletBinding()]
    param()

    while ($script:MenuSessionDepth -gt 0) {
        Stop-MenuSession
    }
}
