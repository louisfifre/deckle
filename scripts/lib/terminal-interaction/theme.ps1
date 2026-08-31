# Semantic presentation roles mapped to the default Deckle terminal theme.
# Exact RGB tokens serve VT hosts; ConsoleColor values remain as the legacy fallback.

# The indexed background is only a hint: Windows Terminal sets a profile's RGB
# background apart from its sixteen-colour table, so Console.BackgroundColor
# does not tell every profile's real background.
function Resolve-TerminalThemeVariant {
    param([Parameter(Mandatory)][ConsoleColor]$BackgroundColor)

    if ($BackgroundColor -in @(
        [ConsoleColor]::Black,
        [ConsoleColor]::DarkBlue,
        [ConsoleColor]::DarkGreen,
        [ConsoleColor]::DarkCyan,
        [ConsoleColor]::DarkRed,
        [ConsoleColor]::DarkMagenta,
        [ConsoleColor]::DarkYellow,
        [ConsoleColor]::DarkGray
    )) {
        return 'Dark'
    }
    return 'Light'
}

# The maintainer's palette, Figma "Deckle Scripts", node 22-27: title,
# sections, action-row title, body text, selection, access, and background.
# It names no colour for a plain Action, muted chrome, or outcomes; those
# roles keep their ConsoleColor fallback rather than an invented value.
function Get-TerminalThemePalette {
    param([Parameter(Mandatory)][ValidateSet('Dark', 'Light')][string]$ThemeVariant)

    if ($ThemeVariant -eq 'Light') {
        return [pscustomobject]@{
            Background = [int[]]@(246, 246, 246)  # F6F6F6
            Title = [int[]]@(40, 142, 215)        # 288ED7
            ActionLabel = [int[]]@(29, 117, 109)  # 1D756D
            Section = [int[]]@(192, 60, 124)      # C03C7C
            Selection = [int[]]@(53, 53, 53)      # 353535
            Body = [int[]]@(83, 83, 83)           # 535353
            Access = [int[]]@(106, 138, 32)       # 6A8A20
        }
    }
    return [pscustomobject]@{
        Background = [int[]]@(1, 22, 39)          # 011627
        Title = [int[]]@(130, 170, 255)           # 82AAFF
        ActionLabel = [int[]]@(119, 207, 192)     # 77CFC0
        Section = [int[]]@(199, 146, 234)         # C792EA
        Selection = [int[]]@(255, 255, 255)       # FFFFFF
        Body = [int[]]@(204, 204, 204)            # CCCCCC
        Access = [int[]]@(186, 217, 115)          # BAD973
    }
}

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
        [ValidateSet('Supported', 'Unsupported', 'Unknown')][string]$ColorCapability = 'Supported',
        [ValidateSet('Dark', 'Light')][string]$ThemeVariant = 'Dark'
    )

    if ($ColorCapability -ne 'Supported') {
        return [pscustomobject]@{
            Foreground = $null
            Background = $null
            ForegroundRgb = $null
            BackgroundRgb = $null
        }
    }

    $palette = Get-TerminalThemePalette -ThemeVariant $ThemeVariant
    if ($State -eq 'Disabled') {
        return [pscustomobject]@{
            Foreground = [ConsoleColor]::DarkGray
            Background = $null
            ForegroundRgb = $null
            BackgroundRgb = $palette.Background
        }
    }
    # One selection for every target, Danger, Error and Exit included: the
    # focused text takes the background token and the cell the selection
    # token, so a consequence shows at rest and selection keeps one language.
    if ($State -eq 'Focused') {
        $foreground = if ($ThemeVariant -eq 'Dark') { [ConsoleColor]::Black } else { [ConsoleColor]::White }
        $background = if ($ThemeVariant -eq 'Dark') { [ConsoleColor]::White } else { [ConsoleColor]::DarkGray }
        return [pscustomobject]@{
            Foreground = $foreground
            Background = $background
            ForegroundRgb = $palette.Background
            BackgroundRgb = $palette.Selection
        }
    }

    $foreground = switch ($Role) {
        'Banner' { [ConsoleColor]::Blue }
        'Context' { [ConsoleColor]::DarkGray }
        'Section' { [ConsoleColor]::Magenta }
        'SectionSeparator' { [ConsoleColor]::Gray }
        # A plain Action reads as body text, like a Variant; only the label of
        # an Action Row, or of a compact Action Group, takes the teal of the
        # launcher's row titles.
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
    $foregroundRgb = switch ($Role) {
        'Banner' { $palette.Title }
        'Section' { $palette.Section }
        'PanelTitle' { $palette.Section }
        'SectionSeparator' { $palette.Selection }
        'PanelSeparator' { $palette.Body }
        'Action' { $palette.Body }
        'ActionLabel' { $palette.ActionLabel }
        'Access' { $palette.Access }
        'ActionVariant' { $palette.Body }
        'Adjust' { $palette.Body }
        'Navigation' { $palette.Body }
        'Body' { $palette.Body }
        'CommandKey' { $palette.Body }
        default { $null }
    }
    return [pscustomobject]@{
        Foreground = $foreground
        Background = $null
        ForegroundRgb = $foregroundRgb
        BackgroundRgb = $palette.Background
    }
}
