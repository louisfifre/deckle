# Catalog builder: one Level per folder, Action Rows grouped by Section,
# sub-folders as Accesses, and the scripts left out with their reason.

function Get-CatalogRelativePath {
    param(
        [Parameter(Mandatory)][string]$RootPath,
        [Parameter(Mandatory)][string]$Path
    )

    # Every path comes from enumerating beneath the root, so the root is a prefix.
    if ($Path.Length -le $RootPath.Length) { return '' }
    return $Path.Substring($RootPath.Length).TrimStart('\', '/').Replace('\', '/')
}

function Compare-OrderThenLabel {
    param($Left, $Right)

    # A lower rank comes first; then ordered entries, ascending; the rest follow
    # by label. The original index breaks every tie, which makes List.Sort stable.
    if ($Left.Rank -ne $Right.Rank) { return $Left.Rank.CompareTo($Right.Rank) }
    $leftHasOrder = $null -ne $Left.Order
    $rightHasOrder = $null -ne $Right.Order
    if ($leftHasOrder -ne $rightHasOrder) { if ($leftHasOrder) { return -1 } else { return 1 } }
    if ($leftHasOrder -and $Left.Order -ne $Right.Order) { return $Left.Order.CompareTo($Right.Order) }
    $byLabel = [System.StringComparer]::OrdinalIgnoreCase.Compare([string]$Left.Label, [string]$Right.Label)
    if ($byLabel -ne 0) { return $byLabel }
    if ($Left.Position -ne $Right.Position) { return $Left.Position.CompareTo($Right.Position) }
    return $Left.Index.CompareTo($Right.Index)
}

function Sort-CatalogEntries {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Entries)

    $list = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $Entries.Count; $index++) {
        $entry = $Entries[$index]
        $list.Add([pscustomobject]@{
            Item = $entry.Item
            Order = $entry.Order
            Label = $entry.Label
            Rank = if ($entry.PSObject.Properties['Rank']) { [int]$entry.Rank } else { 0 }
            Position = if ($entry.PSObject.Properties['Position']) { [int]$entry.Position } else { 0 }
            Index = $index
        })
    }
    $list.Sort([System.Comparison[object]]{
        param($left, $right)
        Compare-OrderThenLabel -Left $left -Right $right
    })
    return @($list | ForEach-Object Item)
}

function New-CatalogRows {
    param(
        [Parameter(Mandatory)][PSTypeName('DeckleScripts.ScriptDiscovery.Header')][object]$Header,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $rows = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $Header.Actions.Count; $index++) {
        $action = $Header.Actions[$index]
        $rows.Add([pscustomobject][ordered]@{
            PSTypeName = 'DeckleScripts.ScriptDiscovery.ActionRow'
            ScriptPath = $Header.Path
            ScriptName = $Header.Name
            RelativePath = $RelativePath
            Label = $action.Label
            Description = $Header.Description
            Arguments = $action.Arguments
            Danger = $action.Danger
            Confirmation = $action.Confirmation
            VariantParameter = $Header.VariantParameter
            Variants = @($Header.Variants)
            Parameters = @($Header.Parameters)
            Section = $Header.Section
            Order = $Header.Order
            ActionIndex = $index
        })
    }
    return @($rows)
}

