# fetch-autocorrect-data.ps1
#
# Downloads the raw lexical sources for the French diacritics autocorrect
# into <OutputRoot>\raw\. This is stage 0 of the autocorrect pipeline: it
# only acquires data, it does not transform it. A later stage builds the
# diacritics dictionary from these files.
#
# OutputRoot lives under artifacts\ (gitignored) — these are large,
# regenerable inputs, never versioned.

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

    # Also fetch the kaikki.org wiktionary raw extractions (~3.3 GB gz in
    # total, one per pack language), the source of the domain packs. Opt-in:
    # the default gesture stays light and the dumps are only needed when
    # rebuilding a pack.
    [switch]$IncludeKaikki,

    # Re-download / rebuild even when a target already exists at a plausible
    # size.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path $ScriptDir 'lib'
. (Join-Path $LibDir 'action-summary.ps1')

$Repo = Split-Path -Parent $PSScriptRoot

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $Repo 'artifacts\autocorrect-data'
}
$RawDir = Join-Path $OutputRoot 'raw'

$WorkflowOutput = New-WorkflowOutput -Category 'fetch'

$Workflow = 'Fetch autocorrect data'
$lexiqueRows = $null
$norvigRows = $null
$trainCount = $null
$evalCount = $null

trap {
    Write-ActionSummary `
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

# curl.exe ships with Windows and is fast on large files. $expectedMinBytes is
# a coarse "present and roughly complete" gate.
function Download($url, $dst, $expectedMinBytes) {
    $name = Split-Path $dst -Leaf
    if (-not $Force -and (Test-Path $dst) -and ((Get-Item $dst).Length -ge $expectedMinBytes)) {
        Write-WorkflowMessage -Output $WorkflowOutput -Message "already present $name ($(Get-SizeMB $dst) MB)"
        return $false
    }
    Write-WorkflowMessage -Output $WorkflowOutput -Message "downloading $name ..."
    & curl.exe -L --fail --retry 3 --progress-bar -o $dst $url
    if ($LASTEXITCODE -ne 0) { throw "curl failed for $url" }
    Write-WorkflowMessage -Output $WorkflowOutput -Message "downloaded $name ($(Get-SizeMB $dst) MB)"
    return $true
}

# The next pipeline stage and most line-oriented tooling choke on a leading
# BOM. Set-Content's utf8 writes one on Windows PowerShell; the explicit .NET
# encoding avoids it on every PowerShell version.
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$swTotal = [System.Diagnostics.Stopwatch]::StartNew()

Write-WorkflowStep -Output $WorkflowOutput -Message "output root: $OutputRoot"
if (-not (Test-Path $RawDir)) {
    New-Item -ItemType Directory -Path $RawDir -Force | Out-Null
    Write-WorkflowMessage -Output $WorkflowOutput -Message "created $RawDir"
} else {
    Write-WorkflowMessage -Output $WorkflowOutput -Message "exists  $RawDir"
}

# Lexique 3.83, CC BY-SA 4.0: its frequency-ranked French ortho column is the
# backbone of the accented-form dictionary. Primary host is lexique.org over
# http (the server only speaks http and serves the TSV as
# application/octet-stream). Fallback is the openlexicon GitHub mirror, which
# serves the same dataset over https raw.
Write-WorkflowStep -Output $WorkflowOutput -Message 'Lexique 3.83 (French inflected forms + frequencies)'
$lexiqueDst = Join-Path $RawDir 'Lexique383.tsv'
$lexiquePrimary  = 'http://www.lexique.org/databases/Lexique383/Lexique383.tsv'
$lexiqueFallback = 'https://raw.githubusercontent.com/chrplr/openlexicon/master/datasets-info/Lexique383/Lexique383.tsv'
# A complete Lexique383.tsv is ~24-26 MB; 20 MB is a safe "looks complete" floor.
$lexiqueMinBytes = 20MB
$lexiqueUrlUsed  = $lexiquePrimary

if ($Force -or -not (Test-Path $lexiqueDst) -or ((Get-Item $lexiqueDst).Length -lt $lexiqueMinBytes)) {
    Write-WorkflowMessage -Output $WorkflowOutput -Message "trying primary $lexiquePrimary"
    & curl.exe -L --fail --retry 2 --progress-bar -o $lexiqueDst $lexiquePrimary
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $lexiqueDst) -or ((Get-Item $lexiqueDst).Length -lt $lexiqueMinBytes)) {
        Write-WorkflowMessage -Output $WorkflowOutput -Message "primary failed or short — falling back to openlexicon GitHub mirror" -Role Warning
        & curl.exe -L --fail --retry 3 --progress-bar -o $lexiqueDst $lexiqueFallback
        if ($LASTEXITCODE -ne 0) { throw "curl failed for both Lexique sources" }
        $lexiqueUrlUsed = $lexiqueFallback
    }
    Write-WorkflowMessage -Output $WorkflowOutput -Message "downloaded Lexique383.tsv ($(Get-SizeMB $lexiqueDst) MB) from $lexiqueUrlUsed"
} else {
    Write-WorkflowMessage -Output $WorkflowOutput -Message "already present Lexique383.tsv ($(Get-SizeMB $lexiqueDst) MB)"
}

$lexiqueHeader = (Get-Content $lexiqueDst -First 1)
$lexiqueCols   = $lexiqueHeader -split "`t"
if ($lexiqueCols -notcontains 'ortho' -or -not ($lexiqueCols -match 'freq')) {
    Write-WorkflowMessage -Output $WorkflowOutput -Message "header does not look like Lexique (no 'ortho' / 'freq' column) — got: $lexiqueHeader" -Role Warning
}
# Measure-Object streams the file rather than loading it whole.
$lexiqueLines = (Get-Content $lexiqueDst | Measure-Object -Line).Lines
$lexiqueRows  = $lexiqueLines - 1
Write-WorkflowMessage -Output $WorkflowOutput -Message "header columns ($($lexiqueCols.Count)): $($lexiqueCols -join ', ')"
Write-WorkflowMessage -Output $WorkflowOutput -Message "data rows: $lexiqueRows"

# Morphalou 3.1 closes the conjugation / vocabulary gap Lexique leaves
# (captes, renommes…): build-data overlays these forms at an epsilon frequency.
# The "tout en un" CSV lives behind an ORTOLANG content URL (the market page is
# JS-only; the content API serves the zip directly). Only the CSV is kept from
# the archive — the bundled HTML conjugation tree is dead weight. LGPL-LR (see
# NOTICE.md).
Write-WorkflowStep -Output $WorkflowOutput -Message 'Morphalou 3.1 (French inflected-form coverage)'
$morphalouCsv = Join-Path $RawDir 'Morphalou3.1_CSV.csv'
$morphalouUrl = 'https://repository.ortolang.fr/api/content/morphalou/3/Morphalou3.1_formatCSV_toutEnUn.zip'
# The extracted CSV is ~100 MB; 50 MB is a safe completeness floor.
$morphalouMinBytes = 50MB

if ($Force -or -not (Test-Path $morphalouCsv) -or ((Get-Item $morphalouCsv).Length -lt $morphalouMinBytes)) {
    $morphalouZip = Join-Path $RawDir 'Morphalou3.1_CSV.zip'
    Write-WorkflowMessage -Output $WorkflowOutput -Message "downloading Morphalou3.1 CSV archive (~38 MB) ..."
    & curl.exe -L --fail --retry 3 --progress-bar -o $morphalouZip $morphalouUrl
    if ($LASTEXITCODE -ne 0) { throw "curl failed for Morphalou" }
    Write-WorkflowMessage -Output $WorkflowOutput -Message "extracting Morphalou3.1_CSV.csv only ..."
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
    Write-WorkflowMessage -Output $WorkflowOutput -Message "extracted Morphalou3.1_CSV.csv ($(Get-SizeMB $morphalouCsv) MB)"
} else {
    Write-WorkflowMessage -Output $WorkflowOutput -Message "already present Morphalou3.1_CSV.csv ($(Get-SizeMB $morphalouCsv) MB)"
}

# Norvig's English unigram counts tell English words (left untouched) from
# French ones (re-accenting candidates).
Write-WorkflowStep -Output $WorkflowOutput -Message 'Norvig count_1w.txt (English word counts)'
$norvigDst = Join-Path $RawDir 'count_1w.txt'
$norvigUrl = 'https://norvig.com/ngrams/count_1w.txt'
# The file is ~4.7 MB / ~333k lines; 3 MB is a safe completeness floor.
Download $norvigUrl $norvigDst 3MB | Out-Null

$norvigSample = Get-Content $norvigDst -First 3
$norvigShapeOk = ($norvigSample | Where-Object { $_ -match '^\S+\t\d+$' }).Count -eq $norvigSample.Count
if (-not $norvigShapeOk) {
    Write-WorkflowMessage -Output $WorkflowOutput -Message "count_1w.txt rows do not match 'word<TAB>count' — sample: $($norvigSample -join ' / ')" -Role Warning
}
$norvigRows = (Get-Content $norvigDst | Measure-Object -Line).Lines
Write-WorkflowMessage -Output $WorkflowOutput -Message "shape ok: $norvigShapeOk (sample: $($norvigSample[0]))"
Write-WorkflowMessage -Output $WorkflowOutput -Message "rows: $norvigRows"

# The restricted globish seed is built from the <Equivalent langue="en"> entries
# of the official FranceTerme export. The canonical XML lives on the
# culture.gouv.fr public export — the remote resource the data.gouv.fr dataset
# points to — served over http as text/xml, ~9 MB, no auth. Licence Ouverte /
# Etalab (see NOTICE.md).
Write-WorkflowStep -Output $WorkflowOutput -Message 'FranceTerme.xml (French terminology — English equivalents)'
$franceTermeDst = Join-Path $RawDir 'FranceTerme.xml'
$franceTermeUrl = 'http://www.franceterme.culture.gouv.fr/public/FranceTerme.xml'
# The export is ~8.9 MB; 4 MB is a safe "looks complete" floor.
Download $franceTermeUrl $franceTermeDst 4MB | Out-Null

$franceTermeHead = (Get-Content $franceTermeDst -First 1)
if ($franceTermeHead -notmatch '<\?xml') {
    Write-WorkflowMessage -Output $WorkflowOutput -Message "FranceTerme.xml does not begin with an XML declaration — got: $franceTermeHead" -Role Warning
}

# The kaikki wiktextract JSONL dumps — the surface-form deposits the domain
# packs are fabricated from (entries carry their topical categories and their
# inline inflections). kaikki's topic-subset JSONLs are deprecated, so the pack
# builder streams these raw dumps. One entry per pack language:
# the URLs are not templatable, kaikki serves the English hub under /dictionary/
# and every other language under its own /<lang>wiktionary/ path. Dual-licensed
# CC BY-SA / GFDL (see NOTICE.md).
$KaikkiDumps = @(
    @{ Name = 'raw-wiktextract-frwiktionary.jsonl.gz'
       Url  = 'https://kaikki.org/frwiktionary/raw-wiktextract-data.jsonl.gz'
       # ~676 MB compressed; 600 MB is a safe completeness floor.
       MinBytes = 600MB
       Label = 'French Wiktionary (~676 MB)' }
    @{ Name = 'raw-wiktextract-enwiktionary.jsonl.gz'
       Url  = 'https://kaikki.org/dictionary/raw-wiktextract-data.jsonl.gz'
       # ~2.64 GB compressed; 2.4 GB is a safe completeness floor.
       MinBytes = 2400MB
       Label = 'English Wiktionary (~2.64 GB)' }
)

if ($IncludeKaikki) {
    foreach ($dump in $KaikkiDumps) {
        Write-WorkflowStep -Output $WorkflowOutput -Message "Kaikki raw extraction — $($dump.Label)"
        Download $dump.Url (Join-Path $RawDir $dump.Name) $dump.MinBytes | Out-Null
    }
}

# Wikipedia FR plaintext is a real-prose corpus for evaluating the
# autocorrect end to end.
Write-WorkflowStep -Output $WorkflowOutput -Message 'Wikipedia FR plaintext corpus (train + eval)'
$trainDst = Join-Path $RawDir 'wiki-fr-train.txt'
$evalDst  = Join-Path $RawDir 'wiki-fr-eval.txt'

$trainOk = (Test-Path $trainDst) -and ((Get-Item $trainDst).Length -ge ($TrainMB * 1MB))
$evalOk  = (Test-Path $evalDst)  -and ((Get-Item $evalDst).Length  -ge ($EvalMB  * 1MB))

if (-not $Force -and $trainOk -and $evalOk) {
    Write-WorkflowMessage -Output $WorkflowOutput -Message "already present wiki-fr-train.txt ($(Get-SizeMB $trainDst) MB) + wiki-fr-eval.txt ($(Get-SizeMB $evalDst) MB)"
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

    # Wikimedia API etiquette: descriptive UA, maxlag, one retry on transient
    # failure, and the callers sleep between requests.
    function Invoke-WikiApi($query) {
        $url = "$apiBase`?$query&format=json&formatversion=2&maxlag=5"
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            try {
                return Invoke-RestMethod -Uri $url -Headers $headers -TimeoutSec 40
            } catch {
                if ($attempt -eq 2) { throw }
                Write-WorkflowMessage -Output $WorkflowOutput -Message "transient API failure (attempt $attempt) — retrying: $($_.Exception.Message)" -Role Warning
                Start-Sleep -Seconds 2
            }
        }
    }

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

    # explaintext renders section headings as "== Titre ==" lines.
    function Get-ArticleText($title) {
        $q = "action=query&prop=extracts&explaintext=1&titles=$([uri]::EscapeDataString($title))"
        $j = Invoke-WikiApi $q
        $page = $j.query.pages[0]
        if ($null -eq $page -or $page.missing -or -not $page.extract) { return $null }
        $lines = $page.extract -split "`r?`n" | Where-Object {
            $_ -notmatch '^\s*=+\s.*\s=+\s*$'
        }
        $text = ($lines -join "`n")
        # Stripped headings leave runs of blank lines; collapse them to one.
        $text = $text -replace "(`n\s*){3,}", "`n`n"
        return $text.Trim()
    }

    Write-WorkflowMessage -Output $WorkflowOutput -Message "enumerating quality-article titles ..."
    $allTitles = @(Get-CategoryTitles)
    Write-WorkflowMessage -Output $WorkflowOutput -Message "candidate titles: $($allTitles.Count)"

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
                    Write-WorkflowMessage -Output $WorkflowOutput -Message "$label : $count articles, $([math]::Round($written / 1MB, 2)) MB"
                }
                if ($written -ge $targetBytes) { break }
            }
        } finally {
            $writer.Flush()
            $writer.Close()
        }
        return $count
    }

    # The shared seen-set keeps train and the held-out eval disjoint by
    # article. Train is filled first, eval second from whatever titles remain.
    $seen = @{}
    Write-WorkflowMessage -Output $WorkflowOutput -Message "building train corpus (target $TrainMB MB) ..."
    $trainCount = Build-Corpus $allTitles $trainDst ($TrainMB * 1MB) 'train' $seen
    Write-WorkflowMessage -Output $WorkflowOutput -Message "train: $trainCount articles, $(Get-SizeMB $trainDst) MB"

    Write-WorkflowMessage -Output $WorkflowOutput -Message "building eval corpus (target $EvalMB MB, disjoint articles) ..."
    $evalCount = Build-Corpus $allTitles $evalDst ($EvalMB * 1MB) 'eval' $seen
    Write-WorkflowMessage -Output $WorkflowOutput -Message "eval: $evalCount articles, $(Get-SizeMB $evalDst) MB"

    if ((Get-Item $trainDst).Length -lt ($TrainMB * 1MB)) {
        Write-WorkflowMessage -Output $WorkflowOutput -Message "train below target ($(Get-SizeMB $trainDst) MB < $TrainMB MB) — categories may be exhausted" -Role Warning
    }
    if ((Get-Item $evalDst).Length -lt ($EvalMB * 1MB)) {
        Write-WorkflowMessage -Output $WorkflowOutput -Message "eval below target ($(Get-SizeMB $evalDst) MB < $EvalMB MB) — categories may be exhausted" -Role Warning
    }
}

