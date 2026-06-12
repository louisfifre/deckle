<#
.SYNOPSIS
    Repairs the Apple Magic Trackpad 2 Bluetooth pairing on Windows.

.DESCRIPTION
    The Apple Precision Touchpad driver (appleprecisiontrackpadbluetooth.inf,
    signed, v6.1.8000.6) has a documented bug: pairing does not hold while the
    driver sits in the driver store. The System event log shows "successfully
    paired" then "link key has been removed" ~10 seconds later.

    The fix:
      1. Export a backup of the driver, then remove it from the driver store.
      2. Power-cycle the trackpad and re-pair it as a generic mouse — without
         the driver present, the pairing holds.
      3. Re-add the driver directly onto the now-paired device; the trackpad
         comes up as "Apple Bluetooth Precision Trackpad" (HIDClass).

    This script is extracted and launched (elevated) by Deckle's trackpad
    module. It is safe to re-run: each step detects the current state.

.PARAMETER BackupDir
    Where the driver backup is exported. Defaults to the Deckle trackpad module
    directory under %LOCALAPPDATA% (or $env:DECKLE_DATA_ROOT when set).
#>

[CmdletBinding()]
param(
    [string]$BackupDir
)

$ErrorActionPreference = 'Stop'
$InfOriginalName = 'appleprecisiontrackpadbluetooth.inf'

# ── Helpers ──────────────────────────────────────────────────────────────────

function Write-Section {
    param([string]$Text)
    Write-Host ''
    Write-Host ('═' * 70) -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host ('═' * 70) -ForegroundColor Cyan
}

function Write-Ok    { param([string]$Text) Write-Host "  [OK]   $Text"   -ForegroundColor Green }
function Write-Info  { param([string]$Text) Write-Host "  [INFO] $Text"   -ForegroundColor Gray  }
function Write-Warn  { param([string]$Text) Write-Host "  [WARN] $Text"   -ForegroundColor Yellow }
function Write-Err   { param([string]$Text) Write-Host "  [FAIL] $Text"   -ForegroundColor Red   }

# pnputil /export-driver lays the package out either directly in the target
# directory or in a per-package subfolder depending on the Windows build —
# resolve the INF wherever it landed.
function Find-BackupInf {
    param([string]$Dir)
    if (-not (Test-Path $Dir)) { return $null }
    $found = Get-ChildItem -Path $Dir -Recurse -Filter $InfOriginalName -File -ErrorAction SilentlyContinue |
             Select-Object -First 1
    if ($found) { return $found.FullName }
    return $null
}

function Test-Admin {
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Stop-OnError {
    param([string]$What)
    if ($LASTEXITCODE -ne 0) {
        Write-Err "$What failed (exit code $LASTEXITCODE)."
        Write-Host ''
        Write-Host '  The procedure cannot continue safely. Review the output above,' -ForegroundColor Red
        Write-Host '  resolve the issue, and re-run this script.' -ForegroundColor Red
        Read-Host '  Press Enter to close'
        exit 1
    }
}

# Parses `pnputil /enum-drivers` output into a list of entries, then returns the
# published name (oemNN.inf) of the entry whose Original Name matches $InfOriginalName.
# Returns $null when no matching entry is found.
function Find-PublishedName {
    $output = & pnputil /enum-drivers 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Err 'pnputil /enum-drivers failed.'
        return $null
    }

    # Group the flat output into per-driver entries. pnputil separates entries
    # with a blank line; each entry has "Published Name:" and "Original Name:"
    # lines (label wording varies by locale, so match on the value, not the label).
    $entries = @()
    $current = @{}
    foreach ($line in $output) {
        $text = [string]$line
        if ([string]::IsNullOrWhiteSpace($text)) {
            if ($current.Count -gt 0) { $entries += [pscustomobject]$current; $current = @{} }
            continue
        }
        # Split on the first colon: "Label : value".
        $idx = $text.IndexOf(':')
        if ($idx -lt 0) { continue }
        $value = $text.Substring($idx + 1).Trim()
        # Capture the two values we care about by their shape: oemNN.inf for the
        # published name, and any *.inf for the original name. We key by line
        # order: the first .inf in an entry is the published name (oemNN.inf),
        # and we separately track any line whose value equals the target inf.
        if ($value -match '^oem\d+\.inf$' -and -not $current.ContainsKey('Published')) {
            $current['Published'] = $value
        }
        if ($value -ieq $InfOriginalName) {
            $current['IsTarget'] = $true
        }
    }
    if ($current.Count -gt 0) { $entries += [pscustomobject]$current }

    $match = $entries | Where-Object { $_.IsTarget -eq $true -and $_.Published } | Select-Object -First 1
    if ($match) { return $match.Published }
    return $null
}

# ── Elevation gate ───────────────────────────────────────────────────────────

