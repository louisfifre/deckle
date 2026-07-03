# fetch-autocorrect-data.ps1
#
# Downloads the raw lexical sources for the French diacritics autocorrect
# into <OutputRoot>\raw\. This is stage 0 of the autocorrect pipeline: it
# only acquires data, it does not transform it. A later stage builds the
# diacritics dictionary from these files.
#
# OutputRoot lives under artifacts\ (gitignored) — these are large,
# regenerable inputs, never versioned.
#
# Sources:
#   1. Lexique 3.83 (Lexique383.tsv) — French inflected forms with film /
#      book frequencies. CC BY-SA 4.0. The frequency-ranked French ortho
#      column is the backbone of the accented-form dictionary.
#   2. Norvig count_1w.txt — English unigram counts. Used to detect words
#      that are English (leave untouched) vs French (candidates for
#      re-accenting).
#   3. FranceTerme.xml — the official French terminology export. Its English
#      equivalents seed the restricted global-English protected lexicon.
#   4. Wikipedia FR plaintext — a real-prose corpus for evaluating the
#      autocorrect end to end. Split disjointly by article into a train
#      set and a held-out eval set.
#
# Idempotent: a target already present with a plausible size is skipped
# unless -Force. The Wikipedia corpus is rebuilt only when the existing
# train/eval files are below their MB targets.

[CmdletBinding()]
param(
    # Root for the downloaded data. raw\ is created beneath it. Defaults to
    # <repo>\artifacts\autocorrect-data — under the gitignored artifacts\.
    [string]$OutputRoot,

    # Wikipedia FR train-corpus size target, in MB. Articles are appended
    # until the file reaches at least this size.
    [double]$TrainMB = 8,

    # Wikipedia FR eval-corpus size target, in MB. Filled after train, from
    # a disjoint set of articles (no article appears in both).
    [double]$EvalMB = 1.5,

    # Re-download / rebuild even when a target already exists at a plausible
    # size.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
. (Join-Path $ScriptDir 'action-summary.ps1')

# Repo root — two levels up: scripts/lib -> scripts -> repo root. Never
# hardcode the worktree path; this resolves relative to where the script
# physically lives.
$Repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $Repo 'artifacts\autocorrect-data'
}
$RawDir = Join-Path $OutputRoot 'raw'

function Step($msg) { Write-Host "`n[fetch] $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "         $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "         $msg" -ForegroundColor Yellow }
function Info($msg) { Write-Host "         $msg" -ForegroundColor Gray }

$Workflow = 'Fetch autocorrect data'
$lexiqueRows = $null
$norvigRows = $null
$trainCount = $null
$evalCount = $null

trap {
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Autocorrect data fetch failed before completion." `
        -Details ([ordered]@{
            OutputRoot = $OutputRoot
            RawDir     = $RawDir
            Force      = $(if ($Force) { 'Yes' } else { 'No' })
            Error      = $_.Exception.Message
        })
    throw
}

function Get-SizeMB($path) { [math]::Round((Get-Item $path).Length / 1MB, 2) }

# Idempotent download via curl.exe (built into Win10/11, native progress
# bar, fast on large files). $expectedMinBytes is a coarse "present and
# roughly complete" gate — same pattern as setup-assets.ps1.
function Download($url, $dst, $expectedMinBytes) {
    $name = Split-Path $dst -Leaf
    if (-not $Force -and (Test-Path $dst) -and ((Get-Item $dst).Length -ge $expectedMinBytes)) {
        Ok "already present $name ($(Get-SizeMB $dst) MB)"
        return $false
    }
    Info "downloading $name ..."
    & curl.exe -L --fail --retry 3 --progress-bar -o $dst $url
    if ($LASTEXITCODE -ne 0) { throw "curl failed for $url" }
    Ok "downloaded $name ($(Get-SizeMB $dst) MB)"
    return $true
}

# UTF-8 *without BOM* writer. The next pipeline stage and most line-oriented
# tooling choke on a leading BOM, so we never emit one. Set-Content's utf8
# encoding writes a BOM on Windows PowerShell; using the .NET encoding object
# explicitly avoids that across PS versions.
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$swTotal = [System.Diagnostics.Stopwatch]::StartNew()

