# Semantic presentation roles mapped to the default Deckle terminal theme.
# ConsoleColor values remain here so descriptors and layout stay host-agnostic.

function Get-TerminalPresentationStyle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet(
            'Banner', 'Context', 'Section', 'SectionSeparator',
            'Action', 'ActionLabel', 'ActionVariant', 'Access', 'Adjust', 'Navigation', 'Exit', 'Danger',
            'PanelTitle', 'Body', 'Supporting', 'PrimarySeparator', 'PanelSeparator', 'PagingSeparator', 'Separator',
            'CommandKey', 'CommandLabel', 'Success', 'Warning', 'Error'
        )]
        [string]$Role,
        [ValidateSet('Normal', 'Focused', 'Disabled')][string]$State = 'Normal',
        [ValidateSet('Supported', 'Unsupported', 'Unknown')][string]$ColorCapability = 'Supported'
    )

    if ($ColorCapability -ne 'Supported') {
        return [pscustomobject]@{
            Foreground = $null
            Background = $null
        }
    }

    if ($State -eq 'Disabled') {
        return [pscustomobject]@{
            Foreground = [ConsoleColor]::DarkGray
            Background = $null
        }
    }
    if ($State -eq 'Focused' -and $Role -in @('Danger', 'Error')) {
        return [pscustomobject]@{
            Foreground = [ConsoleColor]::White
            Background = [ConsoleColor]::DarkRed
        }
    }
    if ($State -eq 'Focused') {
        return [pscustomobject]@{
            Foreground = [ConsoleColor]::Black
            Background = [ConsoleColor]::Gray
        }
    }

    $foreground = switch ($Role) {
        'Banner' { [ConsoleColor]::Blue }
        'Context' { [ConsoleColor]::DarkGray }
        'Section' { [ConsoleColor]::Magenta }
        'SectionSeparator' { [ConsoleColor]::Gray }
        # A plain Action reads as body text, like a Variant; only the label of
        # an Action Row takes the teal of the launcher's row titles. The
        # maintainer's palette (Figma "Deckle Scripts", node 22-27) names
        # title, sections, action-row title, body text, selection, access.
        'Action' { $null }
        'ActionLabel' { [ConsoleColor]::Cyan }
        'ActionVariant' { $null }
        'Access' { [ConsoleColor]::DarkYellow }
        'Adjust' { $null }
        'Navigation' { [ConsoleColor]::DarkGray }
        'Exit' { [ConsoleColor]::Red }
        'Danger' { [ConsoleColor]::Red }
        'PanelTitle' { [ConsoleColor]::Magenta }
        'Supporting' { [ConsoleColor]::DarkGray }
        'PrimarySeparator' { [ConsoleColor]::DarkGray }
        'PanelSeparator' { [ConsoleColor]::Gray }
        'PagingSeparator' { [ConsoleColor]::DarkGray }
        'Separator' { [ConsoleColor]::DarkGray }
        'CommandKey' { [ConsoleColor]::Gray }
        'CommandLabel' { [ConsoleColor]::DarkGray }
        'Success' { [ConsoleColor]::Green }
        'Warning' { [ConsoleColor]::Yellow }
        'Error' { [ConsoleColor]::Red }
        default { $null }
    }
    return [pscustomobject]@{
        Foreground = $foreground
        Background = $null
    }
}
