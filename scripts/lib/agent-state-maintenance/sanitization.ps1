Set-StrictMode -Version Latest

function Assert-JsonObject {
    param([object]$Value, [string]$Name)
    if ($null -eq $Value -or $Value -isnot [pscustomobject]) {
        throw "Expected a JSON object at $Name."
    }
}

function Clear-JsonProperty {
    param(
        [Parameter(Mandatory)][pscustomobject]$Object,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][ValidateSet('Array', 'Object')][string]$Kind
    )

    $property = $Object.PSObject.Properties[$Name]
    if (-not $property) { return $false }
    if ($Kind -eq 'Array' -and $property.Value -isnot [System.Collections.IList]) {
        throw "Expected a JSON array at $Name."
    }
    if ($Kind -eq 'Object' -and $property.Value -isnot [pscustomobject]) {
        throw "Expected a JSON object at $Name."
    }
    if ($Kind -eq 'Array') {
        $property.Value = [object][object[]]::new(0)
    } else {
        $property.Value = [pscustomobject]@{}
    }
    return $true
}

function ConvertTo-CleanCodexGlobalState {
    param([Parameter(Mandatory)][string]$Json)

    $state = $Json | ConvertFrom-Json -Depth 100
    Assert-JsonObject $state 'Codex global state'
    foreach ($name in @(
        'electron-saved-workspace-roots', 'project-order', 'active-workspace-roots',
        'projectless-thread-ids', 'pinned-thread-ids', 'pinned-project-ids'
    )) {
        $null = Clear-JsonProperty -Object $state -Name $name -Kind Array
    }
    foreach ($name in @(
        'queued-follow-ups', 'thread-workspace-root-hints',
        'thread-projectless-output-directories', 'thread-writable-roots',
        'local-projects', 'thread-project-assignments'
    )) {
        $null = Clear-JsonProperty -Object $state -Name $name -Kind Object
    }
    if ($state.PSObject.Properties['selected-project']) {
        $state.PSObject.Properties.Remove('selected-project')
    }

    $atomProperty = $state.PSObject.Properties['electron-persisted-atom-state']
    if ($atomProperty) {
        Assert-JsonObject $atomProperty.Value 'electron-persisted-atom-state'
        $exact = @(
            'prompt-history', 'heartbeat-thread-permissions-by-id',
            'composer-prompt-drafts-v1', 'unread-thread-ids-by-host-v1',
            'thread-descriptions-v1', 'flat-project-sidebar-preferences-v1',
            'sidebar-collapsed-groups', 'sidebar-collapsed-sections-v1'
        )
        $prefixes = @(
            'thread-client-id-v1:', 'thread-reference-capability:',
            'thread-browser-tabs-v1:', 'thread-tab-routes-v1:',
            'codex-writing-block-deleted-thread-v1:',
            'sidebar-project-expanded-v1-', 'remote-thread-summaries:',
            'remote-thread-summaries-v2:'
        )
        foreach ($property in @($atomProperty.Value.PSObject.Properties)) {
            $matchesPrefix = @($prefixes | Where-Object { $property.Name.StartsWith($_, [StringComparison]::Ordinal) }).Count -gt 0
            if ($exact -contains $property.Name -or $matchesPrefix) {
                $atomProperty.Value.PSObject.Properties.Remove($property.Name)
            }
        }
    }
    return ($state | ConvertTo-Json -Depth 100)
}

function ConvertTo-CleanClaudeProfile {
    param([Parameter(Mandatory)][string]$Json)
    $profile = $Json | ConvertFrom-Json -Depth 100
    Assert-JsonObject $profile 'Claude profile'
    $projects = $profile.PSObject.Properties['projects']
    if ($projects -and $projects.Value -isnot [pscustomobject]) {
        throw 'Expected a JSON object at Claude profile projects.'
    }
    if ($projects) { $projects.Value = [pscustomobject]@{} }
    else { $profile | Add-Member -NotePropertyName projects -NotePropertyValue ([pscustomobject]@{}) }
    return ($profile | ConvertTo-Json -Depth 100)
}

