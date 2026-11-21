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

.NOTES
    The driver backup is reinstalled as administrator, so it lives in one folder
    directly under %ProgramData% that only administrators and SYSTEM can write.
    Any folder already there that is not provably theirs (owner, rights, content,
    no reparse point) is deleted and recreated with the right owner and rights
    in one step. There is no parameter to point the backup elsewhere: a folder
    the script does not control cannot be trusted.
#>

[CmdletBinding()]
param()

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
        $idx = $text.IndexOf(':')
        if ($idx -lt 0) { continue }
        $value = $text.Substring($idx + 1).Trim()
        # Match by value shape: the first oemNN.inf value is the published name,
        # and an entry is the target when any value equals the target INF.
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

# ── Backup directory ──────────────────────────────────────────────────────────

# The backup is reinstalled as administrator, so whoever can write into it can
# run code as administrator. It lives in ONE folder directly under %ProgramData%
# (a parent folder would be one more place a user could own), and the folder is
# trusted only when everything in it is provably administrators' and SYSTEM's.
# Anything else is deleted and recreated, because a folder pre-created by a user
# keeps its owner, its rights and its content, and Set-Acl on the folder alone
# changes none of the content.
$TrustedSids = @('S-1-5-32-544', 'S-1-5-18')   # Administrators, SYSTEM

# Any right that lets a holder change, add, remove or re-permission something.
$WriteRights = [Security.AccessControl.FileSystemRights]'WriteData, AppendData, WriteExtendedAttributes, WriteAttributes, DeleteSubdirectoriesAndFiles, Delete, ChangePermissions, TakeOwnership'

function Test-IsReparsePoint {
    param([IO.FileSystemInfo]$Item)
    return (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
}

# True when the item is not a link, is owned by a trusted SID, and no other SID
# holds a write right on it.
function Test-TrustedItem {
    param([IO.FileSystemInfo]$Item)
    if (Test-IsReparsePoint $Item) { return $false }
    $acl = $Item.GetAccessControl()   # not Get-Acl: no module to load
    $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($TrustedSids -notcontains $owner) { return $false }
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne 'Allow') { continue }
        if ($TrustedSids -contains $rule.IdentityReference.Value) { continue }
        if (($rule.FileSystemRights -band $WriteRights) -ne 0) { return $false }
    }
    return $true
}

# True when the folder and everything under it is trusted. A link is never
# followed: it makes the tree untrusted.
function Test-TrustedTree {
    param([string]$Dir)
    if (-not (Test-Path -LiteralPath $Dir)) { return $false }
    $root = Get-Item -LiteralPath $Dir -Force
    if (-not $root.PSIsContainer) { return $false }
    if (-not (Test-TrustedItem $root)) { return $false }
    foreach ($child in Get-ChildItem -LiteralPath $Dir -Force) {
        if (-not (Test-TrustedItem $child)) { return $false }
        if ($child.PSIsContainer -and -not (Test-TrustedTree $child.FullName)) { return $false }
    }
    return $true
}

# Deletes a tree without ever following a link: a junction or symlink is removed
# itself, its target is left alone (Remove-Item -Recurse can walk into one on
# Windows PowerShell 5.1).
function Remove-Tree {
    param([string]$Path)
    $item = Get-Item -LiteralPath $Path -Force
    if (Test-IsReparsePoint $item) {
        if ($item.PSIsContainer) { [IO.Directory]::Delete($Path) } else { [IO.File]::Delete($Path) }
        return
    }
    if ($item.PSIsContainer) {
        foreach ($child in Get-ChildItem -LiteralPath $Path -Force) { Remove-Tree $child.FullName }
        [IO.Directory]::Delete($Path)
    }
    else {
        [IO.File]::SetAttributes($Path, [IO.FileAttributes]::Normal)
        [IO.File]::Delete($Path)
    }
}

# Ends the run on an unrecoverable problem with the backup folder.
function Stop-BackupDir {
    param([string]$What)
    Write-Err $What
    Write-Host '  Nothing was reinstalled. Delete the folder by hand as Administrator' -ForegroundColor Red
    Write-Host "  ($BackupDir) and re-run this script." -ForegroundColor Red
    Read-Host '  Press Enter to close'
    exit 1
}

