# Menu terminal session lifecycle.
$script:MenuSessionDepth = 0
$script:MenuSessionUsesAlternateScreen = $false
$script:MenuPointerInputDepth = 0
$script:MenuPreviousTreatControlCAsInput = $false

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
        [Console]::SetCursorPosition(0, 0)
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

    while ($script:MenuPointerInputDepth -gt 0) {
        Stop-MenuPointerInput
    }
    [Console]::CursorVisible = $true
    Write-Ansi "$([char]27)[0m"
    if ($script:MenuSessionUsesAlternateScreen) {
        Write-Ansi "$([char]27)[?1049l"
    }
    $script:MenuSessionUsesAlternateScreen = $false
}

function Start-MenuPointerInput {
    if (-not (Test-MenuPointerInputAvailable)) { return $false }

    $script:MenuPointerInputDepth++
    if ($script:MenuPointerInputDepth -eq 1) {
        $script:MenuPreviousTreatControlCAsInput = [Console]::TreatControlCAsInput
        [Console]::TreatControlCAsInput = $true
        $escape = [char]27
        Write-Ansi "$escape[?1000h$escape[?1006h"
    }
    return $true
}

function Stop-MenuPointerInput {
    if ($script:MenuPointerInputDepth -le 0) { return }

    $script:MenuPointerInputDepth--
    if ($script:MenuPointerInputDepth -eq 0) {
        $escape = [char]27
        Write-Ansi "$escape[?1000l$escape[?1006l"
        [Console]::TreatControlCAsInput = $script:MenuPreviousTreatControlCAsInput
    }
}