$swTotal.Stop()
Write-WorkflowStep -Output $WorkflowOutput -Message 'done'
$expectedFiles = @('Lexique383.tsv', 'Morphalou3.1_CSV.csv', 'count_1w.txt', 'FranceTerme.xml', 'wiki-fr-train.txt', 'wiki-fr-eval.txt')
if ($IncludeKaikki) { $expectedFiles += $KaikkiDumps.Name }
foreach ($f in $expectedFiles) {
    $p = Join-Path $RawDir $f
    if (Test-Path $p) { Write-Host ("         {0,-20} {1,8} MB" -f $f, (Get-SizeMB $p)) }
    else              { Write-WorkflowMessage -Output $WorkflowOutput -Message "MISSING $f" -Role Warning }
}
Write-OutputLine -Segments @()
Write-WorkflowMessage -Output $WorkflowOutput -Message "wall time: $([math]::Round($swTotal.Elapsed.TotalMinutes, 1)) min" -Role Muted

$missingFiles = $expectedFiles |
    Where-Object { -not (Test-Path (Join-Path $RawDir $_)) }
$summaryResult = if ($missingFiles.Count -gt 0) { 'Partial' } else { 'Success' }
$summarySentence = if ($missingFiles.Count -gt 0) {
    "Autocorrect data fetch finished with $($missingFiles.Count) missing file(s)."
} else {
    "Autocorrect data sources were fetched under $RawDir."
}

Write-ActionSummary `
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
