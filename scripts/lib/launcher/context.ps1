# Deckle launcher context, prompts, and session helpers.
function Use-DeckleCompactMenu {
    $script:DeckleMenuIsCompact = $true
}

function Get-DeckleMenuBannerStyle {
    if ($script:DeckleMenuIsCompact) { return 'Compact' }
    return 'Full'
}

function Start-DeckleMenuSession {
    Start-MenuSession -AlternateScreen
    $script:DeckleMenuSessionActive = $true
}

function Stop-DeckleMenuSession {
    if (-not $script:DeckleMenuSessionActive) { return }
    Stop-MenuSession
    $script:DeckleMenuSessionActive = $false
}

function Begin-DeckleAction {
    $script:DeckleActionCompleted = $true
    if ($script:DeckleMenuSessionActive) {
        Suspend-MenuSession
        $script:DeckleMenuSessionActive = $false
        if (-not [Console]::IsOutputRedirected) {
            Write-Host ''
        }
    }
}

function Clear-DeckleMenuScreen {
    if ([Console]::IsOutputRedirected) { return }
    try {
        [Console]::Clear()
    } catch {
        Write-Host ""
    }
}

function Get-WorktreeOrReturn {
    try {
        $wt = Select-Worktree -ContextDir $ScriptDir -ClearScreen -BannerStyle Compact
        return $wt
    } catch [System.OperationCanceledException] {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return $null
    }
}

function Read-YesNo {
    param(
        [Parameter(Mandatory)][string]$Question,
        [bool]$Default = $false,
        [string]$ConfirmLabel = 'Yes',
        [string]$CancelLabel = 'No',
        [switch]$Destructive
    )
    return (Select-YesNo -Question $Question -Default $Default -ConfirmLabel $ConfirmLabel -CancelLabel $CancelLabel -Destructive:$Destructive -BannerStyle Compact)
}

function Read-Optional {
    param([Parameter(Mandatory)][string]$Question)
    $answer = Read-Host $Question
    if ([string]::IsNullOrWhiteSpace($answer)) { return $null }
    return $answer.Trim()
}

function Get-CsprojVersion {
    param([Parameter(Mandatory)][string]$Worktree)
    $csproj = Join-Path $Worktree 'src\Deckle.App\Deckle.App.csproj'
    $m = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($m) { return $m.Matches[0].Groups[1].Value.Trim() }
    return $null
}
