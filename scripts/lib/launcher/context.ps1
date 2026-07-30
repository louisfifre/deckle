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
        [string[]]$ContextLines = @(),
        [switch]$Destructive
    )
    return (Select-YesNo -Question $Question -Default $Default -ConfirmLabel $ConfirmLabel -CancelLabel $CancelLabel -ContextLines $ContextLines -Destructive:$Destructive -ClearScreen -BannerStyle Compact)
}

function Read-Optional {
    param(
        [Parameter(Mandatory)][string]$Question,
        [string]$Header = 'Deckle',
        [string]$Label = 'Value',
        [string[]]$Lines = @()
    )
    return Read-MenuText -Header $Header -Title $Question -Label $Label -Lines $Lines -BannerStyle Compact
}

function Get-CsprojVersion {
    param([Parameter(Mandatory)][string]$Worktree)
    $csproj = Join-Path $Worktree 'src\Deckle.App\Deckle.App.csproj'
    $m = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($m) { return $m.Matches[0].Groups[1].Value.Trim() }
    return $null
}