Step "output root: $OutputRoot"
if (-not (Test-Path $RawDir)) {
    New-Item -ItemType Directory -Path $RawDir -Force | Out-Null
    Ok "created $RawDir"
} else {
    Ok "exists  $RawDir"
}

# =============================================================================
# 1. Lexique 3.83
# =============================================================================
# Primary host is lexique.org over http (the server only speaks http and
# serves the TSV as application/octet-stream). Fallback is the openlexicon
# GitHub mirror, which serves the same dataset over https raw.
Step 'Lexique 3.83 (French inflected forms + frequencies)'
$lexiqueDst = Join-Path $RawDir 'Lexique383.tsv'
$lexiquePrimary  = 'http://www.lexique.org/databases/Lexique383/Lexique383.tsv'
$lexiqueFallback = 'https://raw.githubusercontent.com/chrplr/openlexicon/master/datasets-info/Lexique383/Lexique383.tsv'
# A complete Lexique383.tsv is ~24-26 MB; 20 MB is a safe "looks complete" floor.
$lexiqueMinBytes = 20MB
$lexiqueUrlUsed  = $lexiquePrimary

if ($Force -or -not (Test-Path $lexiqueDst) -or ((Get-Item $lexiqueDst).Length -lt $lexiqueMinBytes)) {
    Info "trying primary $lexiquePrimary"
    & curl.exe -L --fail --retry 2 --progress-bar -o $lexiqueDst $lexiquePrimary
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $lexiqueDst) -or ((Get-Item $lexiqueDst).Length -lt $lexiqueMinBytes)) {
        Warn "primary failed or short — falling back to openlexicon GitHub mirror"
        & curl.exe -L --fail --retry 3 --progress-bar -o $lexiqueDst $lexiqueFallback
        if ($LASTEXITCODE -ne 0) { throw "curl failed for both Lexique sources" }
        $lexiqueUrlUsed = $lexiqueFallback
    }
    Ok "downloaded Lexique383.tsv ($(Get-SizeMB $lexiqueDst) MB) from $lexiqueUrlUsed"
} else {
    Ok "already present Lexique383.tsv ($(Get-SizeMB $lexiqueDst) MB)"
}

# Verify shape: first line must be a TSV header carrying ortho + a freq
# column. Report the header columns and the data row count.
$lexiqueHeader = (Get-Content $lexiqueDst -First 1)
$lexiqueCols   = $lexiqueHeader -split "`t"
if ($lexiqueCols -notcontains 'ortho' -or -not ($lexiqueCols -match 'freq')) {
    Warn "header does not look like Lexique (no 'ortho' / 'freq' column) — got: $lexiqueHeader"
}
# Total lines minus the header. Measure-Object streams the file rather than
# loading it whole.
$lexiqueLines = (Get-Content $lexiqueDst | Measure-Object -Line).Lines
$lexiqueRows  = $lexiqueLines - 1
Info "header columns ($($lexiqueCols.Count)): $($lexiqueCols -join ', ')"
Info "data rows: $lexiqueRows"

# =============================================================================
# 1b. Morphalou 3.1 (French inflected forms — coverage beyond Lexique)
# =============================================================================
# Closes the conjugation / vocabulary gap Lexique leaves (captes, renommes…):
# build-data overlays these forms at an epsilon frequency. The "tout en un"
# CSV lives behind an ORTOLANG content URL (the market page is JS-only; the
# content API serves the zip directly). We keep only the CSV from the archive
# — the bundled HTML conjugation tree is dead weight. LGPL-LR (see NOTICE.md).
Step 'Morphalou 3.1 (French inflected-form coverage)'
$morphalouCsv = Join-Path $RawDir 'Morphalou3.1_CSV.csv'
$morphalouUrl = 'https://repository.ortolang.fr/api/content/morphalou/3/Morphalou3.1_formatCSV_toutEnUn.zip'
# The extracted CSV is ~100 MB; 50 MB is a safe completeness floor.
$morphalouMinBytes = 50MB

