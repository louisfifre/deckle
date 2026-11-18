<#
.SYNOPSIS
    Verify a branch on win-serv
.DESCRIPTION
    Sends a committed branch to the verification machine, win-serv, which
    builds it, runs every automatic test and answers green or red for that
    exact commit. Takes the branch checked out in this worktree, or the one
    named, and refuses when the worktree holding it has uncommitted changes.
    The branch travels over SSH into a repository of the verification
    machine's own; nothing goes to GitHub. One verification runs at a time
    there: a second request is refused and names the one in progress. The UI
    self-check is not part of the verdict. Exit code: 0 green, 1 red, 2 no
    verdict (the branch or the machine was not ready), 3 busy.
.NOTES
    section: Run
    order: 128
    action: Verify branch |
#>

# How it works:
#   1. The commit is the branch's tip on this machine; the worktree that holds
#      the branch must be clean, so what is verified is what is committed.
#   2. git push sends it over SSH, with the version tags it reaches (a build
#      refuses to run without one), to refs/heads/verify/<branch> of
#      %LOCALAPPDATA%\deckle-verify\deckle on the verification machine. That
#      repository is created on the first run and belongs to this script
#      alone: the machine's working clone of Deckle is fetched on a schedule
#      and used by the sessions hosted there.
#   3. One SSH call extracts scripts/verify-branch-server.ps1 from the pushed
#      commit and runs it, so the machine keeps no copy to update by hand. It
#      takes the machine's single verification slot, checks the commit out,
#      builds, tests and prints the verdict, which this script relays.
#
# The verdict names the full commit hash. A later commit needs its own run.

