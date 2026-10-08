# Semantic presentation roles mapped to terminal attributes, from one of two
# palettes. Terminal, the default, names every role in the sixteen console
# colours and lets the terminal's own scheme give them their values. Deckle
# carries the designed RGB tokens, sent exactly to VT hosts, with the console
# colours kept as the fallback of a legacy host.

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

# Resolves the launcher's palette choice into the one the theme applies: the
# Deckle palette takes its dark or light variant from the console background.
function Resolve-TerminalPalette {
    param(
        [Parameter(Mandatory)][ValidateSet('Terminal', 'Deckle')][string]$Palette,
        [Parameter(Mandatory)][ConsoleColor]$BackgroundColor
    )

    if ($Palette -eq 'Terminal') { return 'Terminal' }
    return 'Deckle' + (Resolve-TerminalThemeVariant -BackgroundColor $BackgroundColor)
}

# The maintainer's palette, Figma "Deckle Scripts", node 22-27: title,
# sections, action-row title, body text, selection, access, and background.
# It names no colour for a plain Action, muted chrome, or outcomes; those
# roles keep their ConsoleColor fallback rather than an invented value.
function Get-TerminalThemePalette {
    param([Parameter(Mandatory)][ValidateSet('DeckleDark', 'DeckleLight')][string]$Palette)

    if ($Palette -eq 'DeckleLight') {
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
        [ValidateSet('Terminal', 'DeckleDark', 'DeckleLight')][string]$Palette = 'Terminal'
    )

    if ($ColorCapability -ne 'Supported') {
        return [pscustomobject]@{
            Foreground = $null
            Background = $null
            ForegroundRgb = $null
            BackgroundRgb = $null
        }
    }

    $designed = if ($Palette -eq 'Terminal') { $null } else { Get-TerminalThemePalette -Palette $Palette }
    $surfaceRgb = if ($null -eq $designed) { $null } else { $designed.Background }
    if ($State -eq 'Disabled') {
        return [pscustomobject]@{
            Foreground = [ConsoleColor]::DarkGray
            Background = $null
            ForegroundRgb = $null
            BackgroundRgb = $surfaceRgb
        }
    }
    if ($State -eq 'Focused' -and $null -eq $designed) {
        # The console-colour selection: Danger and Error invert to white on
        # dark red, every other target, Exit included, to black on grey.
        $consequence = $Role -in @('Danger', 'Error')
        return [pscustomobject]@{
            Foreground = if ($consequence) { [ConsoleColor]::White } else { [ConsoleColor]::Black }
            Background = if ($consequence) { [ConsoleColor]::DarkRed } else { [ConsoleColor]::Gray }
            ForegroundRgb = $null
            BackgroundRgb = $null
        }
    }
    # The designed selection is one for every target, Danger, Error and Exit
    # included: the focused text takes the background token and the cell the
    # selection token, so a consequence shows at rest and selection keeps one
    # language.
    if ($State -eq 'Focused') {
        $foreground = if ($Palette -eq 'DeckleDark') { [ConsoleColor]::Black } else { [ConsoleColor]::White }
        $background = if ($Palette -eq 'DeckleDark') { [ConsoleColor]::White } else { [ConsoleColor]::DarkGray }
        return [pscustomobject]@{
            Foreground = $foreground
            Background = $background
            ForegroundRgb = $designed.Background
            BackgroundRgb = $designed.Selection
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
    $foregroundRgb = if ($null -eq $designed) { $null } else {
        switch ($Role) {
            'Banner' { $designed.Title }
            'Section' { $designed.Section }
            'PanelTitle' { $designed.Section }
            'SectionSeparator' { $designed.Selection }
            'PanelSeparator' { $designed.Body }
            'Action' { $designed.Body }
            'ActionLabel' { $designed.ActionLabel }
            'Access' { $designed.Access }
            'ActionVariant' { $designed.Body }
            'Adjust' { $designed.Body }
            'Navigation' { $designed.Body }
            'Body' { $designed.Body }
            'CommandKey' { $designed.Body }
            default { $null }
        }
    }
    return [pscustomobject]@{
        Foreground = $foreground
        Background = $null
        ForegroundRgb = $foregroundRgb
        BackgroundRgb = $surfaceRgb
    }
}