if ($Force -or -not (Test-Path $morphalouCsv) -or ((Get-Item $morphalouCsv).Length -lt $morphalouMinBytes)) {
    $morphalouZip = Join-Path $RawDir 'Morphalou3.1_CSV.zip'
    Info "downloading Morphalou3.1 CSV archive (~38 MB) ..."
    & curl.exe -L --fail --retry 3 --progress-bar -o $morphalouZip $morphalouUrl
    if ($LASTEXITCODE -ne 0) { throw "curl failed for Morphalou" }
    Info "extracting Morphalou3.1_CSV.csv only ..."
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipArchive = [System.IO.Compression.ZipFile]::OpenRead($morphalouZip)
    try {
        $entry = $zipArchive.Entries | Where-Object { $_.Name -eq 'Morphalou3.1_CSV.csv' }
        if (-not $entry) { throw "Morphalou3.1_CSV.csv not found in the archive" }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $morphalouCsv, $true)
    } finally {
        $zipArchive.Dispose()
    }
    Remove-Item $morphalouZip -Force
    Ok "extracted Morphalou3.1_CSV.csv ($(Get-SizeMB $morphalouCsv) MB)"
} else {
    Ok "already present Morphalou3.1_CSV.csv ($(Get-SizeMB $morphalouCsv) MB)"
}

# =============================================================================
# 2. Norvig count_1w.txt
# =============================================================================
Step 'Norvig count_1w.txt (English word counts)'
$norvigDst = Join-Path $RawDir 'count_1w.txt'
$norvigUrl = 'https://norvig.com/ngrams/count_1w.txt'
# The file is ~4.7 MB / ~333k lines; 3 MB is a safe completeness floor.
Download $norvigUrl $norvigDst 3MB | Out-Null

# Verify line shape: each line is "word<TAB>count". Sample the head rather
# than parsing all 333k lines.
$norvigSample = Get-Content $norvigDst -First 3
$norvigShapeOk = ($norvigSample | Where-Object { $_ -match '^\S+\t\d+$' }).Count -eq $norvigSample.Count
if (-not $norvigShapeOk) {
    Warn "count_1w.txt rows do not match 'word<TAB>count' — sample: $($norvigSample -join ' / ')"
}
$norvigRows = (Get-Content $norvigDst | Measure-Object -Line).Lines
Info "shape ok: $norvigShapeOk (sample: $($norvigSample[0]))"
Info "rows: $norvigRows"

# =============================================================================
# 2b. FranceTerme (French official terminology — English equivalents)
# =============================================================================
# The restricted globish seed is built from the <Equivalent langue="en"> entries
# of the official FranceTerme export. The canonical XML lives on the
# culture.gouv.fr public export — the remote resource the data.gouv.fr dataset
# points to — served over http as text/xml, ~9 MB, no auth. Licence Ouverte /
# Etalab (see NOTICE.md).
Step 'FranceTerme.xml (French terminology — English equivalents)'
$franceTermeDst = Join-Path $RawDir 'FranceTerme.xml'
$franceTermeUrl = 'http://www.franceterme.culture.gouv.fr/public/FranceTerme.xml'
# The export is ~8.9 MB; 4 MB is a safe "looks complete" floor.
Download $franceTermeUrl $franceTermeDst 4MB | Out-Null

# Verify shape: the payload must be an XML document (the builder reads its
# <Equivalent langue="en"> entries).
$franceTermeHead = (Get-Content $franceTermeDst -First 1)
if ($franceTermeHead -notmatch '<\?xml') {
    Warn "FranceTerme.xml does not begin with an XML declaration — got: $franceTermeHead"
}

# =============================================================================
# 3. Wikipedia FR plaintext corpus (train + eval, disjoint by article)
# =============================================================================
Step 'Wikipedia FR plaintext corpus (train + eval)'
$trainDst = Join-Path $RawDir 'wiki-fr-train.txt'
$evalDst  = Join-Path $RawDir 'wiki-fr-eval.txt'