[CmdletBinding(PositionalBinding = $false)]
param(
    # The branch to verify. The branch checked out in -Target by default.
    [string]$Branch,
    # The worktree to read the branch from. The checkout this script lives in
    # by default.
    [string]$Target,
    # The SSH host of the verification machine, as ~/.ssh/config names it.
    [string]$Server = 'win-serv'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$started = Get-Date

function Stop-NoVerdict([string]$Message) {
    Write-Host "NO VERDICT  $Message" -ForegroundColor Yellow
    exit 2
}

$repo = if ($Target) { (Resolve-Path -LiteralPath $Target).Path } else { Split-Path -Parent $PSScriptRoot }

# --- Which commit ---------------------------------------------------------
if (-not $Branch) {
    $Branch = (& git -C $repo symbolic-ref --quiet --short HEAD 2>$null)
    if (-not $Branch) { Stop-NoVerdict "$repo is not on a branch (detached HEAD). Name one with -Branch." }
}
$sha = (& git -C $repo rev-parse --verify --quiet "refs/heads/$Branch^{commit}" 2>$null)
if (-not $sha) { Stop-NoVerdict "No local branch named '$Branch' in $repo." }
$sha = $sha.Trim()

# The worktree that holds the branch, if any, must have nothing uncommitted:
# the machine verifies the commit, and a green verdict must speak of what the
# person sees in that worktree.
$holder = $null
$current = $null
foreach ($line in (& git -C $repo worktree list --porcelain)) {
    if ($line -like 'worktree *') { $current = $line.Substring(9) }
    elseif ($line -eq "branch refs/heads/$Branch") { $holder = $current }
}
if ($holder) {
    $dirty = @(& git -C $holder status --porcelain --untracked-files=normal)
    if ($dirty.Count -gt 0) {
        Stop-NoVerdict "$holder has $($dirty.Count) uncommitted change(s) on $Branch. Commit or stash them first."
    }
}

Write-Host "Verifying $Branch at $sha on $Server..." -ForegroundColor DarkGray

# --- Send it --------------------------------------------------------------
# The path is the verification machine's %LOCALAPPDATA%\deckle-verify; the
# SSH account is the one that owns it. The SSH default shell there is
# PowerShell 7, so the remote commands below go as -EncodedCommand: nested
# quoting through ssh does not survive.
function ConvertTo-RemoteCommand([string]$Script) {
    "pwsh -NoProfile -NonInteractive -EncodedCommand $([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Script)))"
}

$prepare = @'
$ErrorActionPreference = 'Stop'
$repo = Join-Path $env:LOCALAPPDATA 'deckle-verify\deckle'
if (-not (Test-Path (Join-Path $repo '.git'))) {
    & git init --quiet $repo; if ($LASTEXITCODE) { exit 1 }
    # Deckle tracks symbolic links (.claude/skills), and a suite checks them;
    # without this, Git for Windows writes them as plain files.
    & git -C $repo config core.symlinks true
}
$repo.Replace('\', '/')
'@
$remotePath = (& ssh -o ConnectTimeout=20 $Server (ConvertTo-RemoteCommand $prepare) 2>$null | Select-Object -Last 1)
if ($LASTEXITCODE -ne 0 -or -not $remotePath) { Stop-NoVerdict "Could not reach $Server over SSH, or could not prepare its repository." }

$pushOutput = & git -C $repo push --quiet --force --follow-tags "${Server}:$($remotePath.Trim())" "${sha}:refs/heads/verify/$Branch" 2>&1
if ($LASTEXITCODE -ne 0) {
    $pushOutput | Select-Object -Last 5 | ForEach-Object { Write-Host "      $_" }
    Stop-NoVerdict "git push to $Server failed."
}
$sent = [int]((Get-Date) - $started).TotalSeconds

# --- Verify it ------------------------------------------------------------
# The server script comes from the pushed commit itself, written to a file of
# its own so two requests never share one. It is deleted once it has run.
$run = @"
`$ErrorActionPreference = 'Stop'
`$root = Join-Path `$env:LOCALAPPDATA 'deckle-verify'
`$runner = Join-Path `$root ('runner-' + [guid]::NewGuid().ToString('N') + '.ps1')
& git -C (Join-Path `$root 'deckle') show '${sha}:scripts/verify-branch-server.ps1' > `$runner
if (`$LASTEXITCODE -ne 0) { Write-Host 'NO VERDICT  The commit carries no scripts/verify-branch-server.ps1.'; exit 2 }
try {
    & pwsh -NoProfile -NonInteractive -File `$runner -Sha '$sha' -Branch '$($Branch.Replace("'", "''"))' -Root `$root
    exit `$LASTEXITCODE
} finally { Remove-Item -LiteralPath `$runner -ErrorAction SilentlyContinue }
"@
# Keep-alives every 30 s: a run lasts minutes and prints almost nothing until
# its verdict, and a dropped connection stops the run on the machine with it.
$output = [System.Collections.Generic.List[string]]::new()
& ssh -o ConnectTimeout=20 -o ServerAliveInterval=30 -o ServerAliveCountMax=4 $Server (ConvertTo-RemoteCommand $run) |
    ForEach-Object {
        $line = "$_"
        $output.Add($line)
        $color = switch -Regex ($line) { '^GREEN' { 'Green' } '^RED' { 'Red' } '^(BUSY|NO VERDICT)' { 'Yellow' } default { 'Gray' } }
        Write-Host $line -ForegroundColor $color
    }
$sshCode = $LASTEXITCODE

# The verdict is read from the output, not from the exit code: the SSH
# default shell on the machine is a pwsh -c that turns every failure into 1.
# A verdict counts only when it names the commit this run sent.
$minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
$verdictLine = $output | Where-Object { $_ -match '^(GREEN|RED|BUSY|NO VERDICT)\s' } | Select-Object -First 1
$code = switch -Regex ($verdictLine) {
    "^GREEN .* $sha$" { 0; break }
    "^RED .* $sha$" { 1; break }
    '^BUSY' { 3; break }
    '^NO VERDICT' { 2; break }
    default { -1 }
}
if ($code -lt 0) {
    if ($sshCode -eq 255) { Stop-NoVerdict "The SSH connection to $Server dropped after $minutes min; the run there stopped with it." }
    Stop-NoVerdict "$Server gave no verdict naming $sha (ssh exit code $sshCode)."
}
if ($code -le 1) { Write-Host "      Sent in $sent s, verdict after $minutes min." -ForegroundColor DarkGray }
exit $code
