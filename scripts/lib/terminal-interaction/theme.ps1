# Semantic presentation roles mapped to the default Deckle terminal theme.
# ConsoleColor values remain here so descriptors and layout stay host-agnostic.

function Get-TerminalPresentationStyle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet(
            'Banner', 'Context', 'Section', 'SectionSeparator',
            'Action', 'ActionVariant', 'Access', 'Adjust', 'Navigation', 'Exit', 'Danger',
            'PanelTitle', 'Body', 'Supporting', 'Separator',
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
    if ($State -eq 'Focused' -and $Role -in @('Danger', 'Exit', 'Error')) {
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
        'Action' { [ConsoleColor]::Cyan }
        'ActionVariant' { $null }
        'Access' { [ConsoleColor]::DarkYellow }
        'Adjust' { [ConsoleColor]::DarkYellow }
        'Navigation' { [ConsoleColor]::DarkGray }
        'Exit' { [ConsoleColor]::Red }
        'Danger' { [ConsoleColor]::Red }
        'PanelTitle' { [ConsoleColor]::Magenta }
        'Supporting' { [ConsoleColor]::DarkGray }
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
