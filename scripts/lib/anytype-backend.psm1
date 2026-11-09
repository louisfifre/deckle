# The Anytype backend as the app pins it and as this machine activates it.
#
# The app is the source of truth for both halves: the pin is
# BackendInstallation.CurrentBundle in src/Deckle.Anytype/Backend/
# BackendInstallation.cs, read here from its text, and the activation is the
# active.json that BackendProviderStore writes and the supervisor reads at
# every spawn. The runtime launches what active.json names and never reads
# the pin; only the install wizard provisions the pin. So a new pin reaches a
# machine that already has a backend only through someone's gesture, and
# these functions exist to make the gap visible and to make that gesture safe.
# Nothing here downloads, starts or stops anything.

$script:ExecutableName = 'anytype.exe'

# BackendInstallation.ProvidersDirectory, from InstallPaths.DefaultProvidersDir.
function Get-AnytypeProviderDirectory {
    return Join-Path $env:LOCALAPPDATA 'Programs\Deckle Providers\Anytype'
}

# Deckle spawns the backend with no data path and no environment of its own
# (BackendProcess.Spawn), so the CLI's defaults apply. anytype-cli
# core/config/constants.go, identical in v0.5.0 and v0.5.2: the database lives
# in DATA_PATH when set, else %APPDATA%\anytype\data on Windows, and
# %USERPROFILE%\.anytype holds config.json (the stored account) and the logs.
# Anytype Desktop keeps its own accounts in the same %APPDATA%\anytype\data.
function Get-AnytypeDataDirectory {
    foreach ($scope in 'Process', 'User', 'Machine') {
        $value = [Environment]::GetEnvironmentVariable('DATA_PATH', $scope)
        if ($value) { return $value }
    }
    return Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'anytype\data'
}

function Get-AnytypeConfigDirectory {
    return Join-Path ([Environment]::GetFolderPath('UserProfile')) '.anytype'
}

# Reads CurrentBundle out of the C# text. Pass the text rather than a path so a
# caller can read the pin of any commit through git show, checked out or not.
function Get-AnytypeBackendPin {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$SourceText)

    $declaration = [regex]::Match(
        $SourceText,
        'CurrentBundle\s*\{\s*get;\s*\}\s*=\s*new\s*\((?<body>.*?)\);',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $declaration.Success) { throw 'BackendInstallation.CurrentBundle declaration not found' }
    $body = $declaration.Groups['body'].Value

    $fields = [ordered]@{}
    foreach ($name in 'Version', 'Url', 'Sha256') {
        $match = [regex]::Match($body, "$name\s*:\s*`"(?<value>[^`"]*)`"")
        if (-not $match.Success) { throw "BackendInstallation.CurrentBundle.$name not found" }
        $fields[$name] = $match.Groups['value'].Value
    }
    $size = [regex]::Match($body, 'SizeBytes\s*:\s*(?<value>[0-9_]+)L?')
    if (-not $size.Success) { throw 'BackendInstallation.CurrentBundle.SizeBytes not found' }
    $fields['SizeBytes'] = [long]($size.Groups['value'].Value -replace '_', '')
    return [pscustomobject]$fields
}

# The activation as BackendProviderStore.ResolveActiveExecutable reads it: a
# manifest whose RelativeExecutable is not versions/<Version>/anytype.exe, or
# whose executable is missing, activates nothing. Returns $null when there is
# no active.json at all.
function Get-AnytypeBackendActivation {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ProviderDirectory)

    $path = Join-Path $ProviderDirectory 'active.json'
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $version = $null
    $relative = $null
    try {
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $version = [string]$manifest.Version
        $relative = [string]$manifest.RelativeExecutable
    } catch {
        return [pscustomobject]@{ Path = $path; Version = $null; Executable = $null; Valid = $false }
    }
    $executable = if ($version) { Join-Path $ProviderDirectory "versions\$version\$script:ExecutableName" }
    $valid = [bool]$version -and
        ($relative -replace '\\', '/') -ieq "versions/$version/$script:ExecutableName" -and
        (Test-Path -LiteralPath $executable)
    return [pscustomobject]@{ Path = $path; Version = $version; Executable = $executable; Valid = $valid }
}