function New-CatalogLevel {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$RootPath,
        [string[]]$ExcludeFolder = @()
    )

    $name = Split-Path -Leaf $Path
    $level = [pscustomobject][ordered]@{
        PSTypeName = 'DeckleScripts.ScriptDiscovery.Level'
        Path = $Path
        Name = $name
        RelativePath = Get-CatalogRelativePath -RootPath $RootPath -Path $Path
        Label = $name
        Description = $null
        Section = $null
        Order = $null
        Sections = @()
        Accesses = @()
        Hidden = @()
        # False for a folder with no described script anywhere beneath it:
        # it stays in the catalog, so the compliance check still reports its
        # hidden scripts, but no menu offers it as an Access.
        HasActions = $false
    }

    $frontMatter = Get-MarkdownFrontMatter -Path (Join-Path $Path 'README.md')
    if ($null -ne $frontMatter) {
        if (-not [string]::IsNullOrWhiteSpace($frontMatter['name'])) { $level.Label = $frontMatter['name'].Trim() }
        if (-not [string]::IsNullOrWhiteSpace($frontMatter['description'])) { $level.Description = $frontMatter['description'].Trim() }
        if (-not [string]::IsNullOrWhiteSpace($frontMatter['section'])) { $level.Section = $frontMatter['section'].Trim() }
        $orderValue = 0
        if ($frontMatter['order'] -and [int]::TryParse(([string]$frontMatter['order']).Trim(), [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$orderValue)) {
            $level.Order = $orderValue
        }
    }

    # Only .ps1 files are candidates; -Filter would also match longer
    # extensions through 8.3 names, so the extension is compared exactly.
    $scriptFiles = @(Get-ChildItem -LiteralPath $Path -File |
        Where-Object { $_.Extension -ieq '.ps1' } |
        Sort-Object -Property Name)

    $scriptEntries = [System.Collections.Generic.List[object]]::new()
    $hidden = [System.Collections.Generic.List[object]]::new()
    foreach ($file in $scriptFiles) {
        $header = Read-ScriptHeader -Path $file.FullName
        $relativePath = Get-CatalogRelativePath -RootPath $RootPath -Path $file.FullName
        if ($header.Status -ne 'Described') {
            $hidden.Add([pscustomobject]@{
                Path = $file.FullName
                RelativePath = $relativePath
                Status = $header.Status
                Reason = $header.Reason
            })
            continue
        }
        $rows = @(New-CatalogRows -Header $header -RelativePath $relativePath)
        if ($rows.Count -eq 0) { continue }
        # A Section ranks its rows in three bands: Action Rows with Variants
        # first, as one unit per script so they stay consecutive; then the
        # plain Actions; then the Danger Actions, last, where a hand does not
        # reach them by accident. Inside a band the script's order and
        # synopsis sort, then the action's position, so the actions of one
        # script keep their declaration order.
        if (@($rows[0].Variants).Count -gt 0) {
            $scriptEntries.Add([pscustomobject]@{ Item = $rows; Order = $header.Order; Label = $header.Label; Rank = 0; Position = 0 })
            continue
        }
        foreach ($row in $rows) {
            $rank = if ($row.Danger) { 2 } else { 1 }
            $scriptEntries.Add([pscustomobject]@{ Item = @($row); Order = $header.Order; Label = $header.Label; Rank = $rank; Position = $row.ActionIndex })
        }
    }

    $groups = [System.Collections.Specialized.OrderedDictionary]::new([System.StringComparer]::OrdinalIgnoreCase)
    $untitled = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $scriptEntries) {
        $section = $entry.Item[0].Section
        if ($null -eq $section) { $untitled.Add($entry); continue }
        if (-not $groups.Contains($section)) {
            $groups[$section] = [pscustomobject]@{ Label = $section; Entries = [System.Collections.Generic.List[object]]::new() }
        }
        $groups[$section].Entries.Add($entry)
    }

    $sections = [System.Collections.Generic.List[object]]::new()
    if ($untitled.Count -gt 0) {
        $sortedScripts = @(Sort-CatalogEntries -Entries @($untitled))
        $sections.Add([pscustomobject]@{ Label = $null; Order = $null; Rows = @($sortedScripts | ForEach-Object { $_ }) })
    }
    $namedEntries = foreach ($group in $groups.Values) {
        $ordered = @($group.Entries | Where-Object { $null -ne $_.Order } | ForEach-Object Order)
        $smallest = if ($ordered.Count -gt 0) { ($ordered | Measure-Object -Minimum).Minimum } else { $null }
        $sortedScripts = @(Sort-CatalogEntries -Entries @($group.Entries))
        $sectionValue = [pscustomobject]@{
            Label = $group.Label
            Order = if ($null -ne $smallest) { [int]$smallest } else { $null }
            Rows = @($sortedScripts | ForEach-Object { $_ })
        }
        [pscustomobject]@{ Item = $sectionValue; Order = $sectionValue.Order; Label = $sectionValue.Label }
    }
    foreach ($section in @(Sort-CatalogEntries -Entries @($namedEntries))) { $sections.Add($section) }
    $level.Sections = @($sections)

    # The exclusions apply to the root's direct children only: a launcher
    # keeps its library and tests out of the menu, nothing deeper.
    $isRoot = $Path -eq $RootPath
    $accessEntries = foreach ($directory in @(Get-ChildItem -LiteralPath $Path -Directory |
            Where-Object { -not $_.Name.StartsWith('.') } |
            Where-Object { -not ($isRoot -and $_.Name -in $ExcludeFolder) } |
            Sort-Object -Property Name)) {
        $child = New-CatalogLevel -Path $directory.FullName -RootPath $RootPath
        [pscustomobject]@{ Item = $child; Order = $child.Order; Label = $child.Label }
    }
    $level.Accesses = @(Sort-CatalogEntries -Entries @($accessEntries))
    $level.Hidden = @($hidden)
    $level.HasActions = $sections.Count -gt 0 -or @($level.Accesses | Where-Object HasActions).Count -gt 0
    return $level
}

function Get-ScriptCatalog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        # Names of root folders that never enter the catalog, such as the
        # launcher's own library and tests.
        [string[]]$ExcludeFolder = @()
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Scripts folder '$Path' does not exist."
    }
    $rootPath = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).ProviderPath).TrimEnd('\', '/')
    return New-CatalogLevel -Path $rootPath -RootPath $rootPath -ExcludeFolder $ExcludeFolder
}

function Get-CatalogHiddenScripts {
    [CmdletBinding()]
    param([Parameter(Mandatory)][PSTypeName('DeckleScripts.ScriptDiscovery.Level')][object]$Catalog)

    $hidden = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $Catalog.Hidden) { $hidden.Add($entry) }
    foreach ($access in $Catalog.Accesses) {
        foreach ($entry in @(Get-CatalogHiddenScripts -Catalog $access)) { $hidden.Add($entry) }
    }
    return @($hidden)
}

function Get-CatalogRows {
    [CmdletBinding()]
    param([Parameter(Mandatory)][PSTypeName('DeckleScripts.ScriptDiscovery.Level')][object]$Catalog)

    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($section in $Catalog.Sections) {
        foreach ($row in $section.Rows) { $rows.Add($row) }
    }
    foreach ($access in $Catalog.Accesses) {
        foreach ($row in @(Get-CatalogRows -Catalog $access)) { $rows.Add($row) }
    }
    return @($rows)
}
