# Semantic presentation roles mapped to terminal attributes, named in the
# sixteen console colours only, so the terminal's own scheme gives every role
# its value and one theme serves light and dark terminals alike. A null colour
# means the terminal's own default, which the host sends as such: the theme
# never fills it from the console's reported colours, which Windows Terminal
# and VS Code give as grey on black whatever the profile shows.
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
    if ($State -eq 'Focused') {
        # The selection: Danger and Error invert to white on dark red, every
        # other target, Exit included, to black on grey.
        $consequence = $Role -in @('Danger', 'Error')
        return [pscustomobject]@{
            Foreground = if ($consequence) { [ConsoleColor]::White } else { [ConsoleColor]::Black }
            Background = if ($consequence) { [ConsoleColor]::DarkRed } else { [ConsoleColor]::Gray }
        }
    }

    $foreground = switch ($Role) {
        'Banner' { [ConsoleColor]::Blue }
        'Context' { [ConsoleColor]::DarkGray }
        'Section' { [ConsoleColor]::Magenta }
        # The rule after a Section heading stays muted chrome, below the
        # heading it extends.
        'SectionSeparator' { [ConsoleColor]::DarkGray }
        # A plain Action reads as body text, like a Variant; only the label of
        # an Action Row takes the teal of the launcher's row titles.
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