# One line for a summary: whether this machine runs the backend the app pins.
# State is aligned, differs, staged (the pinned version is on disk but not
# active), absent (no activation: the wizard provisions the pin) or invalid.
function Get-AnytypeBackendPinStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PinnedVersion,
        [string]$ProviderDirectory = (Get-AnytypeProviderDirectory)
    )

    $activation = Get-AnytypeBackendActivation -ProviderDirectory $ProviderDirectory
    $pinnedExe = Join-Path $ProviderDirectory "versions\$PinnedVersion\$script:ExecutableName"
    $pinnedOnDisk = Test-Path -LiteralPath $pinnedExe
    $switch = 'scripts/maintenance/switch-anytype-backend.ps1 -Apply, Deckle, the bot and Anytype Desktop closed'

    if ($null -eq $activation) {
        $state = 'absent'
        $line = "pin $PinnedVersion, nothing activated: the setup wizard installs it"
    } elseif (-not $activation.Valid) {
        $state = 'invalid'
        $line = "pin $PinnedVersion, active.json activates nothing: the setup wizard installs the pin"
    } elseif ($activation.Version -ieq $PinnedVersion) {
        $state = 'aligned'
        $line = "$PinnedVersion, as pinned"
    } elseif ($pinnedOnDisk) {
        $state = 'staged'
        $line = "active $($activation.Version), pin $PinnedVersion staged: switch with $switch"
    } else {
        $state = 'differs'
        $line = "active $($activation.Version), pin $PinnedVersion not on disk: stage it under versions\$PinnedVersion, then $switch"
    }
    return [pscustomobject]@{
        State   = $state
        Pinned  = $PinnedVersion
        Active  = if ($activation) { $activation.Version }
        Line    = $line
    }
}

