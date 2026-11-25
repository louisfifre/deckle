# logo.ps1 - The repository's name drawn in block letters, for the logo.
#
# Each glyph is two lines of half blocks, so a letter is two terminal rows
# high and two to five cells wide; one blank cell separates two glyphs. A
# name is drawn in capitals. A character the font does not hold leaves the
# logo out altogether, so the Header falls back on its Banner line rather
# than spelling a name wrongly.

$script:LauncherLogoFont = @{
    'A' = @('▄▀█', '█▀█'); 'B' = @('█▄▄', '█▄█'); 'C' = @('█▀▀', '█▄▄'); 'D' = @('█▀▄', '█▄▀')
    'E' = @('█▀▀', '██▄'); 'F' = @('█▀▀', '█▀ '); 'G' = @('█▀▀', '█▄█'); 'H' = @('█ █', '█▀█')
    'I' = @('█', '█'); 'J' = @('  █', '█▄█'); 'K' = @('█▄▀', '█ █'); 'L' = @('█  ', '█▄▄')
    'M' = @('█▀▄▀█', '█ ▀ █'); 'N' = @('█▄ █', '█ ▀█'); 'O' = @('█▀█', '█▄█'); 'P' = @('█▀█', '█▀▀')
    'Q' = @('█▀█', '▀▀█'); 'R' = @('█▀█', '█▀▄'); 'S' = @('█▀', '▄█'); 'T' = @('▀█▀', ' █ ')
    'U' = @('█ █', '█▄█'); 'V' = @('█ █', '▀▄▀'); 'W' = @('█ █ █', '▀▄▀▄▀'); 'X' = @('▀▄▀', '█ █')
    'Y' = @('█▄█', ' █ '); 'Z' = @('▀█', '█▄')
    '0' = @('█▀█', '█▄█'); '1' = @('▄█', ' █'); '2' = @('▀█', '█▄'); '3' = @('▀▀█', '▄██')
    '4' = @('█ █', '▀▀█'); '5' = @('█▀', '▄█'); '6' = @('█▄▄', '█▄█'); '7' = @('▀▀█', '  █')
    '8' = @('███', '█▄█'); '9' = @('█▀█', '▀▀█')
    '-' = @('▄▄', '  '); '_' = @('  ', '▄▄'); '.' = @(' ', '▄'); ' ' = @('  ', '  ')
}

function ConvertTo-LauncherLogo {
    param([AllowEmptyString()][string]$Text)

    $top = [System.Collections.Generic.List[string]]::new()
    $bottom = [System.Collections.Generic.List[string]]::new()
    foreach ($character in $Text.ToUpperInvariant().ToCharArray()) {
        $glyph = $script:LauncherLogoFont[[string]$character]
        if ($null -eq $glyph) { return @() }
        $top.Add($glyph[0])
        $bottom.Add($glyph[1])
    }
    if ($top.Count -eq 0) { return @() }
    return @(($top -join ' ').TrimEnd(), ($bottom -join ' ').TrimEnd())
}
