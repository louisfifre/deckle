$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'

$root = Join-Path ([IO.Path]::GetTempPath()) "deckle-readme-stats-$([guid]::NewGuid())"
$scriptPath = Join-Path $root 'scripts\update-readme-stats.ps1'

function Invoke-TestGit {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    $output = & git -C $root @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed" }
    return $output
}

try {
    $null = New-Item -ItemType Directory -Path $root
    $null = New-Item -ItemType Directory -Path (Join-Path $root 'scripts\lib')
    Copy-Item -LiteralPath (Join-Path $ScriptsDir 'update-readme-stats.ps1') -Destination $scriptPath
    Copy-Item -LiteralPath (Join-Path $LibDir 'action-summary.ps1') -Destination (Join-Path $root 'scripts\lib\action-summary.ps1')
    Copy-Item -LiteralPath (Join-Path $LibDir 'script-output.ps1') -Destination (Join-Path $root 'scripts\lib\script-output.ps1')
    Invoke-TestGit init --initial-branch=main | Out-Null
    Invoke-TestGit config user.name 'Deckle Tests' | Out-Null
    Invoke-TestGit config user.email 'deckle-tests@example.invalid' | Out-Null
    Invoke-TestGit config core.autocrlf false | Out-Null

    @'
# Deckle test

<!-- deckle-stats:start -->
old pulse
<!-- deckle-stats:end -->

---

Stable content.
'@ | Set-Content -LiteralPath (Join-Path $root 'README.md') -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $root 'tracked.txt') -Value 'tracked' -Encoding utf8NoBOM
    Invoke-TestGit add README.md tracked.txt scripts | Out-Null
    Invoke-TestGit commit -m 'test: seed repository' | Out-Null

    Push-Location (Split-Path -Parent $root)
    try {
        & $scriptPath
    } finally {
        Pop-Location
    }
    if (-not @(Invoke-TestGit diff --name-only -- README.md).Count) {
        throw 'README update should leave a pending generated change without -Commit'
    }

    & $scriptPath -Target $root -Commit
    $subject = @(Invoke-TestGit log -1 --format=%s)[0]
    if ($subject -cne 'docs(readme): refresh development pulse') {
        throw "README update created the wrong commit: $subject"
    }
    if (@(Invoke-TestGit status --porcelain).Count -ne 0) {
        throw 'README commit should leave the repository clean'
    }

    # The pictures: the curve is one stretching svg (a viewBox, no width or
    # height, no text, no circle), the three labels are fixed-size svgs holding
    # one text each, and the README tags carry the sizes that make them keep
    # their shape. Light and dark of each.
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $readmeText = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw
    $lastLinePoints = @{}
    function Test-NoUnsafeContent {
        param([string]$Name, [xml]$Xml, [string]$Text)
        if (@($Xml.SelectNodes('//*[local-name()="rect" or local-name()="script" or local-name()="style" or local-name()="image" or local-name()="foreignObject"]')).Count -gt 0) {
            throw "$Name should hold no bar, script, style, image or foreignObject"
        }
        if ($Text -match '(?i)background|<svg[^>]*\sfill=') { throw "$Name should have a transparent background" }
        $scanned = $Text.Replace('http://www.w3.org/2000/svg', '')
        if ($scanned -match '(?i)\b[a-z]:[\\/]|@|https?:|\bon\w+=') { throw "$Name should hold no drive path, address, link or event handler" }
        foreach ($person in $env:USERNAME, $env:COMPUTERNAME) {
            if ($person -and $Text -match [regex]::Escape($person)) { throw "$Name should hold no personal data" }
        }
    }
    foreach ($theme in 'light', 'dark') {
        $relative = "docs/media/pulse-commits-$theme.svg"
        if ($readmeText -notlike "*$relative*") { throw "The pulse section should reference $relative" }
        $svgText = Get-Content -LiteralPath (Join-Path $root $relative) -Raw
        $xml = [xml]$svgText
        $svgRoot = $xml.DocumentElement
        if ($svgRoot.LocalName -ne 'svg') { throw "The $theme curve should be an svg document" }
        # Nothing that must keep its shape lives in a stretching picture.
        if (-not $svgRoot.HasAttribute('viewBox')) { throw "The $theme curve should have a viewBox on its root" }
        if ($svgRoot.GetAttribute('viewBox') -ne '0 0 800 40') { throw "The $theme curve viewBox should be 0 0 800 40" }
        if ($svgRoot.GetAttribute('preserveAspectRatio') -ne 'none') { throw "The $theme curve should stretch freely (preserveAspectRatio none)" }
        if ($svgRoot.HasAttribute('width') -or $svgRoot.HasAttribute('height')) { throw "The $theme curve should have no width or height, so that it fills the box the README gives it" }
        if (@($xml.SelectNodes('//*[local-name()="text" or local-name()="circle" or local-name()="ellipse"]')).Count -gt 0) {
            throw "The $theme curve should hold no text, circle or ellipse, and no nested svg"
        }
        if (@($xml.SelectNodes('/*/*[local-name()="svg"]')).Count -gt 0) { throw "The $theme curve should hold no nested svg" }
        Test-NoUnsafeContent -Name "The $theme curve" -Xml $xml -Text $svgText
        $box = @($svgRoot.GetAttribute('viewBox') -split '\s+' | ForEach-Object { [double]::Parse($_, $inv) })
        $paths = @($xml.SelectNodes('//*[local-name()="path"]'))
        $linePaths = @($paths | Where-Object { $_.GetAttribute('fill') -eq 'none' -and $_.GetAttribute('d') -match '^M[\d. ]+ C' })
        $areaPaths = @($paths | Where-Object { $_.GetAttribute('fill') -like 'url(#*' })
        $markerPaths = @($paths | Where-Object { $_.GetAttribute('fill') -eq 'none' -and $_.GetAttribute('d') -match '^M([\d.]+) ([\d.]+) L\1 \2$' })
        if ($paths.Count -ne 3 -or $linePaths.Count -ne 1 -or $areaPaths.Count -ne 1 -or $markerPaths.Count -ne 1) {
            throw "The $theme curve should hold one path for the line, one for the area and one zero-length path for the peak marker"
        }
        foreach ($stroked in $linePaths + $markerPaths) {
            if ($stroked.GetAttribute('vector-effect') -ne 'non-scaling-stroke') { throw "The $theme strokes should keep their thickness (non-scaling-stroke)" }
        }
        if ($markerPaths[0].GetAttribute('stroke-linecap') -ne 'round') { throw "The $theme peak marker should have round caps, so that it stays round" }
        $markerWidth = [double]::Parse($markerPaths[0].GetAttribute('stroke-width'), $inv)
        $markerNumbers = @([regex]::Matches($markerPaths[0].GetAttribute('d'), '\d+(\.\d+)?') | ForEach-Object { [double]::Parse($_.Value, $inv) })
        # Room for the stroke and the marker: 4 pixels of radius, one unit of y is one pixel, one unit of x is at least 0.375 pixel.
        if ($markerNumbers[1] -lt $markerWidth / 2 -or $markerNumbers[0] -lt $markerWidth / 2 / 0.375 -or $markerNumbers[0] -gt $box[2] - $markerWidth / 2 / 0.375) {
            throw "The $theme peak marker should not be clipped at the top or at the edges"
        }
        if ($linePaths[0].GetAttribute('stroke-linejoin') -ne 'round') { throw "The $theme line should have rounded joins" }
        if (@($xml.SelectNodes('//*[local-name()="title"]')).Count -ne 1 -or @($xml.SelectNodes('//*[local-name()="desc"]')).Count -ne 1) {
            throw "The $theme curve should keep its title and desc"
        }
        if (@($xml.SelectNodes('//*[local-name()="linearGradient"]')).Count -ne 1) { throw "The $theme curve should hold one gradient for the area" }
        $lineWidth = [double]::Parse($linePaths[0].GetAttribute('stroke-width'), $inv)
        foreach ($path in $linePaths + $areaPaths) {
            $d = $path.GetAttribute('d')
            if ($d -match '(?i)nan|infinity|e[+-]?\d') { throw "The $theme path should hold no NaN or exponent" }
            if ($d -notmatch '^M[\d. ]+( C[\d. ]+)+') { throw "The $theme path should be a smooth Bezier path" }
            $numbers = @([regex]::Matches($d, '-?\d+(\.\d+)?') | ForEach-Object { [double]::Parse($_.Value, $inv) })
            for ($i = 0; $i -lt $numbers.Count; $i += 2) {
                if ($numbers[$i] -lt 0 -or $numbers[$i] -gt $box[2] -or $numbers[$i + 1] -lt 0 -or $numbers[$i + 1] -gt $box[3]) {
                    throw "The $theme path should stay inside the viewBox"
                }
                if ($path -eq $linePaths[0] -and ($numbers[$i + 1] -lt $lineWidth / 2 -or $numbers[$i + 1] -gt $box[3] - $lineWidth / 2)) {
                    throw "The $theme line stroke should not be clipped at the top or the bottom"
                }
            }
        }
        if ($areaPaths[0].GetAttribute('d') -notlike ($linePaths[0].GetAttribute('d') + ' L*Z')) {
            throw "The $theme area should follow the line and close down to the baseline"
        }
        $lastLinePoints[$theme] = $linePaths[0].GetAttribute('d')

        # The labels: fixed size, matching viewBox, one text each, no stretch.
        foreach ($kind in 'start', 'end', 'peak') {
            $labelRelative = "docs/media/pulse-label-$kind-$theme.svg"
            if ($readmeText -notlike "*$labelRelative*") { throw "The pulse section should reference $labelRelative" }
            $labelText = Get-Content -LiteralPath (Join-Path $root $labelRelative) -Raw
            $labelXml = [xml]$labelText
            $labelRoot = $labelXml.DocumentElement
            foreach ($attribute in 'width', 'height', 'viewBox') {
                if (-not $labelRoot.HasAttribute($attribute)) { throw "$labelRelative should have a $attribute on its root" }
            }
            if ($labelRoot.GetAttribute('width') -notmatch '^\d+$' -or $labelRoot.GetAttribute('height') -ne '16') { throw "$labelRelative should have a fixed size in pixels, 16 high" }
            if ($labelRoot.GetAttribute('viewBox') -ne ('0 0 {0} {1}' -f $labelRoot.GetAttribute('width'), $labelRoot.GetAttribute('height'))) { throw "$labelRelative viewBox should match its size" }
            if ($labelRoot.HasAttribute('preserveAspectRatio')) { throw "$labelRelative should not stretch" }
            $texts = @($labelXml.SelectNodes('//*[local-name()="text"]'))
            if ($texts.Count -ne 1) { throw "$labelRelative should hold one text" }
            if (@($labelXml.SelectNodes('//*')).Count -ne 2) { throw "$labelRelative should hold nothing but its text" }
            $expectedAnchor = if ($kind -eq 'start') { 'start' } else { 'end' }
            if ($texts[0].GetAttribute('text-anchor') -ne $expectedAnchor) { throw "$labelRelative text should be anchored to the side it is pinned to" }
            if ($texts[0].GetAttribute('font-size') -ne '13' -or $texts[0].GetAttribute('font-family') -notlike "*'Segoe UI'*") { throw "$labelRelative should keep the font stack and size" }
            if ($texts[0].GetAttribute('x') -match '%') { throw "$labelRelative text should not be placed in percentages" }
            # As wide as its text needs: at least 7 pixels a character.
            if ([int]$labelRoot.GetAttribute('width') -lt 7 * $texts[0].InnerText.Length) { throw "$labelRelative should be wide enough for its text" }
            Test-NoUnsafeContent -Name $labelRelative -Xml $labelXml -Text $labelText
            # The README tag keeps the exact size and the side.
            $tag = [regex]::Match($readmeText, ('<img [^>]*src="{0}"[^>]*>' -f [regex]::Escape("docs/media/pulse-label-$kind-light.svg")))
            if (-not $tag.Success) { throw "The README should show the light $kind label in an img tag" }
            if ($tag.Value -notmatch ('width="{0}"' -f $labelRoot.GetAttribute('width')) -or $tag.Value -notmatch ('height="{0}"' -f $labelRoot.GetAttribute('height'))) {
                throw "The README tag of $labelRelative should carry the exact width and height of the picture"
            }
            $expectedAlign = if ($kind -eq 'start') { 'left' } else { 'right' }
            if ($tag.Value -notmatch ('align="{0}"' -f $expectedAlign)) { throw "The README tag of $labelRelative should be pinned with align=$expectedAlign" }
            if ($tag.Value -match '%') { throw "The README tag of $labelRelative should not use a percentage" }
        }
    }
    if ($readmeText -notmatch '(?s)<picture>.*prefers-color-scheme: dark.*prefers-color-scheme: light.*</picture>') {
        throw 'The pulse section should show the pictures through picture elements with a dark and a light source'
    }
    if ($readmeText -notmatch '<img [^>]*src="docs/media/pulse-commits-light.svg"[^>]*width="100%"[^>]*height="40"') {
        throw 'The README curve should fill the column with width="100%" and have height="40"'
    }
    if ($readmeText -match '(?i)<img [^>]*height="88"|\sstyle=|\sclass=') { throw 'The README pulse should use no style, no class and no 88 pixel picture' }
    # The order: table, peak label, curve, start and end labels, a break, the Generated line below.
    $order = 'pulse-label-peak-light', 'pulse-commits-light', 'pulse-label-start-light', 'pulse-label-end-light', '<br clear="all">', '<sub>Generated from'
    $cursor = $readmeText.IndexOf('| First commit')
    foreach ($needle in $order) {
        $cursor = $readmeText.IndexOf($needle, $cursor + 1)
        if ($cursor -lt 0) {
            throw 'The pulse section should hold the table, the peak label, the curve, the start and end labels, a break and the Generated line, in that order'
        }
    }
    # All the seeded commits are from today: the curve rises to its highest
    # point at its right end (the smallest y of the line is the last point).
    $lineNumbers = @([regex]::Matches($lastLinePoints['light'], '-?\d+(\.\d+)?') | ForEach-Object { [double]::Parse($_.Value, $inv) })
    $endY = $lineNumbers[-1]
    $startY = $lineNumbers[1]
    if ($endY -ge $startY) { throw 'Today''s commits should raise the right end of the curve above its start' }

    # The same input draws the same bytes.
    $mediaFiles = @(foreach ($theme in 'light', 'dark') { "pulse-commits-$theme.svg"; foreach ($kind in 'start', 'end', 'peak') { "pulse-label-$kind-$theme.svg" } })
    & $scriptPath -Target $root
    $firstRun = @($mediaFiles | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $root "docs/media/$_")).Hash })
    & $scriptPath -Target $root
    $secondRun = @($mediaFiles | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $root "docs/media/$_")).Hash })
    if (($firstRun -join ',') -cne ($secondRun -join ',')) { throw 'A second run should write byte-identical picture files' }
    Invoke-TestGit checkout -- . | Out-Null

    if (@(Invoke-TestGit ls-files 'docs/media/pulse-*.svg').Count -ne 8) {
        throw 'The pulse commit should carry the two curve files and the six label files'
    }
    $readmePath = Join-Path $root 'README.md'
    $manualEdit = (Get-Content -LiteralPath $readmePath -Raw) -replace '# Deckle test', '# Manual edit'
    [IO.File]::WriteAllText($readmePath, $manualEdit, [Text.UTF8Encoding]::new($false))
    $failureOutput = @(& pwsh -NoProfile -File $scriptPath -Target $root -Commit 2>&1)
    if ($LASTEXITCODE -eq 0) {
        throw 'Expected README commit to reject manual content changes'
    }
    if (($failureOutput -join "`n") -notlike '*changes outside the generated development pulse*') {
        throw 'README commit rejection should explain how to preserve manual changes'
    }

    # The guard compares the committed README with the working one: it must not
    # depend on the console's output encoding. Non-ASCII text outside the pulse,
    # a legacy code page, and only the pulse section changed: the guard passes.
    $accented = "Caf$([char]0xE9) $([char]0x2014) na$([char]0xEF)ve $([char]0x201C)quoted$([char]0x201D)"
    $readmePath = Join-Path $root 'README.md'
    $withText = (Get-Content -LiteralPath $readmePath -Raw -Encoding utf8) -replace '# Manual edit', "# $accented"
    [IO.File]::WriteAllText($readmePath, $withText, [Text.UTF8Encoding]::new($false))
    Invoke-TestGit add README.md | Out-Null
    Invoke-TestGit commit -m 'test: non-ASCII README' | Out-Null
    $sectionOnly = [regex]::Replace((Get-Content -LiteralPath $readmePath -Raw -Encoding utf8), '(?s)(<!-- deckle-stats:start -->).*?(<!-- deckle-stats:end -->)', '$1' + "`nstale pulse`n" + '$2')
    [IO.File]::WriteAllText($readmePath, $sectionOnly, [Text.UTF8Encoding]::new($false))
    $savedEncoding = [Console]::OutputEncoding
    try {
        [Console]::OutputEncoding = [Text.Encoding]::GetEncoding(850)
        $env:DECKLE_TEST_SCRIPT = $scriptPath
        $env:DECKLE_TEST_ROOT = $root
        $legacyOutput = @(& pwsh -NoProfile -Command '[Console]::OutputEncoding = [Text.Encoding]::GetEncoding(850); & $env:DECKLE_TEST_SCRIPT -Target $env:DECKLE_TEST_ROOT -Commit' 2>&1)
        $legacyExit = $LASTEXITCODE
    } finally {
        [Console]::OutputEncoding = $savedEncoding
        Remove-Item Env:DECKLE_TEST_SCRIPT, Env:DECKLE_TEST_ROOT -ErrorAction SilentlyContinue
    }
    if ($legacyExit -ne 0 -or ($legacyOutput -join "`n") -like '*changes outside the generated development pulse*') {
        throw ('The README guard should pass under a non-UTF-8 console encoding when only the pulse changed: ' + ($legacyOutput -join ' ').Substring(0, [math]::Min(600, ($legacyOutput -join ' ').Length)))
    }

    Write-Host 'update-readme-stats.tests.ps1: PASS' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