# The processes that must be gone before the switch: Deckle (its supervisor
# would respawn or adopt a backend), every anytype-cli serve (the bot outlives
# a Deckle that closes) and Anytype Desktop with its helper, which write to
# the same %APPDATA%\anytype\data a backup copies. Desktop's Anytype.exe and
# the CLI's anytype.exe share a process name; the path tells them apart.
function Get-AnytypeSwitchBlocker {
    [CmdletBinding()]
    param([string]$ProviderDirectory = (Get-AnytypeProviderDirectory))

    $providerRoot = [System.IO.Path]::GetFullPath($ProviderDirectory).TrimEnd('\') + '\'
    foreach ($process in @(Get-Process -Name Deckle, anytype, anytypeHelper -ErrorAction SilentlyContinue)) {
        $path = try { $process.Path } catch { $null }
        $kind = if ($process.Name -ieq 'Deckle') { 'Deckle' }
                elseif ($path -and $path.StartsWith($providerRoot, [StringComparison]::OrdinalIgnoreCase)) { 'bot' }
                else { 'Anytype Desktop' }
        [pscustomobject]@{ Id = $process.Id; Kind = $kind; Path = $path }
    }
}

function Copy-AnytypeFolder([string]$Source, [string]$Destination) {
    # robocopy keeps its file list to itself: the folder may hold keys, and
    # only counts and sizes are ever printed.
    & robocopy $Source $Destination /E /COPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed copying $Source (exit code $LASTEXITCODE)." }
    # PowerShell names are case-insensitive: these must not be $source/$destination.
    $original = Get-ChildItem -LiteralPath $Source -Recurse -File -Force | Measure-Object Length -Sum
    $copy = Get-ChildItem -LiteralPath $Destination -Recurse -File -Force | Measure-Object Length -Sum
    if ($original.Count -ne $copy.Count -or $original.Sum -ne $copy.Sum) {
        throw "The copy of $Source differs from its source: $($copy.Count) of $($original.Count) files, $($copy.Sum) of $($original.Sum) bytes."
    }
    return [pscustomobject]@{ Files = $copy.Count; Bytes = [long]$copy.Sum }
}

# Written whole beside the target, flushed, then swapped in one replace: a
# start never reads half a manifest. Same shape and encoding as
# BackendProviderStore.ActivateProtected writes.
function Write-AnytypeActivation([string]$ProviderDirectory, [string]$Version) {
    $path = Join-Path $ProviderDirectory 'active.json'
    $temporary = Join-Path $ProviderDirectory "active.$([guid]::NewGuid().ToString('N')).tmp"
    $json = "{`"Version`":`"$Version`",`"RelativeExecutable`":`"versions/$Version/$script:ExecutableName`"}"
    try {
        $stream = [System.IO.FileStream]::new($temporary, [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write, [System.IO.FileShare]::None, 4096,
            [System.IO.FileOptions]::WriteThrough)
        try {
            $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        } finally { $stream.Dispose() }
        # [NullString]: PowerShell would pass $null to a .NET string as "".
        if (Test-Path -LiteralPath $path) { [System.IO.File]::Replace($temporary, $path, [NullString]::Value) }
        else { [System.IO.File]::Move($temporary, $path) }
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

# The switch itself, without the process guard the script runs first, so a
# test can drive it on a temporary layout. Without -Apply it returns the plan
# and writes nothing.
function Invoke-AnytypeBackendSwitch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$ProviderDirectory,
        [Parameter(Mandatory)][string]$DataDirectory,
        [Parameter(Mandatory)][string]$ConfigDirectory,
        [object[]]$Blockers = @(),
        [string]$Stamp = (Get-Date -Format 'yyyyMMdd-HHmmss'),
        # Asks the staged executable its version; a test passes its own answer.
        [scriptblock]$ReadVersion = { param($Executable) (& $Executable --version 2>&1 | Select-Object -First 1) -as [string] },
        [switch]$Apply
    )

    $activation = Get-AnytypeBackendActivation -ProviderDirectory $ProviderDirectory
    $target = Join-Path $ProviderDirectory "versions\$Version\$script:ExecutableName"
    if (-not (Test-Path -LiteralPath $target)) {
        throw "versions\$Version\$script:ExecutableName is not staged under $ProviderDirectory."
    }
    $reported = [string](& $ReadVersion $target)
    if ($reported -notmatch "^anytype-cli v$([regex]::Escape($Version))(\s|$)") {
        throw "versions\$Version\$script:ExecutableName reports '$reported', not anytype-cli v$Version."
    }
    if ($activation -and $activation.Valid -and $activation.Version -ieq $Version) {
        return [pscustomobject]@{ Result = 'Skipped'; Version = $Version; Reported = $reported; Previous = $Version }
    }

    $activationBackup = $null
    if ($activation) {
        $label = if ($activation.Version) { $activation.Version } else { 'unreadable' }
        $activationBackup = Join-Path $ProviderDirectory "active.json.$label.bak"
        if (Test-Path -LiteralPath $activationBackup) {
            $activationBackup = Join-Path $ProviderDirectory "active.json.$label.$Stamp.bak"
        }
    }
    $copies = @(
        foreach ($folder in $DataDirectory, $ConfigDirectory) {
            if (Test-Path -LiteralPath $folder) {
                $size = Get-ChildItem -LiteralPath $folder -Recurse -File -Force | Measure-Object Length -Sum
                [pscustomobject]@{ Source = $folder; Destination = "$($folder.TrimEnd('\')).backup-$Stamp"; Files = $size.Count; Bytes = [long]$size.Sum }
            }
        })

    $plan = [pscustomobject]@{
        Result           = if ($Apply) { 'Success' } else { 'Preview' }
        Version          = $Version
        Reported         = $reported
        Previous         = if ($activation) { $activation.Version }
        ActivationPath   = Join-Path $ProviderDirectory 'active.json'
        ActivationBackup = $activationBackup
        Copies           = $copies
        Blockers         = @($Blockers)
    }
    if (-not $Apply) { return $plan }

    if (@($Blockers).Count -gt 0) {
        $list = ($Blockers | ForEach-Object { "$($_.Kind) PID $($_.Id)" }) -join ', '
        throw "Still running: $list. Close them, then run again."
    }
    foreach ($copy in $copies) {
        if (Test-Path -LiteralPath $copy.Destination) { throw "$($copy.Destination) already exists." }
        $measure = Copy-AnytypeFolder $copy.Source $copy.Destination
        $copy.Files = $measure.Files
        $copy.Bytes = $measure.Bytes
    }
    if ($activationBackup) {
        Copy-Item -LiteralPath $plan.ActivationPath -Destination $activationBackup
    }
    Write-AnytypeActivation $ProviderDirectory $Version
    $written = Get-AnytypeBackendActivation -ProviderDirectory $ProviderDirectory
    if (-not $written.Valid -or $written.Version -ine $Version) {
        throw "active.json was written but does not activate $Version."
    }
    return $plan
}

Export-ModuleMember -Function Get-AnytypeProviderDirectory, Get-AnytypeDataDirectory,
    Get-AnytypeConfigDirectory, Get-AnytypeBackendPin, Get-AnytypeBackendActivation,
    Get-AnytypeBackendPinStatus, Get-AnytypeSwitchBlocker, Invoke-AnytypeBackendSwitch