$trainOk = (Test-Path $trainDst) -and ((Get-Item $trainDst).Length -ge ($TrainMB * 1MB))
$evalOk  = (Test-Path $evalDst)  -and ((Get-Item $evalDst).Length  -ge ($EvalMB  * 1MB))

if (-not $Force -and $trainOk -and $evalOk) {
    Ok "already present wiki-fr-train.txt ($(Get-SizeMB $trainDst) MB) + wiki-fr-eval.txt ($(Get-SizeMB $evalDst) MB)"
} else {
    $UA = 'Deckle-autocorrect-corpus/1.0 (contact: outils@louisfifre.com)'
    $apiBase = 'https://fr.wikipedia.org/w/api.php'
    $headers = @{ 'User-Agent' = $UA }

    # Quality categories, in priority order. "Article de qualité" is the
    # top tier (a few thousand articles); "Bon article" is the second tier,
    # tapped only if the first runs dry before the targets are met.
    $categories = @(
        'Catégorie:Article de qualité',
        'Catégorie:Bon article'
    )

    # Politeness wrapper: descriptive UA, maxlag, one retry on transient
    # failure, throttled to <= 2 req/s by the caller's Start-Sleep. Returns
    # parsed JSON or throws on the second failure.
    function Invoke-WikiApi($query) {
        $url = "$apiBase`?$query&format=json&formatversion=2&maxlag=5"
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            try {
                return Invoke-RestMethod -Uri $url -Headers $headers -TimeoutSec 40
            } catch {
                if ($attempt -eq 2) { throw }
                Warn "transient API failure (attempt $attempt) — retrying: $($_.Exception.Message)"
                Start-Sleep -Seconds 2
            }
        }
    }

    # Enumerate article titles across the quality categories, following the
    # cmcontinue token. Yields a flat de-duplicated title list, lazily — the
    # caller stops requesting more once both corpus targets are met.
    function Get-CategoryTitles {
        $seen = @{}
        foreach ($cat in $categories) {
            $cont = $null
            do {
                $q = "action=query&list=categorymembers&cmtitle=$([uri]::EscapeDataString($cat))&cmlimit=500&cmnamespace=0"
                if ($cont) { $q += "&cmcontinue=$([uri]::EscapeDataString($cont))" }
                $j = Invoke-WikiApi $q
                Start-Sleep -Milliseconds 400
                foreach ($m in $j.query.categorymembers) {
                    if (-not $seen.ContainsKey($m.title)) {
                        $seen[$m.title] = $true
                        $m.title
                    }
                }
                $cont = $j.continue.cmcontinue
            } while ($cont)
        }
    }

    # Fetch one article's plaintext extract. explaintext gives section
    # headings as "== Titre ==" lines — strip those, collapse blank runs.
    function Get-ArticleText($title) {
        $q = "action=query&prop=extracts&explaintext=1&titles=$([uri]::EscapeDataString($title))"
        $j = Invoke-WikiApi $q
        $page = $j.query.pages[0]
        if ($null -eq $page -or $page.missing -or -not $page.extract) { return $null }
        $lines = $page.extract -split "`r?`n" | Where-Object {
            # Drop pure section-heading markers like "== Histoire ==".
            $_ -notmatch '^\s*=+\s.*\s=+\s*$'
        }
        $text = ($lines -join "`n")
        # Collapse runs of 3+ newlines (left by stripped headings) to a
        # single blank line.
        $text = $text -replace "(`n\s*){3,}", "`n`n"
        return $text.Trim()
    }

    Info "enumerating quality-article titles ..."
    $allTitles = @(Get-CategoryTitles)
    Info "candidate titles: $($allTitles.Count)"

    # Build a target. Streams articles into a StreamWriter (UTF-8 no BOM),
    # one article per paragraph block separated by a blank line. Skips any
    # title already in $seenSet so train and eval stay disjoint. Returns the
    # set of titles consumed.
    function Build-Corpus($titles, $dst, $targetBytes, $label, [hashtable]$seenSet) {
        $writer = New-Object System.IO.StreamWriter($dst, $false, $Utf8NoBom)
        $count = 0
        $written = 0
        try {
            foreach ($title in $titles) {
                if ($seenSet.ContainsKey($title)) { continue }
                $seenSet[$title] = $true
                $text = Get-ArticleText $title
                Start-Sleep -Milliseconds 400
                if (-not $text) { continue }
                if ($count -gt 0) { $writer.Write("`n") }  # blank line between articles
                $writer.Write($text)
                $writer.Write("`n")
                $count++
                $written = $writer.BaseStream.Length
                if ($count % 25 -eq 0) {
                    Info "$label : $count articles, $([math]::Round($written / 1MB, 2)) MB"
                }
                if ($written -ge $targetBytes) { break }
            }
        } finally {
            $writer.Flush()
            $writer.Close()
        }
        return $count
    }

    # Shared seen-set guarantees the split is disjoint by article. Train is
    # filled first, eval second from whatever titles remain.
    $seen = @{}
    Info "building train corpus (target $TrainMB MB) ..."
    $trainCount = Build-Corpus $allTitles $trainDst ($TrainMB * 1MB) 'train' $seen
    Ok "train: $trainCount articles, $(Get-SizeMB $trainDst) MB"

    Info "building eval corpus (target $EvalMB MB, disjoint articles) ..."
    $evalCount = Build-Corpus $allTitles $evalDst ($EvalMB * 1MB) 'eval' $seen
    Ok "eval: $evalCount articles, $(Get-SizeMB $evalDst) MB"

    if ((Get-Item $trainDst).Length -lt ($TrainMB * 1MB)) {
        Warn "train below target ($(Get-SizeMB $trainDst) MB < $TrainMB MB) — categories may be exhausted"
    }
    if ((Get-Item $evalDst).Length -lt ($EvalMB * 1MB)) {
        Warn "eval below target ($(Get-SizeMB $evalDst) MB < $EvalMB MB) — categories may be exhausted"
    }
}

