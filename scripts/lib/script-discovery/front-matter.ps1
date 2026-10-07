# Markdown front matter, read through the shared key-value reader.

function Get-DeckleMarkdownFrontMatter {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }

    # Front matter sits at the top; 120 lines bound the cost on large documents.
    $lines = @(Get-Content -LiteralPath $Path -TotalCount 120 -Encoding UTF8 -ErrorAction SilentlyContinue)
    if ($lines.Count -lt 2) { return $null }
    if ($lines[0].Trim() -ne '---') { return $null }

    $closing = -1
    for ($index = 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index].Trim() -eq '---') { $closing = $index; break }
    }
    if ($closing -lt 0) { return $null }
    if ($closing -eq 1) { return $null }

    $pairs = @(Read-DeckleKeyValueLines -Lines $lines[1..($closing - 1)])
    $fields = ConvertTo-DeckleKeyValueTable -Pairs $pairs
    if ($fields.Count -eq 0) { return $null }
    return $fields
}
