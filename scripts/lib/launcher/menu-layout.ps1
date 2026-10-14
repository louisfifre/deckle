# Pure layout transformations for launcher menu definitions.
function ConvertTo-MenuRows {
    param(
        [Parameter(Mandatory)][object[]]$Sections,
        [ValidateRange(1, 10)][int]$Columns = 2,
        [switch]$SeparateSections
    )

    for ($sectionIndex = 0; $sectionIndex -lt $Sections.Count; $sectionIndex++) {
        $section = $Sections[$sectionIndex]
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
        if ($SeparateSections -and $sectionIndex -lt $Sections.Count - 1) {
            @{ Blank = $true }
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
        @{ Blank  = $true }
        @{ Title  = 'Workspace' }
        @{ Cells = @(
            @{ Label = 'Project…'; Value = 'project-menu'; Role = 'folder' }
            @{ Label = 'Release…'; Value = 'release-menu'; Role = 'folder' }
        ) }
        @{ Cells = @(
            @{ Label = 'Maintenance…'; Value = 'maintenance-menu'; Role = 'folder' }
            @{ Label = 'Setup…';       Value = 'setup-menu';       Role = 'folder' }
        ); TrailingCell = @{ Label = 'Quit'; Value = 'quit'; Role = 'quit' } }
    )) { $rows.Add($row) }

    return @($rows)
}

# Selection state that lands on Quit: the last selectable row, in the column
# right after its regular cells, which the grid keeps for the trailing cell.
# Computed from the rows alone so the launcher never needs the grid plan.
function Get-DeckleQuitSelection {
    param([Parameter(Mandatory)][object[]]$MenuRows)

    $selectable = @($MenuRows | Where-Object { $_.ContainsKey('Cells') })
    $quitIndex = [Array]::FindLastIndex($selectable, [Predicate[object]]{
        param($row)
        $row.ContainsKey('TrailingCell') -and $row['TrailingCell']['Value'] -eq 'quit'
    })
    if ($quitIndex -lt 0) { return $null }

    $quitRow = $selectable[$quitIndex]
    $columnOffset = if ($quitRow.ContainsKey('ColumnOffset')) { [int]$quitRow['ColumnOffset'] } else { 0 }
    return @{ Index = $quitIndex; PreferredColumn = $columnOffset + @($quitRow['Cells']).Count }
}
