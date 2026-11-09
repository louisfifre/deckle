Set-StrictMode -Version Latest

$script:Strings = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'agent-state-maintenance.strings.psd1')
. (Join-Path $PSScriptRoot 'agent-state-maintenance\sanitization.ps1')

function Get-AgentStateStrings {
    return $script:Strings
}

function Resolve-AgentStateRoots {
    param(
        [string]$UserProfileRoot,
        [string]$ClaudeHome,
        [string]$ClaudeDesktopData
    )

    if ([string]::IsNullOrWhiteSpace($UserProfileRoot)) {
        $UserProfileRoot = [Environment]::GetFolderPath('UserProfile')
    }
    $profile = [IO.Path]::GetFullPath($UserProfileRoot)
    if ([string]::IsNullOrWhiteSpace($ClaudeHome)) {
        $ClaudeHome = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $profile '.claude' }
    }
    if ([string]::IsNullOrWhiteSpace($ClaudeDesktopData)) {
        $ClaudeDesktopData = Join-Path $profile 'AppData\Roaming\Claude'
    }

    return [pscustomobject]@{
        UserProfile = $profile
        Claude = [IO.Path]::GetFullPath($ClaudeHome)
        ClaudeProfile = [IO.Path]::GetFullPath((Join-Path $profile '.claude.json'))
        ClaudeProfileBackup = [IO.Path]::GetFullPath((Join-Path $profile '.claude.json.backup'))
        ClaudeDesktop = [IO.Path]::GetFullPath($ClaudeDesktopData)
    }
}

function Assert-AgentStatePath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    if (-not $fullPath.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Cleanup target escapes its allowed root: $fullPath"
    }
    $volumeRoot = [IO.Path]::GetPathRoot($fullRoot)
    $cursor = if (Test-Path -LiteralPath $fullPath) { $fullPath } else { Split-Path -Parent $fullPath }
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Cleanup target has a reparse point ancestor: $cursor"
            }
        }
        if ($cursor.TrimEnd('\', '/').Equals($volumeRoot.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) { break }
        $parent = Split-Path -Parent $cursor
        if (-not $parent -or $parent.Equals($cursor, [StringComparison]::OrdinalIgnoreCase)) { break }
        $cursor = $parent
    }
    return $fullPath
}