if (-not (Test-Admin)) {
    Write-Err 'This script must run as Administrator.'
    Write-Host '  Re-launch it elevated (Deckle does this for you via the Settings button).' -ForegroundColor Red
    Read-Host '  Press Enter to close'
    exit 1
}

# ── Resolve backup directory ──────────────────────────────────────────────────

if (-not $BackupDir) {
    if ($env:DECKLE_DATA_ROOT) {
        $BackupDir = Join-Path $env:DECKLE_DATA_ROOT 'modules\trackpad\driver-backup'
    } else {
        $BackupDir = Join-Path $env:LOCALAPPDATA 'Deckle\modules\trackpad\driver-backup'
    }
}

Write-Section 'Apple Magic Trackpad 2 — connection repair'
Write-Info "Driver INF       : $InfOriginalName"
Write-Info "Backup directory : $BackupDir"

# ── Step a/b: locate the driver and (if present) back it up + remove it ───────

Write-Section 'Step 1 — Locate and remove the driver from the store'

$published = Find-PublishedName

if ($published) {
    Write-Ok "Driver found in store as: $published"

    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null

    Write-Info "Exporting backup to: $BackupDir"
    & pnputil /export-driver $published "$BackupDir"
    Stop-OnError 'pnputil /export-driver'
    Write-Ok 'Backup exported.'

    Write-Info "Removing driver from store: $published"
    & pnputil /delete-driver $published /uninstall
    Stop-OnError 'pnputil /delete-driver'
    Write-Ok 'Driver removed from the store.'
}
else {
    # Step c: not in the store. If a backup with the inf already exists, the
    # delete happened on a previous run — continue to the re-pair step.
    $backupInf = Find-BackupInf $BackupDir
    if ($backupInf) {
        Write-Warn 'Driver is not in the store, but a backup exists.'
        Write-Info 'The removal already happened on a previous run — continuing.'
    }
    else {
        Write-Err 'Driver not found in the store and no backup exists.'
        Write-Host '  The Apple trackpad driver does not appear to be installed.' -ForegroundColor Red
        Write-Host '  Pair the trackpad once (Windows will install the driver),' -ForegroundColor Red
        Write-Host '  then re-run this script.' -ForegroundColor Red
        Read-Host '  Press Enter to close'
        exit 1
    }
}

# ── Step d: interactive re-pair ───────────────────────────────────────────────

Write-Section 'Step 2 — Re-pair the trackpad (manual)'
Write-Host ''
Write-Host '  Do this now, in order:' -ForegroundColor White
Write-Host '    1. Switch the trackpad OFF, then ON again (the power switch on the back).' -ForegroundColor White
Write-Host '    2. Open  Settings > Bluetooth & devices  and pair the trackpad.' -ForegroundColor White
Write-Host '       With the driver removed, it pairs as a generic mouse and the' -ForegroundColor White
Write-Host '       pairing HOLDS this time.' -ForegroundColor White
Write-Host ''
Write-Host '  ┌────────────────────────────────────────────────────────────────┐' -ForegroundColor Red
Write-Host '  │  CRITICAL — NEVER use "Remove device" in Bluetooth settings.     │' -ForegroundColor Red
Write-Host '  │  That triggers the re-pairing bug of this driver. To reconnect   │' -ForegroundColor Red
Write-Host '  │  the trackpad, switching it OFF then ON is ALWAYS enough.        │' -ForegroundColor Red
Write-Host '  └────────────────────────────────────────────────────────────────┘' -ForegroundColor Red
Write-Host ''
Read-Host '  When the trackpad is paired and connected, press Enter to continue'

# ── Step e: reinstall the driver ──────────────────────────────────────────────

Write-Section 'Step 3 — Reinstall the driver onto the device'

$backupInf = Find-BackupInf $BackupDir
if (-not $backupInf) {
    Write-Err "Backup INF not found under: $BackupDir"
    Write-Host '  Cannot reinstall the driver without its backup. Locate the' -ForegroundColor Red
    Write-Host '  exported backup and re-run, or pass -BackupDir explicitly.' -ForegroundColor Red
    Read-Host '  Press Enter to close'
    exit 1
}

Write-Info "Installing driver: $backupInf"
& pnputil /add-driver "$backupInf" /install
Stop-OnError 'pnputil /add-driver'
Write-Ok 'Driver reinstalled.'
Write-Info 'The driver attaches to the paired BTHENUM device; the trackpad comes'
Write-Info 'up as "Apple Bluetooth Precision Trackpad" (HIDClass).'

# ── Step f: summary ───────────────────────────────────────────────────────────

Write-Section 'Done'
Write-Ok 'The trackpad should now pair persistently and expose full precision input.'
Write-Host ''
Write-Host '  REMEMBER: to reconnect later, switch the trackpad OFF/ON.' -ForegroundColor Yellow
Write-Host '  NEVER use "Remove device" — it re-triggers the pairing bug.' -ForegroundColor Yellow
Write-Host ''
Read-Host '  Press Enter to close'
