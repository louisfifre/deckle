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
