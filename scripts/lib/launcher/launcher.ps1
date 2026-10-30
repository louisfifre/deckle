# launcher.ps1 - The repository's terminal menu, under PowerShell 7.
#
# menu.ps1 is the only entry: it relaunches this file under pwsh when the
# shell is Windows PowerShell. Interactive by default; -List and -Run serve
# an agent or a shell, -Snapshot serves tests and inspection.

[CmdletBinding(DefaultParameterSetName = 'Interactive')]
param(
    # Print every Action of the catalog, one per line: id, label, script,
    # arguments, variants. Nothing runs.
    [Parameter(ParameterSetName = 'List')][switch]$List,
    # Run one Action by its id (first column of -List) without the menu and
    # print its Journal; the exit code says how it ended.
    [Parameter(ParameterSetName = 'Run', Mandatory)][string]$Run,
    [Parameter(ParameterSetName = 'Run')][string]$Variant,
    # Print one deterministic frame instead of starting the host.
    [Parameter(ParameterSetName = 'Snapshot', Mandatory)]
    [ValidateSet('Menu', 'Level', 'Confirmation', 'Execution', 'Logs')][string]$Snapshot,
    # The folder path of the Level snapshot, as the catalog names it.
    [Parameter(ParameterSetName = 'Snapshot')][string]$LevelPath = '',
    [Parameter(ParameterSetName = 'Snapshot')][ValidateRange(20, 240)][int]$Width = 100,
    [Parameter(ParameterSetName = 'Snapshot')][ValidateRange(8, 100)][int]$Height = 30,
    # Terminal paints with the console colours of the terminal's scheme;
    # TrueColor paints with the designed RGB palette, dark or light after the
    # console background. Redirected output carries no colour either way.
    [Parameter(ParameterSetName = 'Interactive')]
    [Parameter(ParameterSetName = 'HostSmokeTest')]
    [ValidateSet('Terminal', 'TrueColor')][string]$Palette = 'Terminal',
    [Parameter(ParameterSetName = 'HostSmokeTest')][switch]$HostSmokeTest,
    # The scripts folder to serve; the repository root is asked to git.
    [string]$ScriptsRoot
)

$ErrorActionPreference = 'Stop'
$LibDir = Split-Path -Parent $PSScriptRoot
if (-not $ScriptsRoot) { $ScriptsRoot = Split-Path -Parent $LibDir }
$ScriptsRoot = (Resolve-Path -LiteralPath $ScriptsRoot).ProviderPath
# The logo opens every frame when the terminal has room for it; the Banner
# beneath it then names the repository the menu serves.
$Logo = @(
    '█▀▄ █▀▀ █▀▀ █▄▀ █   █▀▀'
    '█▄▀ █▄▄ █▄▄ █ █ █▄▄ █▄▄  SCRIPTS'
)

Import-Module (Join-Path $LibDir 'terminal-interaction.psm1') -Force
Import-Module (Join-Path $LibDir 'script-discovery.psm1') -Force
. (Join-Path $LibDir 'launcher\discovered-catalog.ps1')
. (Join-Path $LibDir 'launcher\execution.ps1')
. (Join-Path $LibDir 'launcher\logs.ps1')
. (Join-Path $LibDir 'launcher\flows.ps1')

function Get-LauncherRepositoryRoot {
    param([Parameter(Mandatory)][string]$Path)

    # The nearest ancestor holding a .git entry is the repository root: a
    # directory in a checkout, a file in a worktree or a submodule. Reading
    # it costs nothing, where asking git takes a tenth of a second at every
    # opening of the menu.
    $directory = [IO.DirectoryInfo]$Path
    while ($null -ne $directory) {
        if (Test-Path -LiteralPath (Join-Path $directory.FullName '.git')) { return $directory.FullName }
        $directory = $directory.Parent
    }
    throw "The scripts folder '$Path' is not inside a git repository: no folder above it holds a .git entry."
}

function Get-LauncherRepositoryName {
    param([Parameter(Mandatory)][string]$Root)

    # A worktree is named after its subject, not after the repository: its
    # .git file points into the main checkout's .git/worktrees, and the
    # repository's folder is the one that holds that .git.
    $gitEntry = Join-Path $Root '.git'
    if (Test-Path -LiteralPath $gitEntry -PathType Leaf) {
        $pointer = ([IO.File]::ReadAllText($gitEntry)).Trim()
        if ($pointer -match '^gitdir:\s*(.+?)[/\\]\.git[/\\]worktrees[/\\][^/\\]+$') {
            return Split-Path -Leaf $Matches[1]
        }
    }
    return Split-Path -Leaf $Root
}

