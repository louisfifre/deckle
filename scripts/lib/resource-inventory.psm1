# Static, read-only inventory of WinUI resource references and .resw maps.

Set-StrictMode -Version Latest

function Get-ResourceAllowlist {
    param([string]$Path)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return @{ DynamicKeys = @(); RequiredKeys = @(); IntentionalDivergences = @() }
    }
    $value = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -AsHashtable
    if (-not $value.ContainsKey('DynamicKeys')) { $value.DynamicKeys = @() }
    if (-not $value.ContainsKey('RequiredKeys')) { $value.RequiredKeys = @() }
    if (-not $value.ContainsKey('IntentionalDivergences')) { $value.IntentionalDivergences = @() }
    return $value
}

function Get-ReswEntries {
    param([Parameter(Mandatory)][string]$Path)
    [xml]$document = Get-Content -Raw -LiteralPath $Path
    $entries = @{}
    foreach ($node in @($document.root.data)) {
        $key = [string]$node.name
        if ($entries.ContainsKey($key)) { throw "Duplicate resource key '$key' in $Path" }
        $entries[$key] = [string]$node.value
    }
    return $entries
}

function Test-AllowedResourceKey {
    param([string]$Assembly, [string]$Key, [object[]]$Rules)
    foreach ($rule in $Rules) {
        if ([string]$rule.Assembly -ne $Assembly) { continue }
        if ($Key -like [string]$rule.Pattern) { return $true }
    }
    return $false
}

function Get-CSharpStringLiterals {
    param([string[]]$Paths)
    $values = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $Paths) {
        $text = Get-Content -Raw -LiteralPath $path
        foreach ($match in [regex]::Matches($text, '"(?<value>(?:\\.|[^"\\])*)"')) {
            $values.Add($match.Groups['value'].Value) | Out-Null
        }
    }
    return ,$values
}

function Get-StaticRootReferences {
    param([string[]]$Paths)
    $references = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $Paths) {
        $text = Get-Content -Raw -LiteralPath $path
        foreach ($match in [regex]::Matches($text, 'Loc\.(?:Get|Format)\s*\(\s*"(?<key>[^"$]+)"')) {
            $references.Add($match.Groups['key'].Value) | Out-Null
        }
    }
    return ,$references
}

function Get-StaticModuleReferences {
    param([string[]]$Paths)
    $references = [System.Collections.Generic.List[object]]::new()
    foreach ($path in $Paths) {
        $text = Get-Content -Raw -LiteralPath $path
        foreach ($match in [regex]::Matches($text, 'Loc\.GetFrom(?:Optional)?\s*\(\s*"(?<assembly>[^"]+)"\s*,\s*"(?<key>[^"$]+)"')) {
            $references.Add([pscustomobject]@{
                Assembly = $match.Groups['assembly'].Value
                Key = $match.Groups['key'].Value.Replace('/', '.')
                Path = $path
            })
        }
    }
    return @($references)
}

function Get-XamlUids {
    param([string[]]$Paths)
    $uids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $Paths) {
        $text = Get-Content -Raw -LiteralPath $path
        foreach ($match in [regex]::Matches($text, 'x:Uid\s*=\s*"(?<uid>[^"]+)"')) {
            $uids.Add($match.Groups['uid'].Value) | Out-Null
        }
    }
    return ,$uids
}

function Test-KeyUsedLocally {
    param(
        [string]$Key,
        [System.Collections.Generic.HashSet[string]]$Uids,
        [System.Collections.Generic.HashSet[string]]$Literals
    )
    if ($Literals.Contains($Key)) { return $true }
    $base = ($Key -split '[./]', 2)[0]
    return $Uids.Contains($base) -or $Literals.Contains($base)
}

function Test-IntentionalDivergence {
    param([string]$Assembly, [string]$Key, [object[]]$Rules)
    foreach ($rule in $Rules) {
        if ([string]$rule.Assembly -eq $Assembly -and [string]$rule.Key -eq $Key) { return $true }
    }
    return $false
}