# =============================================================================
# Summary
# =============================================================================
$swTotal.Stop()
Step 'done'
foreach ($f in @('Lexique383.tsv', 'Morphalou3.1_CSV.csv', 'count_1w.txt', 'FranceTerme.xml', 'wiki-fr-train.txt', 'wiki-fr-eval.txt')) {
    $p = Join-Path $RawDir $f
    if (Test-Path $p) { Write-Host ("         {0,-20} {1,8} MB" -f $f, (Get-SizeMB $p)) }
    else              { Warn "MISSING $f" }
}
Write-Host "`n         wall time: $([math]::Round($swTotal.Elapsed.TotalMinutes, 1)) min" -ForegroundColor Gray

$missingFiles = @('Lexique383.tsv', 'Morphalou3.1_CSV.csv', 'count_1w.txt', 'FranceTerme.xml', 'wiki-fr-train.txt', 'wiki-fr-eval.txt') |
    Where-Object { -not (Test-Path (Join-Path $RawDir $_)) }
$summaryResult = if ($missingFiles.Count -gt 0) { 'Partial' } else { 'Success' }
$summarySentence = if ($missingFiles.Count -gt 0) {
    "Autocorrect data fetch finished with $($missingFiles.Count) missing file(s)."
} else {
    "Autocorrect data sources were fetched under $RawDir."
}

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result $summaryResult `
    -Sentence $summarySentence `
    -Details ([ordered]@{
        OutputRoot     = $OutputRoot
        RawDir         = $RawDir
        Force          = $(if ($Force) { 'Yes' } else { 'No' })
        'Lexique rows' = $lexiqueRows
        'Norvig rows'  = $norvigRows
        Train          = $(if ($trainCount -ne $null) { "$trainCount article(s)" } else { 'Already present or not rebuilt' })
        Eval           = $(if ($evalCount -ne $null) { "$evalCount article(s)" } else { 'Already present or not rebuilt' })
        Missing        = ($missingFiles -join ', ')
        'Wall time'    = "$([math]::Round($swTotal.Elapsed.TotalMinutes, 1)) min"
    })
