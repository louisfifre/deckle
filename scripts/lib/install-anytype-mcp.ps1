# install-anytype-mcp.ps1
#
# Publishes the Anytype MCP host the AI clients (Claude Code, Codex, …) spawn,
# decoupling it from Deckle's build output. On Windows a running .exe is locked:
# while a client held artifacts\bin\…\Deckle.Anytype.Mcp.exe, any build that
# rewrote it failed with MSB3026, and Deckle couldn't be rebuilt or restarted.
#
# Layout — the Scoop model. The consumed binary lives under
# <UserDataRoot>\mcp\anytype\ :
#
#   mcp\anytype\
#     versions\<timestamp>\   each publish lands in a fresh, never-locked dir
#     current  ->  versions\<timestamp>   a JUNCTION to the active version
#
# .claude.json points ONCE, forever, at current\Deckle.Anytype.Mcp.exe. An
# update republishes into a NEW version dir and re-points the junction — it never
# overwrites a running exe, so the clients can stay open: live sessions keep the
# version they spawned (in their old dir, pruned once they let go of it), new
# spawns follow the junction to the fresh one.
#
# `publish` is the maintainer's act (project hard rule): this script IS that act
# for the MCP host. Run it by hand from the Setup menu, never from an agent.
#
# UserDataRoot resolution mirrors AppPaths.ResolveUserDataRoot and
# setup-assets.ps1 (same order):
#   1. -DataRoot   2. $env:DECKLE_DATA_ROOT   3. %LOCALAPPDATA%\Deckle\