function Assert-AgentStateRootIdentity {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][ValidateSet('Claude', 'ClaudeDesktop')][string]$Kind,
        [Parameter(Mandatory)][string]$UserProfile
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }
    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $volumeRoot = [IO.Path]::GetPathRoot($fullPath).TrimEnd('\', '/')
    $profile = [IO.Path]::GetFullPath($UserProfile).TrimEnd('\', '/')
    if ($fullPath.Equals($volumeRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.Equals($profile, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing broad $Kind state root: $fullPath"
    }
    $null = Assert-AgentStatePath -Path (Join-Path $fullPath '__root-check__') -Root $fullPath
    $canonicalPath = switch ($Kind) {
        'Claude' { Join-Path $profile '.claude' }
        'ClaudeDesktop' { Join-Path $profile 'AppData\Roaming\Claude' }
    }
    $isCanonical = $fullPath.Equals(
        [IO.Path]::GetFullPath($canonicalPath).TrimEnd('\', '/'),
        [StringComparison]::OrdinalIgnoreCase
    )
    $markers = switch ($Kind) {
        'Claude' { @('.credentials.json', '.last-cleanup') }
        'ClaudeDesktop' { @('claude_desktop_config.json') }
    }
    $recognized = @($markers | Where-Object { Test-Path -Path (Join-Path $fullPath $_) }).Count -gt 0
    if (-not $isCanonical -and -not $recognized) { throw "Unrecognized $Kind state root: $fullPath" }
}

function Assert-NoReparsePoint {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $items = @((Get-Item -LiteralPath $Path -Force))
    if (Test-Path -LiteralPath $Path -PathType Container) {
        $items += @(Get-ChildItem -LiteralPath $Path -Force -Recurse -ErrorAction Stop)
    }
    $reparse = $items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1
    if ($reparse) { throw "Cleanup target contains a reparse point: $($reparse.FullName)" }
}

function Get-AgentStatePathMeasure {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ Files = 0; Bytes = [int64]0 }
    }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer) {
        $files = @(Get-ChildItem -LiteralPath $Path -File -Force -Recurse -ErrorAction Stop)
        $bytes = if ($files.Count -gt 0) {
            ($files | Measure-Object -Property Length -Sum).Sum
        } else {
            0
        }
        return [pscustomobject]@{ Files = $files.Count; Bytes = [int64]$bytes }
    }
    return [pscustomobject]@{ Files = 1; Bytes = [int64]$item.Length }
}

function Format-AgentStateByteCount {
    param([Parameter(Mandatory)][int64]$Bytes)
    if ($Bytes -ge 1GB) { return '{0:N2} GB' -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return '{0:N1} MB' -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return '{0:N1} KB' -f ($Bytes / 1KB) }
    return "$Bytes B"
}

function Get-AgentStateFileTargets {
    param(
        [Parameter(Mandatory)][object]$Roots,
        [Parameter(Mandatory)][ValidateSet('Claude')][string]$Scope
    )

    $targets = [System.Collections.Generic.List[object]]::new()
    function Add-Target([string]$Path, [string]$Root, [string]$Category) {
        $safePath = Assert-AgentStatePath -Path $Path -Root $Root
        $targets.Add([pscustomobject]@{ Path = $safePath; Root = $Root; Category = $Category })
    }

    foreach ($name in @(
        'ide', 'projects', 'session-env', 'sessions',
        'shell-snapshots', 'tasks', 'teams', 'debug', 'plans', 'paste-cache',
        'image-cache', 'file-history', 'feedback-bundles', 'todos', 'logs'
    )) {
        Add-Target (Join-Path $Roots.Claude $name) $Roots.Claude "Claude $name"
    }
    foreach ($name in @('history.jsonl', 'stats-cache.json')) {
        Add-Target (Join-Path $Roots.Claude $name) $Roots.Claude "Claude $name"
    }
    foreach ($name in @(
        'claude-code-sessions', 'local-agent-mode-sessions', 'logs', 'Cache',
        'Local Storage\leveldb'
    )) {
        Add-Target (Join-Path $Roots.ClaudeDesktop $name) $Roots.ClaudeDesktop "Claude Desktop $name"
    }
    return @($targets)
}

function Assert-AgentProcessesStopped {
    param([string[]]$ProcessNames)
    if ($null -eq $ProcessNames) {
        $ProcessNames = @(Get-Process -ErrorAction SilentlyContinue | ForEach-Object ProcessName)
    }
    $blocked = @($ProcessNames | Where-Object {
        $_ -eq 'ChatGPT' -or $_ -eq 'claude' -or $_ -like 'claude-*'
    } | Sort-Object -Unique)
    if ($blocked.Count -gt 0) {
        throw "Close the active AI tools before resetting local state: $($blocked -join ', ')"
    }
}

function Get-AgentStateCleanupPlan {
    [CmdletBinding()]
    param(
        [ValidateSet('Claude')][string[]]$Scope = @('Claude'),
        [string]$UserProfileRoot,
        [string]$ClaudeHome,
        [string]$ClaudeDesktopData
    )

    $rootParameters = @{
        UserProfileRoot = $UserProfileRoot
        ClaudeHome = $ClaudeHome
        ClaudeDesktopData = $ClaudeDesktopData
    }
    $roots = Resolve-AgentStateRoots @rootParameters
    if ($Scope -contains 'Claude') {
        Assert-AgentStateRootIdentity -Path $roots.Claude -Kind Claude -UserProfile $roots.UserProfile
        Assert-AgentStateRootIdentity -Path $roots.ClaudeDesktop -Kind ClaudeDesktop -UserProfile $roots.UserProfile
    }
    $targets = @()
    foreach ($item in $Scope) { $targets += @(Get-AgentStateFileTargets -Roots $roots -Scope $item) }
    $files = 0
    $bytes = [int64]0
    foreach ($target in $targets) {
        if (Test-Path -LiteralPath $target.Path) { Assert-NoReparsePoint -Path $target.Path }
        $measure = Get-AgentStatePathMeasure -Path $target.Path
        $files += $measure.Files
        $bytes += $measure.Bytes
    }

    $stateFiles = @()
    if ($Scope -contains 'Claude') {
        if (Test-Path -LiteralPath $roots.ClaudeProfile -PathType Leaf) {
            $null = Assert-AgentStatePath -Path $roots.ClaudeProfile -Root $roots.UserProfile
            Assert-NoReparsePoint -Path $roots.ClaudeProfile
            $stateFiles += [pscustomobject]@{ Path = $roots.ClaudeProfile; Root = $roots.UserProfile; Kind = 'ClaudeProfile' }
        }
        if (Test-Path -LiteralPath $roots.ClaudeProfileBackup -PathType Leaf) {
            $null = Assert-AgentStatePath -Path $roots.ClaudeProfileBackup -Root $roots.UserProfile
            $stateFiles += [pscustomobject]@{ Path = $roots.ClaudeProfileBackup; Root = $roots.UserProfile; Kind = 'ClaudeProfile' }
        }
        $backupDirectory = Join-Path $roots.Claude 'backups'
        if (Test-Path -LiteralPath $backupDirectory -PathType Container) {
            foreach ($backup in Get-ChildItem -LiteralPath $backupDirectory -File -Filter '.claude.json.backup*') {
                $null = Assert-AgentStatePath -Path $backup.FullName -Root $roots.Claude
                $stateFiles += [pscustomobject]@{ Path = $backup.FullName; Root = $roots.Claude; Kind = 'ClaudeProfile' }
            }
        }
        $desktopConfig = Join-Path $roots.ClaudeDesktop 'claude_desktop_config.json'
        if (Test-Path -LiteralPath $desktopConfig -PathType Leaf) {
            $null = Assert-AgentStatePath -Path $desktopConfig -Root $roots.ClaudeDesktop
            Assert-NoReparsePoint -Path $desktopConfig
            $stateFiles += [pscustomobject]@{ Path = $desktopConfig; Root = $roots.ClaudeDesktop; Kind = 'ClaudeDesktopConfig' }
        }
    }

    $targetPaths = @($targets | ForEach-Object Path | Sort-Object)
    $stateFilePaths = @($stateFiles | ForEach-Object Path | Sort-Object)
    $planText = @($Scope | Sort-Object) + $targetPaths + $stateFilePaths
    $bytesForHash = [Text.Encoding]::UTF8.GetBytes($planText -join [char]10)
    $planHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytesForHash)).Substring(0, 12)
    return [pscustomobject]@{
        Roots = $roots
        Scope = @($Scope)
        FileTargets = @($targets)
        Files = $files
        Bytes = $bytes
        StateFiles = @($stateFiles)
        PlanId = $planHash
        Warnings = @(
            $script:Strings.CloudWarning
            if ($Scope -contains 'Claude' -and (Test-Path -LiteralPath (Join-Path $roots.ClaudeDesktop 'Local Storage\leveldb'))) {
                $script:Strings.LevelDbResetWarning
            }
        )
    }
}

function Invoke-AgentStateCleanupPlanCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Plan,
        [switch]$Apply,
        [string]$Confirmation,
        [string[]]$RunningProcessNames
    )

    if ($Apply) {
        if ($Confirmation -cne $script:Strings.ConfirmationPhrase) {
            throw "Reset requires the exact confirmation phrase: $($script:Strings.ConfirmationPhrase)"
        }
        Assert-AgentProcessesStopped -ProcessNames $RunningProcessNames
    }

    $stateChanges = @()
    foreach ($stateFile in $Plan.StateFiles) {
        $stateChanges += Get-CleanAgentStateFileContent -StateFile $stateFile
    }
    $changedStateFiles = @($stateChanges | Where-Object Changed).Count

    if (-not $Apply) {
        return [pscustomobject]@{
            Applied = $false
            RemovedTargets = 0
            ChangedStateFiles = $changedStateFiles
        }
    }

    Assert-AgentProcessesStopped -ProcessNames $RunningProcessNames

    $changedStateFiles = 0
    foreach ($stateFile in $Plan.StateFiles) {
        $change = Get-CleanAgentStateFileContent -StateFile $stateFile
        if ($change.Changed) {
            Write-AgentStateFileAtomically -Path $change.Path -Content $change.After
            $changedStateFiles++
        }
    }

    $removedTargets = 0
    foreach ($target in $Plan.FileTargets) {
        if (-not (Test-Path -LiteralPath $target.Path)) { continue }
        $null = Assert-AgentStatePath -Path $target.Path -Root $target.Root
        Assert-NoReparsePoint -Path $target.Path
        Remove-Item -LiteralPath $target.Path -Recurse -Force
        $removedTargets++
    }
    return [pscustomobject]@{
        Applied = $true
        RemovedTargets = $removedTargets
        ChangedStateFiles = $changedStateFiles
    }
}

function Invoke-AgentStateCleanupPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Plan,
        [switch]$Apply,
        [string]$Confirmation,
        [string[]]$RunningProcessNames
    )

    if (-not $Apply) {
        return Invoke-AgentStateCleanupPlanCore @PSBoundParameters
    }

    $mutex = [Threading.Mutex]::new($false, 'Local\DeckleScripts.AgentStateReset')
    $acquired = $false
    try {
        try {
            $acquired = $mutex.WaitOne(0)
        } catch [Threading.AbandonedMutexException] {
            $acquired = $true
        }
        if (-not $acquired) {
            throw 'Another local AI session reset is already running.'
        }
        return Invoke-AgentStateCleanupPlanCore @PSBoundParameters
    } finally {
        if ($acquired) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

Export-ModuleMember -Function @(
    'Get-AgentStateStrings',
    'Resolve-AgentStateRoots',
    'Assert-AgentStatePath',
    'Assert-NoReparsePoint',
    'ConvertTo-CleanClaudeProfile',
    'ConvertTo-CleanClaudeDesktopConfig',
    'Assert-AgentProcessesStopped',
    'Format-AgentStateByteCount',
    'Get-AgentStateCleanupPlan',
    'Invoke-AgentStateCleanupPlan'
)