$RepositoryRoot = Get-LauncherRepositoryRoot -Path $ScriptsRoot
$Banner = Get-LauncherRepositoryName -Root $RepositoryRoot
# The library and the tests never enter the menu; a manifest folder starts
# with a dot and is skipped by discovery itself.
$Catalog = Get-ScriptCatalog -Path $ScriptsRoot -ExcludeFolder @('lib', 'tests')
$Context = New-LauncherContext -Catalog $Catalog -Banner $Banner -RepositoryRoot $RepositoryRoot -ScriptsRoot $ScriptsRoot

function Get-LauncherActionTargets {
    param([Parameter(Mandatory)][PSTypeName('DeckleScripts.ScriptDiscovery.Level')][object]$Level)

    foreach ($row in @(Get-CatalogRows -Catalog $Level)) {
        $item = ConvertTo-LauncherDiscoveredItem -Row $row
        if ($item.PSObject.Properties['Kind'] -and $item.Kind -eq 'ActionRow') {
            foreach ($variant in $item.Variants) { $variant }
        } else {
            $item
        }
    }
}

if ($List) {
    foreach ($target in @(Get-LauncherActionTargets -Level $Catalog)) {
        $payload = $target.Payload
        $variants = if ($payload.Variant) { $payload.Variant } else { '' }
        [Console]::WriteLine(($payload.ActionId, $payload.Label, $payload.ScriptRelativePath, $payload.Arguments, $variants) -join "`t")
    }
    return
}

if ($PSCmdlet.ParameterSetName -eq 'Run') {
    $targets = @(Get-LauncherActionTargets -Level $Catalog | Where-Object { $_.Payload.ActionId -eq $Run })
    if ($targets.Count -eq 0) { throw "No Action '$Run' in the catalog. Use -List to see the ids." }
    # Without -Variant, an Action with Variants runs its first one, as the
    # menu preselects it.
    $target = if ($Variant) { @($targets | Where-Object { $_.Payload.Variant -eq $Variant }) } else { @($targets[0]) }
    if ($target.Count -eq 0) { throw "Action '$Run' has no Variant '$Variant'. Use -List to see the variants." }
    $record = New-LauncherExecutionRecord -Action $target[0].Payload -OwnerActionMenuId 'menu.discovered' -Sequence 1
    $record = Invoke-LauncherExecution -Record $record -ScriptsRoot $ScriptsRoot -RepositoryRoot $RepositoryRoot
    foreach ($line in $record.Journal) { [Console]::WriteLine($line.Text) }
    [Console]::WriteLine("Result: $($record.Result)")
    if ($record.State -eq 'Failed') { exit 1 }
    return
}

function Get-LauncherSampleExecutionRecord {
    # A fixed Journal, so the Execution layout can be inspected without
    # running anything.
    $action = [pscustomobject]@{
        ActionId = 'sample.0'; Variant = $null; Label = 'Sample run'; ScriptRelativePath = 'sample.ps1'
        Arguments = ''; VariantParameter = $null; Confirmation = $null
    }
    $record = New-LauncherExecutionRecord -Action $action -OwnerActionMenuId 'menu.discovered' -Sequence 1
    $record.Journal.Add([pscustomobject]@{ Text = "[launcher] & 'sample.ps1'"; PresentationRole = 'Supporting' })
    for ($index = 1; $index -le 28; $index++) {
        $record.Journal.Add(('[sample] Journal line {0:d2} keeps its complete logical value for clipping and paging validation.' -f $index))
    }
    $record.Journal.Add('[sample] This deliberately long line should be clipped at the Journal Panel boundary without wrapping into the Tracking Panel or changing the stored value.')
    $record.Journal.Add([pscustomobject]@{ Text = '[summary] Sample completed.'; PresentationRole = 'PanelTitle' })
    $record.State = 'Completed'; $record.Phase = 'Completed'; $record.Result = 'Success'
    return $record
}

function Get-LauncherConfirmationSnapshot {
    $row = @(Get-CatalogRows -Catalog $Catalog | Where-Object { $_.Confirmation }) | Select-Object -First 1
    if ($null -eq $row) { throw 'No discovered Action declares a confirmation sentence.' }
    $item = ConvertTo-LauncherDiscoveredItem -Row $row
    $target = if ($item.PSObject.Properties['Kind'] -and $item.Kind -eq 'ActionRow') { $item.Variants[0] } else { $item }
    $levelPath = if ($row.RelativePath.Contains('/')) { $row.RelativePath.Substring(0, $row.RelativePath.LastIndexOf('/')) } else { '' }
    $level = Resolve-LauncherDiscoveredAccess -Catalog $Catalog -AccessId $levelPath
    $owner = ConvertTo-LauncherDiscoveredView -Level $level -Banner $Banner -IsRoot:($levelPath -eq '')
    return [pscustomobject]@{
        View = New-LauncherConfirmationView -Action $target.Payload -Banner $Banner -OwnerActionMenuId $owner.ViewId
        Owner = $owner
    }
}

