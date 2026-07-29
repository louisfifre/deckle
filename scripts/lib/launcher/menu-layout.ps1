# Pure layout transformations for launcher menu definitions.
function ConvertTo-MenuRows {
    param(
        [Parameter(Mandatory)][object[]]$Sections,
        [ValidateRange(1, 10)][int]$Columns = 2
    )

    foreach ($section in $Sections) {
        # Hashtable dot access collides with IDictionary.Items when a section
        # contains exactly one item. Index by key so the declared cells survive.
        $items = @(if ($section.ContainsKey('Items')) { $section['Items'] } else { $section['Cells'] })
        for ($i = 0; $i -lt $items.Count; $i += $Columns) {
            $lastIndex = [Math]::Min($i + $Columns - 1, $items.Count - 1)
            $cells = @(
                for ($itemIndex = $i; $itemIndex -le $lastIndex; $itemIndex++) {
                    $items[$itemIndex]
                }
            )
            @{
                Prefix = if ($i -eq 0) { $section.Prefix } else { '' }
                Cells = $cells
            }
        }
    }
}

function Get-DeckleMainMenuRows {
    $rows = [System.Collections.Generic.List[object]]::new()

    foreach ($row in @(
        @{ Title  = 'Run' }
        @{ Prefix = 'Launch';         Cells = @( @{ Label = 'Release'; Value = 'launch:Release' }, @{ Label = 'Debug'; Value = 'launch:Debug' } ) }
        @{ Prefix = 'Build & run';    Cells = @( @{ Label = 'Release'; Value = 'run:Release' },    @{ Label = 'Debug'; Value = 'run:Debug' } ) }
        @{ Prefix = 'Build (no run)'; Cells = @( @{ Label = 'Release'; Value = 'norun:Release' },  @{ Label = 'Debug'; Value = 'norun:Debug' } ) }
        @{ Title  = 'Project' }
    )) { $rows.Add($row) }

    foreach ($row in (ConvertTo-MenuRows -Columns 2 -Sections @(
        @{ Items = @(
            @{ Label = 'Update README pulse'; Value = 'readme-stats' }
            @{ Label = 'Update changelog';    Value = 'changelog' }
            @{ Label = 'Record version';      Value = 'record-version' }
        ) }
    ))) { $rows.Add($row) }

    $rows.Add(@{ Title = 'Release' })
    foreach ($row in (ConvertTo-MenuRows -Columns 2 -Sections @(
        @{ Items = @(
            @{ Label = 'Publish app release';              Value = 'publish' }
            @{ Label = 'Prepare app artifacts';    Value = 'artifacts' }
            @{ Label = 'Prepare native runtime';   Value = 'native' }
        ) }
    ))) { $rows.Add($row) }

    $rows.Add(@{ Title = 'More' })
    $rows.Add(@{ Cells = @(
        @{ Label = 'Maintenance…'; Value = 'maintenance-menu'; Role = 'folder' }
        @{ Label = 'Setup…';       Value = 'setup-menu';       Role = 'folder' }
    ) })
    $rows.Add(@{ ColumnOffset = 1; Cells = @(
        @{ Label = 'Quit'; Value = 'quit' }
    ) })

    return @($rows)
}