[CmdletBinding()]
param(
    # Publish from a specific repo or worktree instead of the one containing
    # this script. The default (the script's own repo) is deliberate: the live
    # MCP should track the stable checkout, not an experimental worktree.
    [string]$Target,

    # Interactive worktree picker. Overrides -Target.
    [switch]$Pick,

    # Override the target UserDataRoot. Highest priority, ahead of
    # DECKLE_DATA_ROOT and %LOCALAPPDATA%\Deckle\.
    [string]$DataRoot,

    # Publish a new version + repoint the junction, but leave client configs
    # untouched.
    [switch]$NoConfig,

    # Only re-point the client config(s) at current\ (no rebuild). Useful to
    # heal a config without cutting a new version.
    [switch]$ConfigOnly,

    # Mount or remove the supervised management catalog (complete / archive /
    # delete) in the consumer's MCP args. 'on' writes `--management`; 'off' clears
    # it; omitted leaves the args untouched — so a routine republish never silently
    # strips a flag the maintainer set earlier. Default consumer = no destructive
    # tool.
    [ValidateSet('on', 'off')]
    [string]$Management
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot                                  # scripts/lib/
. (Join-Path $ScriptDir 'action-summary.ps1')

function Step($msg) { Write-Host "`n[mcp] $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "      $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "      $msg" -ForegroundColor Yellow }

$Workflow = 'Install Anytype MCP'
$RepoRoot = $null
$UserDataRoot = $null
$InstallRoot = $null
$CurrentLink = $null
$StableExe = $null
$versionId = $null
$fileCount = $null
$ConfigStatus = 'Pending'
$PrunedVersions = New-Object System.Collections.Generic.List[string]
$KeptVersions = New-Object System.Collections.Generic.List[string]
$ConfigResults = New-Object System.Collections.Generic.List[string]

trap {
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Anytype MCP install/update failed before completion." `
        -Details ([ordered]@{
            Worktree     = $RepoRoot
            'Install root' = $InstallRoot
            Version      = $versionId
            'Stable exe' = $StableExe
            Config       = $ConfigStatus
            'Config detail' = ($ConfigResults.ToArray() -join ', ')
            'Pruned versions' = ($PrunedVersions.ToArray() -join ', ')
            'Kept versions' = ($KeptVersions.ToArray() -join ', ')
            Management   = $(if ($Management) { $Management } else { 'unchanged' })
            Error        = $_.Exception.Message
        })
    throw
}

# ── RepoRoot resolution (mirrors publish-app.ps1) ─────────────────────────────
if ($Pick) {
    Import-Module (Join-Path $ScriptDir '_menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}
Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray

$Csproj = Join-Path $RepoRoot 'src\Deckle.Anytype.Mcp\Deckle.Anytype.Mcp.csproj'
if (-not (Test-Path $Csproj)) { throw "MCP csproj not found at $Csproj — is '$RepoRoot' a Deckle repo?" }

# ── Install layout — same UserDataRoot order as AppPaths.ResolveUserDataRoot ──
if ($DataRoot)                 { $UserDataRoot = $DataRoot }
elseif ($env:DECKLE_DATA_ROOT) { $UserDataRoot = $env:DECKLE_DATA_ROOT }
else                           { $UserDataRoot = Join-Path $env:LOCALAPPDATA 'Deckle' }

# Sibling of modules\anytype\ (where credentials.json lives). The repo's
# build/clean never reach this tree.
$InstallRoot = Join-Path $UserDataRoot 'mcp\anytype'
$VersionsDir = Join-Path $InstallRoot 'versions'
$CurrentLink = Join-Path $InstallRoot 'current'
# The one path .claude.json ever needs — stable across versions via the junction.
$StableExe   = Join-Path $CurrentLink 'Deckle.Anytype.Mcp.exe'

Step "install root: $InstallRoot"

# ── Publish a new version + repoint the junction (skipped with -ConfigOnly) ───
if (-not $ConfigOnly) {
    New-Item -ItemType Directory -Path $VersionsDir -Force | Out-Null

    # Version id = timestamp; a manual gesture never collides within a second.
    $versionId     = Get-Date -Format 'yyyyMMdd-HHmmss'
    $newVersionDir = Join-Path $VersionsDir $versionId
    $newExe        = Join-Path $newVersionDir 'Deckle.Anytype.Mcp.exe'

    # Framework-dependent publish: the run machine IS the build machine (the .NET
    # SDK is always present), so there's no need to bundle the runtime. We only
    # need a self-standing copy OUTSIDE artifacts\bin. The host is plain AnyCPU
    # console (no WindowsAppSDK, no PRI), so no RID/Platform juggling.
    Step "dotnet publish -> versions\$versionId (Release, framework-dependent)"
    & dotnet publish $Csproj `
        '-c:Release' `
        '-o' $newVersionDir `
        '-v:m' '-nologo' `
        '/nr:false' '/p:UseSharedCompilation=false'
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (code $LASTEXITCODE)" }
    if (-not (Test-Path $newExe)) { throw "MCP exe missing from publish output — $newExe" }
    $fileCount = (Get-ChildItem $newVersionDir -Recurse -File).Count
    Ok "published $fileCount files"

    # Repoint the junction. Delete only the reparse point (NEVER its target):
    # Directory.Delete(path,$false) drops the junction without following it, the
    # safe primitive here (Remove-Item -Recurse on a junction can shoot the
    # target — same hazard the worktree convention warns about).
    Step "point current -> versions\$versionId"
    if (Test-Path $CurrentLink) {
        $link = Get-Item $CurrentLink -Force
        if ($link.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            [System.IO.Directory]::Delete($CurrentLink, $false)
        } else {
            throw "$CurrentLink exists and is NOT a junction — refusing to touch it. Move it aside and re-run."
        }
    }
    New-Item -ItemType Junction -Path $CurrentLink -Target $newVersionDir | Out-Null
    Ok "current -> $newVersionDir"

    # Prune older versions. A dir whose exe a live client still holds won't
    # delete (locked) — skip it; the next run reaps it once that client respawns.
    $kept = 0
    Get-ChildItem $VersionsDir -Directory | Where-Object { $_.Name -ne $versionId } | ForEach-Object {
        try {
            Remove-Item $_.FullName -Recurse -Force
            Ok "pruned old version $($_.Name)"
            $PrunedVersions.Add($_.Name) | Out-Null
        } catch {
            $kept++
            Warn "kept $($_.Name) (still in use by a running client)"
            $KeptVersions.Add($_.Name) | Out-Null
        }
    }
    if ($kept) { Warn "$kept old version(s) left in place — they reap on the next run" }
}

if ($NoConfig) {
    Step 'config repoint skipped (-NoConfig)'
    $ConfigStatus = 'Skipped (-NoConfig)'
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Success `
        -Sentence "Anytype MCP was published and pointed at current; client config was left untouched." `
        -Details ([ordered]@{
            Worktree       = $RepoRoot
            'Install root' = $InstallRoot
            Version        = $versionId
            Files          = $fileCount
            Current        = $CurrentLink
            'Stable exe'   = $StableExe
            Config         = $ConfigStatus
            'Config detail' = $(if ($ConfigResults.Count -gt 0) { $ConfigResults.ToArray() } else { 'None' })
            'Pruned versions' = $(if ($PrunedVersions.Count -gt 0) { $PrunedVersions.ToArray() } else { 'None' })
            'Kept versions' = $(if ($KeptVersions.Count -gt 0) { $KeptVersions.ToArray() } else { 'None' })
            Management     = $(if ($Management) { $Management } else { 'unchanged' })
        }) `
        -Next @("Point clients at $StableExe if they are not already configured.")
    return
}

# ── Repoint AI-client config(s) at current\ — idempotent, done once for good ──
# Surgical text replace, NOT a JSON round-trip: .claude.json is a large, deeply
# nested global file and ConvertTo-Json would reflow / truncate it. The MCP exe
# name is unique in the file, so we retarget exactly the one "command" value
# ending in Deckle.Anytype.Mcp.exe. Backslashes are doubled to match how the
# existing JSON string escapes a Windows path. After the first run the value is
# already current\…exe, so this is a no-op forever after.
#
# The same surgical pass optionally mounts the supervised management flag in that
# server's args. The args array is anchored on the command, so we touch only the
# Anytype block; it relies on the client writing "args" right after "command"
# (Claude Code's layout). $Management $null leaves the args alone.
function Update-McpConfig {
    param([string]$ConfigPath, [string]$NewExe, [object]$Management)

    $name = Split-Path $ConfigPath -Leaf

    if (-not (Test-Path $ConfigPath)) {
        Warn "absent, skipped: $name"
        return "${name}: absent"
    }

    $raw   = Get-Content $ConfigPath -Raw
    $cmdRx = '"command"\s*:\s*"[^"]*Deckle\.Anytype\.Mcp\.exe"'
    if ($raw -notmatch $cmdRx) {
        Warn "no Anytype MCP command found in $name — repoint by hand if this client uses another mechanism"
        return "${name}: no Anytype command"
    }

    # 1) Retarget the command at current\…exe.
    $escaped = $NewExe -replace '\\', '\\'
    $updated = [regex]::Replace($raw, $cmdRx, ('"command": "{0}"' -f $escaped))

    # 2) Mount / clear --management in the SAME server's args (skipped when omitted).
    if ($null -ne $Management) {
        $argsValue = if ($Management) { '["--management"]' } else { '[]' }
        $argsRx    = '("command"\s*:\s*"[^"]*Deckle\.Anytype\.Mcp\.exe"\s*,\s*"args"\s*:\s*)\[[^\]]*\]'
        if ($updated -match $argsRx) {
            $updated = [regex]::Replace($updated, $argsRx, ('${1}' + $argsValue))
        } else {
            Warn "args array not found beside the Anytype command in $name — set --management by hand"
        }
    }

    if ($updated -ceq $raw) {
        Ok "already current: $name"
        return "${name}: already current"
    }

    # One-time backup beside the file, in case the swap ever needs reverting.
    $bak = "$ConfigPath.deckle-mcp.bak"
    if (-not (Test-Path $bak)) { Copy-Item $ConfigPath $bak }
    Set-Content -Path $ConfigPath -Value $updated -Encoding utf8 -NoNewline
    $note = if ($Management -eq $true) { ' + management' } elseif ($Management -eq $false) { ' - management' } else { '' }
    Ok "repointed $name -> current\$note"
    return "${name}: repointed$note"
}

Step 'repoint client configs'
$wantManagement = switch ($Management) { 'on' { $true } 'off' { $false } default { $null } }
$ConfigResults.Add((Update-McpConfig -ConfigPath (Join-Path $env:USERPROFILE '.claude.json') -NewExe $StableExe -Management $wantManagement)) | Out-Null
$ConfigStatus = 'Checked .claude.json'

# ── Done ──────────────────────────────────────────────────────────────────────
Step 'done'
$mgmtNote = switch ($Management) {
    'on'    { "  Management   : mounted — complete / archive / delete served to this consumer`n" }
    'off'   { "  Management   : not mounted`n" }
    default { '' }
}
Write-Host @"

  Live version : $CurrentLink  (junction)
  Consumed at  : $StableExe   (stable — .claude.json points here once)
$mgmtNote  Restart the AI client to pick up this version (and any args change):
  a live session keeps the host it spawned until it respawns.

  Re-run this any time to cut a new version: it republishes and re-points the
  junction without overwriting anything running. Open sessions keep the version
  they spawned until they restart; new spawns get the fresh one.

  Codex / other clients: point their MCP command at
    $StableExe

"@ -ForegroundColor Green

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence "Anytype MCP is installed behind the stable current junction." `
    -Details ([ordered]@{
        Worktree       = $RepoRoot
        'Install root' = $InstallRoot
        Version        = $(if ($ConfigOnly) { 'Not published (-ConfigOnly)' } else { $versionId })
        Files          = $(if ($ConfigOnly) { 'Not published (-ConfigOnly)' } else { $fileCount })
        Current        = $CurrentLink
        'Stable exe'   = $StableExe
        Config         = $ConfigStatus
        'Config detail' = $(if ($ConfigResults.Count -gt 0) { $ConfigResults.ToArray() } else { 'None' })
        'Pruned versions' = $(if ($PrunedVersions.Count -gt 0) { $PrunedVersions.ToArray() } else { 'None' })
        'Kept versions' = $(if ($KeptVersions.Count -gt 0) { $KeptVersions.ToArray() } else { 'None' })
        Management     = $(if ($Management) { $Management } else { 'unchanged' })
    }) `
    -Next @("Restart the AI client to pick up this version or args change.")