function Invoke-ResourceInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [string]$AllowlistPath
    )

    $src = Join-Path $RepoRoot 'src'
    $allowlist = Get-ResourceAllowlist -Path $AllowlistPath
    $csFiles = @(Get-ChildItem -LiteralPath $src -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Select-Object -Expand FullName)
    $rootReferences = Get-StaticRootReferences -Paths $csFiles
    $moduleReferences = @(Get-StaticModuleReferences -Paths $csFiles)
    $moduleReferenceKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($reference in $moduleReferences) {
        $moduleReferenceKeys.Add("$($reference.Assembly)|$($reference.Key)") | Out-Null
    }
    $allLiterals = Get-CSharpStringLiterals -Paths $csFiles

    $maps = @{}
    $projects = @()
    foreach ($resw in Get-ChildItem -LiteralPath $src -Recurse -File -Filter 'Resources.resw' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) {
        $projectDir = Split-Path (Split-Path (Split-Path $resw.FullName -Parent) -Parent) -Parent
        $csproj = Get-ChildItem -LiteralPath $projectDir -File -Filter '*.csproj' | Select-Object -First 1
        if (-not $csproj) { continue }
        $assembly = $csproj.BaseName
        $map = Get-ReswEntries -Path $resw.FullName
        $maps[$assembly] = $map
        $projects += [pscustomobject]@{ Assembly = $assembly; Root = $projectDir; Path = $resw.FullName; Entries = $map }
    }

    $missing = [System.Collections.Generic.List[object]]::new()
    $unused = [System.Collections.Generic.List[object]]::new()
    $divergences = [System.Collections.Generic.List[object]]::new()
    $appMap = $maps['Deckle.App']

    foreach ($key in $rootReferences) {
        if ($null -eq $appMap -or -not $appMap.ContainsKey($key)) {
            $missing.Add([pscustomobject]@{ Assembly = 'Deckle.App'; Key = $key; Reference = 'Loc root map' })
        }
    }
    foreach ($reference in $moduleReferences) {
        if (-not $maps.ContainsKey($reference.Assembly) -or -not $maps[$reference.Assembly].ContainsKey($reference.Key)) {
            $missing.Add([pscustomobject]@{ Assembly = $reference.Assembly; Key = $reference.Key; Reference = 'Loc.GetFrom' })
        }
    }
    $requiredKeyIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($required in @($allowlist.RequiredKeys)) {
        $assembly = [string]$required.Assembly
        $key = [string]$required.Key
        $requiredKeyIds.Add("$assembly|$key") | Out-Null
        if (-not $maps.ContainsKey($assembly) -or -not $maps[$assembly].ContainsKey($key)) {
            $missing.Add([pscustomobject]@{ Assembly = $assembly; Key = $key; Reference = 'required allowlist' })
        }
    }

    foreach ($project in $projects) {
        $localCs = @(Get-ChildItem -LiteralPath $project.Root -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Select-Object -Expand FullName)
        $localXaml = @(Get-ChildItem -LiteralPath $project.Root -Recurse -File -Filter '*.xaml' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Select-Object -Expand FullName)
        $uids = Get-XamlUids -Paths $localXaml
        $literals = Get-CSharpStringLiterals -Paths $localCs

        foreach ($uid in $uids) {
            $found = @($project.Entries.Keys | Where-Object { $_ -like "$uid.*" }).Count -gt 0
            if (-not $found -and -not (Test-AllowedResourceKey -Assembly $project.Assembly -Key $uid -Rules $allowlist.DynamicKeys)) {
                $missing.Add([pscustomobject]@{ Assembly = $project.Assembly; Key = $uid; Reference = 'x:Uid' })
            }
        }

        foreach ($key in $project.Entries.Keys) {
            $used = (Test-KeyUsedLocally -Key $key -Uids $uids -Literals $literals) -or
                $moduleReferenceKeys.Contains("$($project.Assembly)|$key") -or
                $requiredKeyIds.Contains("$($project.Assembly)|$key")
            if ($project.Assembly -eq 'Deckle.App') { $used = $used -or $rootReferences.Contains($key) -or $allLiterals.Contains($key) }
            if (-not $used -and -not (Test-AllowedResourceKey -Assembly $project.Assembly -Key $key -Rules $allowlist.DynamicKeys)) {
                $unused.Add([pscustomobject]@{ Assembly = $project.Assembly; Key = $key; Path = $project.Path })
            }

            if ($project.Assembly -ne 'Deckle.App' -and $null -ne $appMap -and $appMap.ContainsKey($key) -and $used -and $rootReferences.Contains($key) -and $project.Entries[$key] -cne $appMap[$key] -and -not (Test-IntentionalDivergence -Assembly $project.Assembly -Key $key -Rules $allowlist.IntentionalDivergences)) {
                $divergences.Add([pscustomobject]@{ Assembly = $project.Assembly; Key = $key; ModuleValue = $project.Entries[$key]; RootValue = $appMap[$key] })
            }
        }
    }

    return [pscustomobject]@{
        Maps = $projects.Count
        Keys = [int](($projects | ForEach-Object { $_.Entries.Count } | Measure-Object -Sum).Sum)
        Missing = @($missing)
        PotentiallyUnused = @($unused)
        Divergences = @($divergences)
        DynamicRules = @($allowlist.DynamicKeys).Count
        RequiredRules = @($allowlist.RequiredKeys).Count
    }
}

Export-ModuleMember -Function Invoke-ResourceInventory, Get-ReswEntries
