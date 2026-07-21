# Pure layout transformations for launcher menu definitions.
function ConvertTo-MenuRows {
    param(
        [Parameter(Mandatory)][object[]]$Sections,
        [ValidateRange(1, 10)][int]$Columns = 2
    )

    foreach ($section in $Sections) {
        $items = if ($section.ContainsKey('Items')) { @($section.Items) } else { @($section.Cells) }
        for ($i = 0; $i -lt $items.Count; $i += $Columns) {
            @{
                Prefix = if ($i -eq 0) { $section.Prefix } else { '' }
                Cells = @($items[$i..([Math]::Min($i + $Columns - 1, $items.Count - 1))])
            }
        }
    }
}