function Get-LauncherSnapshot {
    param([Parameter(Mandatory)][string]$Name)

    $root = Get-LauncherRootView -Context $Context
    switch ($Name) {
        'Menu' {
            return [pscustomobject]@{ View = $root; Owner = $root; Focus = (Get-LauncherFirstTargetId -View $root) }
        }
        'Level' {
            $level = Resolve-LauncherDiscoveredAccess -Catalog $Catalog -AccessId $LevelPath
            if ($null -eq $level) { throw "No level '$LevelPath' in the catalog." }
            $view = ConvertTo-LauncherDiscoveredView -Level $level -Banner $Banner
            return [pscustomobject]@{ View = $view; Owner = $view; Focus = (Get-LauncherFirstTargetId -View $view) }
        }
        'Confirmation' {
            $snapshot = Get-LauncherConfirmationSnapshot
            return [pscustomobject]@{ View = $snapshot.View; Owner = $snapshot.Owner; Focus = $snapshot.View.Targets[0].TargetId }
        }
        'Execution' {
            $view = ConvertTo-LauncherExecutionView -Record (Get-LauncherSampleExecutionRecord) -Banner $Banner
            return [pscustomobject]@{ View = $view; Owner = $root; Focus = 'navigation.back' }
        }
        'Logs' {
            Add-LauncherLogsRecord -Register $Context.Logs -Record (Get-LauncherSampleExecutionRecord)
            $view = ConvertTo-LauncherLogsView -Register $Context.Logs -Banner $Banner -OwnerActionMenuId $root.ViewId
            return [pscustomobject]@{ View = $view; Owner = $root; Focus = 'navigation.back' }
        }
    }
}

if ($HostSmokeTest) {
    $smoke = Get-LauncherSnapshot -Name 'Menu'
    $module = Get-Module terminal-interaction
    & $module {
        param($view, $ownerActionMenu, $focus, $palette, $logo)
        $originalOutputCodePage = [Console]::OutputEncoding.CodePage
        $hostState = Start-TerminalHost -Palette $palette
        try {
            if ($hostState.UnicodeOutput -ne 'Supported' -or [Console]::OutputEncoding.CodePage -ne 65001) {
                throw 'The host smoke test could not activate UTF-8 console output.'
            }
            $metrics = Get-TerminalHostMetrics
            $frame = Get-TerminalInteractionFrame `
                -View $view `
                -OwnerActionMenu $ownerActionMenu `
                -Width $metrics.Width `
                -Height $metrics.Height `
                -FocusedTargetId $focus `
                -Logo $logo
            Write-TerminalInteractionFrame -Frame $frame -HostState $hostState
            Start-Sleep -Milliseconds 100
        } finally {
            Stop-TerminalHost -State $hostState
        }
        if ([Console]::OutputEncoding.CodePage -ne $originalOutputCodePage) {
            throw 'The host smoke test did not restore the original console output encoding.'
        }
    } $smoke.View $smoke.Owner $smoke.Focus $Palette $Logo
    return
}

if ($Snapshot) {
    $shot = Get-LauncherSnapshot -Name $Snapshot
    $frame = Get-TerminalInteractionFrame `
        -View $shot.View `
        -OwnerActionMenu $shot.Owner `
        -Width $Width `
        -Height $Height `
        -FocusedTargetId $shot.Focus `
        -JournalOffset ([int]::MaxValue) `
        -Logo $Logo
    foreach ($line in @(ConvertTo-TerminalFrameText -Frame $frame)) {
        [Console]::WriteLine($line)
    }
    return
}

# An update replaces the launcher under its feet; the manifest's stamp
# tells, and the menu reopens in a fresh process once this one ends.
$ManifestPath = Join-Path $ScriptsRoot '.deckle-scripts\manifest.json'
$ManifestStamp = if (Test-Path -LiteralPath $ManifestPath) { (Get-Item -LiteralPath $ManifestPath).LastWriteTimeUtc } else { $null }

Start-TerminalInteraction -RootView (Get-LauncherRootView -Context $Context) -Palette $Palette -Logo $Logo -IntentHandler {
    param($request, $sourceView)
    Resolve-LauncherIntent -Request $request -SourceView $sourceView -Context $Context
}

$currentStamp = if (Test-Path -LiteralPath $ManifestPath) { (Get-Item -LiteralPath $ManifestPath).LastWriteTimeUtc } else { $null }
if ($null -ne $ManifestStamp -and $currentStamp -ne $ManifestStamp) {
    [Console]::WriteLine('Deckle Scripts was updated; reopening the menu.')
    & pwsh -NoProfile -File (Join-Path $ScriptsRoot 'menu.ps1')
    exit $LASTEXITCODE
}
