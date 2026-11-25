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

    # The chart: two well-formed SVG files, one smooth line and one area under
    # it, no bar, a fixed height, a transparent background, no script, no drive path, no personal data,
    # and the README shows them in a picture element at the column's width.
    $readmeText = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw
    $lastLinePoints = @{}
    foreach ($theme in 'light', 'dark') {
        $relative = "docs/media/pulse-commits-$theme.svg"
        if ($readmeText -notlike "*$relative*") { throw "The pulse section should reference $relative" }
        $svgText = Get-Content -LiteralPath (Join-Path $root $relative) -Raw
        $xml = [xml]$svgText
        $svgRoot = $xml.DocumentElement
        if ($svgRoot.LocalName -ne 'svg') { throw "The $theme chart should be an svg document" }
        # The picture has a fixed height and no viewBox of its own: it fills
        # the column in width only, so its text keeps its size.
        if ($svgRoot.HasAttribute('viewBox')) { throw "The $theme chart should have no viewBox on its root, so that its text does not scale" }
        if ($svgRoot.GetAttribute('width') -ne '100%') { throw "The $theme chart should be 100% wide" }
        if ($svgRoot.GetAttribute('height') -notmatch '^\d+$') { throw "The $theme chart should have a fixed height in pixels" }
        if ($readmeText -notmatch ('<img [^>]*height="{0}"' -f $svgRoot.GetAttribute('height'))) {
            throw 'The README image should repeat the chart height in pixels, because the chart has no ratio of its own'
        }
        # Transparent background: no rect, no background style, no fill on the root.
        if ($svgText -match '(?i)background|<svg[^>]*\sfill=') { throw "The $theme chart should have a transparent background" }
        # Only the curve stretches: one nested svg holds the two paths, with a
        # non-scaling stroke; the text and the marker sit outside it.
        $nested = @($xml.SelectNodes('/*/*[local-name()="g"]/*[local-name()="svg"]'))
        if ($nested.Count -ne 1) { throw "The $theme chart should hold one nested svg for the curve" }
        if ($nested[0].GetAttribute('preserveAspectRatio') -ne 'none') { throw "The $theme curve should stretch freely (preserveAspectRatio none)" }
        $box = @($nested[0].GetAttribute('viewBox') -split '\s+' | ForEach-Object { [double]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) })
        if ($box.Count -ne 4) { throw "The $theme curve should have a viewBox" }
        if ([double]::Parse($nested[0].GetAttribute('height'), [Globalization.CultureInfo]::InvariantCulture) -ne $box[3]) {
            throw "The $theme curve viewBox height should equal its height in pixels"
        }
        if (@($nested[0].SelectNodes('.//*[local-name()="text" or local-name()="circle"]')).Count -gt 0) {
            throw "The $theme chart should keep its text and marker outside the scaled part"
        }
        $outsideText = @($xml.SelectNodes('//*[local-name()="text"]'))
        if ($outsideText.Count -lt 3) { throw "The $theme chart should hold the two date labels and the peak label" }
        foreach ($text in $outsideText) {
            if ($text.GetAttribute('x') -notmatch '%$') { throw "The $theme labels should be placed with percentages" }
        }
        if (@($nested[0].SelectNodes('.//*[local-name()="path" and @fill="none"]'))[0].GetAttribute('vector-effect') -ne 'non-scaling-stroke') {
            throw "The $theme line should keep its thickness (non-scaling-stroke)"
        }
        if (@($xml.SelectNodes('//*[local-name()="rect"]')).Count -gt 0) { throw "The $theme chart should hold no bar" }
        if (@($xml.SelectNodes('//*[local-name()="script" or local-name()="style" or local-name()="image" or local-name()="foreignObject"]')).Count -gt 0) {
            throw "The $theme chart should hold no script, style, image or foreignObject"
        }
        $paths = @($xml.SelectNodes('//*[local-name()="path"]'))
        $linePaths = @($paths | Where-Object { $_.GetAttribute('fill') -eq 'none' })
        $areaPaths = @($paths | Where-Object { $_.GetAttribute('fill') -like 'url(#*' })
        if ($paths.Count -ne 2 -or $linePaths.Count -ne 1 -or $areaPaths.Count -ne 1) {
            throw "The $theme chart should hold one path for the line and one for the area"
        }
        if ($linePaths[0].GetAttribute('stroke-linejoin') -ne 'round') { throw "The $theme line should have rounded joins" }
        if (@($xml.SelectNodes('//*[local-name()="linearGradient"]')).Count -ne 1) {
            throw "The $theme chart should hold one gradient for the area"
        }
        foreach ($path in $linePaths + $areaPaths) {
            $d = $path.GetAttribute('d')
            if ($d -match '(?i)nan|infinity|e[+-]?\d') { throw "The $theme path should hold no NaN or exponent" }
            if ($d -notmatch '^M[\d. ]+( C[\d. ]+)+') { throw "The $theme path should be a smooth Bezier path" }
            $numbers = @([regex]::Matches($d, '-?\d+(\.\d+)?') | ForEach-Object { [double]::Parse($_.Value, [Globalization.CultureInfo]::InvariantCulture) })
            for ($i = 0; $i -lt $numbers.Count; $i += 2) {
                if ($numbers[$i] -lt 0 -or $numbers[$i] -gt $box[2] -or $numbers[$i + 1] -lt 0 -or $numbers[$i + 1] -gt $box[3]) {
                    throw "The $theme path should stay inside the viewBox"
                }
            }
        }
        if ($areaPaths[0].GetAttribute('d') -notlike ($linePaths[0].GetAttribute('d') + ' L*Z')) {
            throw "The $theme area should follow the line and close down to the baseline"
        }
        $scanned = $svgText.Replace('http://www.w3.org/2000/svg', '')
        if ($scanned -match '(?i)\b[a-z]:[\\/]|@|https?:|\bon\w+=') {
            throw "The $theme chart should hold no drive path, address, link or event handler"
        }
        foreach ($name in $env:USERNAME, $env:COMPUTERNAME) {
            if ($name -and $svgText -match [regex]::Escape($name)) { throw "The $theme chart should hold no personal data" }
        }
        $lastLinePoints[$theme] = $linePaths[0].GetAttribute('d')
    }
    if ($readmeText -notmatch '(?s)<picture>.*prefers-color-scheme: dark.*prefers-color-scheme: light.*</picture>') {
        throw 'The pulse section should show the chart through a picture element with a dark and a light source'
    }
    if ($readmeText -notmatch '<img [^>]*width="100%"') {
        throw 'The README image should fill the column with width="100%"'
    }
    # All the seeded commits are from today: the curve rises to its highest
    # point at its right end (the smallest y of the line is the last point).
    $lineNumbers = @([regex]::Matches($lastLinePoints['light'], '-?\d+(\.\d+)?') | ForEach-Object { [double]::Parse($_.Value, [Globalization.CultureInfo]::InvariantCulture) })
    $endY = $lineNumbers[-1]
    $startY = $lineNumbers[1]
    if ($endY -ge $startY) { throw 'Today''s commits should raise the right end of the curve above its start' }

    # The same input draws the same bytes.
    $lightFile = Join-Path $root 'docs/media/pulse-commits-light.svg'
    $darkFile = Join-Path $root 'docs/media/pulse-commits-dark.svg'
    & $scriptPath -Target $root
    $firstRun = @((Get-FileHash -LiteralPath $lightFile).Hash, (Get-FileHash -LiteralPath $darkFile).Hash)
    & $scriptPath -Target $root
    $secondRun = @((Get-FileHash -LiteralPath $lightFile).Hash, (Get-FileHash -LiteralPath $darkFile).Hash)
    if (($firstRun -join ',') -cne ($secondRun -join ',')) { throw 'A second run should write byte-identical chart files' }
    Invoke-TestGit checkout -- . | Out-Null

    if (@(Invoke-TestGit ls-files 'docs/media/pulse-commits-light.svg' 'docs/media/pulse-commits-dark.svg').Count -ne 2) {
        throw 'The pulse commit should carry both chart files'
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