function Initialize-BackupDir {
    param([string]$Dir)

    if (Test-Path -LiteralPath $Dir) {
        if (Test-TrustedTree $Dir) {
            Write-Info 'Backup folder is administrator-only — kept as it is.'
            return
        }
        Write-Warn 'Backup folder is not administrator-only (owner, rights, content or a link) — deleting it.'
        try { Remove-Tree $Dir } catch { Stop-BackupDir "Could not delete the untrusted backup folder: $($_.Exception.Message)" }
    }

    # Created with its owner and rights in the same call: no moment where the
    # folder exists with the rights inherited from %ProgramData%.
    $admins = New-Object Security.Principal.SecurityIdentifier($TrustedSids[0])
    $system = New-Object Security.Principal.SecurityIdentifier($TrustedSids[1])
    $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $security = New-Object Security.AccessControl.DirectorySecurity
    $security.SetOwner($admins)
    $security.SetAccessRuleProtection($true, $false)
    foreach ($sid in @($admins, $system)) {
        $security.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
            $sid, 'FullControl', $inherit, 'None', 'Allow')))
    }
    try { (New-Object IO.DirectoryInfo($Dir)).Create($security) }
    catch { Stop-BackupDir "Could not create the backup folder: $($_.Exception.Message)" }

    if (-not (Test-TrustedTree $Dir) -or @(Get-ChildItem -LiteralPath $Dir -Force).Count -ne 0) {
        Stop-BackupDir 'The new backup folder is not empty and administrator-only as expected.'
    }
}

$BackupDir = Join-Path $env:ProgramData 'DeckleTrackpadDriverBackup'
Initialize-BackupDir $BackupDir
Write-Section 'Apple Magic Trackpad 2 — connection repair'
Write-Info "Driver INF       : $InfOriginalName"
Write-Info "Backup directory : $BackupDir"

Write-Section 'Step 1 — Locate and remove the driver from the store'

$published = Find-PublishedName

if ($published) {
    Write-Ok "Driver found in store as: $published"

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
    $backupInf = Find-BackupInf $BackupDir
    if ($backupInf) {
        Write-Warn 'Driver is not in the store, but a backup exists.'
        Write-Info 'The removal already happened on a previous run — continuing.'
    }
    else {
        Write-Err 'Driver not found in the store and no trusted backup exists.'
        Write-Host '  (A backup left in any other place, or by an older version, is not trusted.)' -ForegroundColor Red
        Write-Host '  The Apple trackpad driver does not appear to be installed.' -ForegroundColor Red
        Write-Host '  Pair the trackpad once (Windows will install the driver),' -ForegroundColor Red
        Write-Host '  then re-run this script.' -ForegroundColor Red
        Read-Host '  Press Enter to close'
        exit 1
    }
}

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

Write-Section 'Step 3 — Reinstall the driver onto the device'

if (-not (Test-TrustedTree $BackupDir)) {
    Stop-BackupDir 'The backup folder changed and is no longer administrator-only.'
}
$backupInf = Find-BackupInf $BackupDir
if (-not $backupInf) {
    Write-Err "Backup INF not found under: $BackupDir"
    Write-Host '  Cannot reinstall the driver without its backup. Restore the' -ForegroundColor Red
    Write-Host '  exported backup and re-run.' -ForegroundColor Red
    Read-Host '  Press Enter to close'
    exit 1
}

Write-Info "Installing driver: $backupInf"
& pnputil /add-driver "$backupInf" /install
Stop-OnError 'pnputil /add-driver'
Write-Ok 'Driver reinstalled.'
Write-Info 'The driver attaches to the paired BTHENUM device; the trackpad comes'
Write-Info 'up as "Apple Bluetooth Precision Trackpad" (HIDClass).'

Write-Section 'Done'
Write-Ok 'The trackpad should now pair persistently and expose full precision input.'
Write-Host ''
Write-Host '  REMEMBER: to reconnect later, switch the trackpad OFF/ON.' -ForegroundColor Yellow
Write-Host '  NEVER use "Remove device" — it re-triggers the pairing bug.' -ForegroundColor Yellow
Write-Host ''
Read-Host '  Press Enter to close'
