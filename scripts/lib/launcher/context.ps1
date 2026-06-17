# Deckle launcher context, prompts, and session helpers.
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
        $wt = Select-Worktree -ContextDir $ScriptDir -ClearScreen
        Write-Host "Worktree: $wt" -ForegroundColor DarkGray
        return $wt
    } catch {
        Write-Host "Cancelled." -ForegroundColor DarkGray
        return $null
    }
}

function Read-YesNo {
    param(
        [Parameter(Mandatory)][string]$Question,
        [bool]$Default = $false
    )
    $hint = if ($Default) { '[Y/n]' } else { '[y/N]' }
    $ans  = Read-Host "$Question $hint"
    if ([string]::IsNullOrWhiteSpace($ans)) { return $Default }
    return ($ans -match '^(y|yes|o|oui)$')
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