function ConvertTo-CleanClaudeDesktopConfig {
    param([Parameter(Mandatory)][string]$Json)
    $config = $Json | ConvertFrom-Json -Depth 100
    Assert-JsonObject $config 'Claude Desktop config'
    $preferencesProperty = $config.PSObject.Properties['preferences']
    if (-not $preferencesProperty) { return ($config | ConvertTo-Json -Depth 100) }
    Assert-JsonObject $preferencesProperty.Value 'Claude Desktop preferences'
    $preferences = $preferencesProperty.Value
    foreach ($name in @('launchPreviewPersistedWorkspaces', 'launchPreviewSessionScopedSessions')) {
        $null = Clear-JsonProperty -Object $preferences -Name $name -Kind Array
    }

    $epitaxyProperty = $preferences.PSObject.Properties['epitaxyPrefs']
    if (-not $epitaxyProperty) { return ($config | ConvertTo-Json -Depth 100) }
    Assert-JsonObject $epitaxyProperty.Value 'Claude Desktop epitaxyPrefs'
    $epitaxy = $epitaxyProperty.Value
    foreach ($name in @('starred-local-code-sessions', 'starred-session-groups', 'starred-cowork-spaces')) {
        $null = Clear-JsonProperty -Object $epitaxy -Name $name -Kind Array
    }
    foreach ($property in @($epitaxy.PSObject.Properties)) {
        if ($property.Name.StartsWith('epitaxy-perm-mode-acks.', [StringComparison]::Ordinal)) {
            if ($property.Value -isnot [System.Collections.IList]) {
                throw "Expected a JSON array at Claude Desktop preference $($property.Name)."
            }
            $property.Value = [object][object[]]::new(0)
        } elseif ($property.Name.StartsWith('epitaxy-folder-permission-mode.', [StringComparison]::Ordinal)) {
            if ($property.Value -isnot [pscustomobject]) {
                throw "Expected a JSON object at Claude Desktop preference $($property.Name)."
            }
            $property.Value = [pscustomobject]@{}
        }
    }
    $sliceProperty = $epitaxy.PSObject.Properties['dframe-local-slice']
    if ($sliceProperty) {
        Assert-JsonObject $sliceProperty.Value 'Claude Desktop dframe-local-slice'
        foreach ($name in @('homeProjectsPinnedOrder', 'pinnedOrder')) {
            $null = Clear-JsonProperty -Object $sliceProperty.Value -Name $name -Kind Array
        }
    }
    $filterProperty = $epitaxy.PSObject.Properties['ccd-sessions-filter']
    if ($filterProperty) {
        Assert-JsonObject $filterProperty.Value 'Claude Desktop ccd-sessions-filter'
        $filterState = $filterProperty.Value.PSObject.Properties['state']
        if ($filterState) {
            Assert-JsonObject $filterState.Value 'Claude Desktop ccd-sessions-filter state'
            $null = Clear-JsonProperty -Object $filterState.Value -Name 'selectedProjects' -Kind Array
        }
    }
    return ($config | ConvertTo-Json -Depth 100)
}

function ConvertTo-CleanCodexConfig {
    param([Parameter(Mandatory)][string]$Toml)

    if ($Toml.Contains('"""') -or $Toml.Contains("'''")) {
        throw 'Codex config contains a multiline TOML string; refusing an unsafe project-table rewrite.'
    }
    $lines = $Toml -split '\r?\n'
    $result = [System.Collections.Generic.List[string]]::new()
    $skipping = $false
    foreach ($line in $lines) {
        if ($line -match '^\s*\[') {
            if ($line -match '^\s*\[projects\.(?:''[^'']*''|"[^"]*")\]\s*(?:#.*)?$') {
                $skipping = $true
                continue
            }
            if ($line -match '^\s*\[projects\.') {
                throw "Unrecognized Codex project table header: $line"
            }
            $skipping = $false
        }
        if (-not $skipping) { $result.Add($line) }
    }
    return ($result -join [Environment]::NewLine).TrimEnd() + [Environment]::NewLine
}

function Write-AgentStateFileAtomically {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )
    $directory = Split-Path -Parent $Path
    $temporary = Join-Path $directory ".$([IO.Path]::GetFileName($Path)).reset-$([guid]::NewGuid()).tmp"
    try {
        $suffix = if ($Content.EndsWith([Environment]::NewLine)) { '' } else { [Environment]::NewLine }
        [IO.File]::WriteAllText($temporary, $Content + $suffix, [Text.UTF8Encoding]::new($false))
        $null = Get-Content -Raw -LiteralPath $temporary -ErrorAction Stop
        [IO.File]::Move($temporary, $Path, $true)
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Get-CleanAgentStateFileContent {
    param([Parameter(Mandatory)][object]$StateFile)

    $null = Assert-AgentStatePath -Path $StateFile.Path -Root $StateFile.Root
    $before = Get-Content -Raw -LiteralPath $StateFile.Path
    $after = switch ($StateFile.Kind) {
        'CodexGlobalState' { ConvertTo-CleanCodexGlobalState -Json $before }
        'CodexConfig' { ConvertTo-CleanCodexConfig -Toml $before }
        'ClaudeProfile' { ConvertTo-CleanClaudeProfile -Json $before }
        'ClaudeDesktopConfig' { ConvertTo-CleanClaudeDesktopConfig -Json $before }
        default { throw "Unknown mixed state file kind: $($StateFile.Kind)" }
    }
    return [pscustomobject]@{
        Path = $StateFile.Path
        Before = $before
        After = $after
        Changed = $before.TrimEnd() -ne $after.TrimEnd()
    }
}
