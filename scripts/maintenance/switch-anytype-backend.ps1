<#
.SYNOPSIS
    Switch the Anytype backend
.DESCRIPTION
    Points the Anytype backend at the version the app pins, once that version
    is staged under versions in the provider folder. Run it with Deckle, the
    bot and Anytype Desktop closed: it copies the Anytype data folder and the
    CLI config folder to dated backups beside them, backs up active.json, then
    rewrites active.json in one atomic replace. Without -Apply it shows what
    it would do and writes nothing. It downloads nothing and never stops or
    starts a process; the next Deckle start spawns the new version.
.NOTES
    order: 240
    action: Preview / Anytype backend |
    action: Switch / Anytype backend | -Apply | danger | Copies the Anytype data, then points the backend at the pinned version. Deckle, the bot and Anytype Desktop must be closed.
#>

# Why a gesture of its own: the supervisor spawns what active.json names and
# never reads the pin (BackendInstallation.CurrentBundle), and the setup
# wizard, the only code that provisions the pin, does not run on a machine
# that already has a backend. A live copy of a running database is not a
# backup, so the switch refuses while anything that writes the data runs.

[CmdletBinding()]
param(
    # The version to activate. Defaults to the pin of this checkout's
    # src/Deckle.Anytype/Backend/BackendInstallation.cs.
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    # Copy, back up and write. Without it, nothing is written.
    [switch]$Apply,
    [string]$ProviderDirectory
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'anytype-backend.psm1') -Force

$Repo = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$Workflow = if ($Apply) { 'Switch Anytype backend' } else { 'Preview Anytype backend switch' }
if (-not $ProviderDirectory) { $ProviderDirectory = Get-AnytypeProviderDirectory }
$WorkflowOutput = New-WorkflowOutput -Category 'anytype'

trap {
    Write-ActionSummary -Workflow $Workflow -Result Failed `
        -Sentence 'The backend was not switched; active.json is as it was.' `
        -Details ([ordered]@{ Provider = $ProviderDirectory; Version = $Version; Error = $_.Exception.Message })
    exit 1
}

function Format-Size([long]$Bytes) { '{0:N0} MB' -f [Math]::Ceiling($Bytes / 1MB) }

if (-not $Version) {
    $source = Join-Path $Repo 'src\Deckle.Anytype\Backend\BackendInstallation.cs'
    $Version = (Get-AnytypeBackendPin -SourceText (Get-Content -LiteralPath $source -Raw)).Version
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'Look for what still runs'
$blockers = @(Get-AnytypeSwitchBlocker -ProviderDirectory $ProviderDirectory)
foreach ($blocker in $blockers) {
    Write-WorkflowMessage -Output $WorkflowOutput -Message "$($blocker.Kind) PID $($blocker.Id) $($blocker.Path)" -Role Muted
}

Write-WorkflowStep -Output $WorkflowOutput -Message "Check versions\$Version"
$plan = Invoke-AnytypeBackendSwitch -Version $Version -ProviderDirectory $ProviderDirectory `
    -DataDirectory (Get-AnytypeDataDirectory) -ConfigDirectory (Get-AnytypeConfigDirectory) `
    -Blockers $blockers -Apply:$Apply

$activationPath = Join-Path $ProviderDirectory 'active.json'
if ($plan.Result -eq 'Skipped') {
    Write-ActionSummary -Workflow $Workflow -Result Skipped `
        -Sentence "The backend already activates $Version; nothing to do." `
        -Details ([ordered]@{ Provider = $ProviderDirectory; Reported = $plan.Reported })
    return
}

$details = [ordered]@{
    Provider   = $ProviderDirectory
    Switch     = "$($plan.Previous) -> $Version"
    Reported   = $plan.Reported
}
foreach ($copy in $plan.Copies) {
    $details["Backup $(Split-Path -Leaf $copy.Source)"] = "$($copy.Destination) ($($copy.Files) files, $(Format-Size $copy.Bytes))"
}
if ($plan.ActivationBackup) { $details['Backup active.json'] = $plan.ActivationBackup }

$wayBack = if ($plan.ActivationBackup) {
    "Way back, Deckle closed: Copy-Item -LiteralPath '$($plan.ActivationBackup)' -Destination '$activationPath' -Force"
}
$check = "After Deckle starts: Get-CimInstance Win32_Process -Filter `"Name='anytype.exe'`" | Select-Object ProcessId, ExecutablePath  # must show versions\$Version"

if (-not $Apply) {
    # Deckle first: while it runs, its supervisor respawns a bot that was
    # stopped. The bot then, since it outlives a Deckle that closes.
    $next = @()
    if ($blockers | Where-Object Kind -eq 'Deckle') { $next += 'Quit Deckle from its tray icon.' }
    $bots = @($blockers | Where-Object Kind -eq 'bot' | ForEach-Object Id)
    if ($bots) { $next += "Stop the bot, which outlives Deckle: Stop-Process -Id $($bots -join ', ')" }
    if ($blockers | Where-Object Kind -eq 'Anytype Desktop') { $next += 'Quit Anytype Desktop from its tray icon.' }
    $next += "Then: pwsh -NoProfile -File `"$(Join-Path $ScriptDir 'switch-anytype-backend.ps1')`" -Apply"
    if ($blockers.Count -gt 0) {
        $details['Still running'] = ($blockers | Group-Object Kind | ForEach-Object {
            "$($_.Name) ($(($_.Group | ForEach-Object Id) -join ', '))"
        }) -join '; '
    }
    Write-ActionSummary -Workflow $Workflow -Result Skipped `
        -Sentence "Preview only, nothing was written. -Apply would copy the folders below, then activate $Version." `
        -Details $details -Next $next
    return
}

Write-ActionSummary -Workflow $Workflow -Result Success `
    -Sentence "The backend activates $Version; Deckle spawns it at its next start." `
    -Details $details -Next @($check, $wayBack)
