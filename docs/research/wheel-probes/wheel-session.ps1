#requires -Version 7
<#
.SYNOPSIS
  Menu to record labelled mouse-wheel sessions next to Deckle's wheel recorder.

.DESCRIPTION
  The script never records wheel events itself: Deckle's recorder does (toggle
  "Record wheel events", Diagnostics page, Telemetry section). This script only
  writes down WHEN each labelled session happened (start / end wall-clock time,
  with offset) plus a few facts in sessions.jsonl. wheel_analysis.py
  --sessions later cuts the recorder's files along those windows.

  It sends no input to Windows: no wheel or mouse injection of any kind. The
  probe entry (P) runs hidpp_probe.py, which is read-only by construction.

.PARAMETER CapturesDir
  Where sessions.jsonl, probe-*.txt and report-*.txt go (default: a sessions
  folder inside the wheel telemetry folder). Measurements stay beside the raw
  captures they label, outside the repository, so running the kit from a
  checkout never leaves data in the working tree.

.PARAMETER TelemetryDir
  Deckle's wheel telemetry folder (default: %LOCALAPPDATA%\Deckle\telemetry\mouse-wheel).
#>
[CmdletBinding()]
param(
    [string]$CapturesDir = (Join-Path $env:LOCALAPPDATA 'Deckle\telemetry\mouse-wheel\sessions'),
    [string]$TelemetryDir = (Join-Path $env:LOCALAPPDATA 'Deckle\telemetry\mouse-wheel')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Accents must survive both what we print and what native tools print back.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$KitDir = $PSScriptRoot
$SessionsFile = Join-Path $CapturesDir 'sessions.jsonl'
# A telemetry file written more recently than this means the recorder is probably on.
$RecentHours = 3

# ---------------------------------------------------------------------------
# Session catalogue
# ---------------------------------------------------------------------------
# Mx       : ask connection (bolt / bluetooth) and Options+ smooth scrolling.
# Precision: expected Precision Scrolling state ('on' / 'off'); $null = ask freely.
# Steps    : the gestures, shown before the session and again while it runs.

$MouseSteps = @(
    "10 crans lents vers le bas (environ un cran par seconde).",
    "Pause de 3 secondes : ne touche plus à la roue.",
    "10 crans lents vers le haut.",
    "Un coup rapide : une impulsion franche de la roue, vers le bas.",
    "Si la roue libre est disponible : un lancer libre (donne un élan, puis laisse la roue tourner seule)."
)
$TouchpadSteps = @(
    "Deux doigts, défilement lent : quelques secondes vers le bas, puis vers le haut.",
    "Deux doigts, défilement rapide : quelques allers-retours francs.",
    "Un lancer : glisse vite puis relâche les doigts, et laisse l'inertie finir sans toucher."
)

$Sessions = [ordered]@{
    S1 = @{
        Title     = "MX, lissage Options+ dans l'état actuel, Precision Scrolling off"
        Mx        = $true
        Precision = 'off'
        Steps     = $MouseSteps
        Extra     = "Ne change PAS le lissage d'Options+ pour cette session : c'est l'état dans lequel il est maintenant."
    }
    S2 = @{
        Title     = "MX, lissage Options+ dans l'autre état, Precision Scrolling off"
        Mx        = $true
        Precision = 'off'
        Steps     = $MouseSteps
        Extra     = "Avant de commencer, bascule le lissage (smooth scrolling) d'Options+ dans l'état opposé à celui de S1, puis indique-le plus bas."
    }
    S3 = @{
        Title     = "MX, Precision Scrolling on"
        Mx        = $true
        Precision = 'on'
        Steps     = $MouseSteps
        Extra     = "Active Precision Scrolling dans Deckle avant de commencer."
    }
    S4 = @{
        Title     = "Touchpad, Precision Scrolling off"
        Mx        = $false
        Precision = 'off'
        Steps     = $TouchpadSteps
        Extra     = $null
    }
    S5 = @{
        Title     = "Touchpad, Precision Scrolling on"
        Mx        = $false
        Precision = 'on'
        Steps     = $TouchpadSteps + @(
            "Ensuite, essaie de reproduire le double défilement (la page avance deux fois pour un seul geste). Si tu y arrives, dis-le dans la remarque finale."
        )
        Extra     = "Active Precision Scrolling dans Deckle avant de commencer."
    }
    S6 = @{
        Title     = "DualSense via Steam Input (config bureau), Precision Scrolling off"
        Mx        = $false
        Precision = 'off'
        Steps     = @(
            "Stick de défilement : petite inclinaison vers le bas pendant 5 secondes.",
            "Puis petite inclinaison vers le haut pendant 5 secondes.",
            "Puis stick à fond vers le bas, quelques secondes.",
            "Puis stick à fond vers le haut, quelques secondes."
        )
        Extra     = "Steam doit être sur la configuration bureau (Desktop) pour la manette."
    }
    S7 = @{
        Title     = "Libre"
        Mx        = $false
        Precision = $null
        Steps     = @("Fais ce que tu veux observer : tu décris la session par une étiquette, puis tu la joues.")
        Extra     = $null
        Free      = $true
    }
}

# ---------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------

function Write-Title([string]$Text) {
    Write-Host ""
    Write-Host $Text -ForegroundColor Cyan
    Write-Host ('-' * $Text.Length) -ForegroundColor DarkCyan
}

function Read-Choice {
    # Loops until the answer is one of $Allowed (case-insensitive); returns it lowercased.
    param([string]$Prompt, [string[]]$Allowed)
    while ($true) {
        $answer = (Read-Host $Prompt).Trim().ToLowerInvariant()
        if ($Allowed -contains $answer) { return $answer }
        Write-Host "  Réponse attendue : $($Allowed -join ' / ')" -ForegroundColor Yellow
    }
}

function Read-YesNo {
    # Accepts o/oui/y/yes and n/non/no; an empty answer takes $Default.
    param([string]$Prompt, [bool]$Default = $true)
    $hint = if ($Default) { 'O/n' } else { 'o/N' }
    while ($true) {
        $answer = (Read-Host "$Prompt ($hint)").Trim().ToLowerInvariant()
        if ($answer -eq '') { return $Default }
        if ($answer -in 'o', 'oui', 'y', 'yes') { return $true }
        if ($answer -in 'n', 'non', 'no') { return $false }
        Write-Host "  Réponds par o ou n." -ForegroundColor Yellow
    }
}

function Get-RecordedSessions {
    # Returns the parsed lines of sessions.jsonl (empty array when the file does not exist).
    if (-not (Test-Path -LiteralPath $SessionsFile)) { return @() }
    $rows = foreach ($line in Get-Content -LiteralPath $SessionsFile -Encoding utf8) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $line | ConvertFrom-Json } catch { }
    }
    return @($rows)
}

function Save-Session {
    # Appends ONE JSON line. Optional fields are omitted rather than written as null.
    param(
        [string]$Label, [string]$Title, [string]$Start, [string]$End,
        [string]$Connection, [string]$OptionsSmooth, [string]$PrecisionScroll, [string]$Note
    )
    $record = [ordered]@{ label = $Label; title = $Title; start = $Start; end = $End }
    if ($Connection) { $record['connection'] = $Connection }
    if ($OptionsSmooth) { $record['optionsSmooth'] = $OptionsSmooth }
    $record['precisionScroll'] = $PrecisionScroll
    $record['note'] = $Note
    New-Item -ItemType Directory -Force -Path $CapturesDir | Out-Null
    $json = $record | ConvertTo-Json -Compress
    Add-Content -LiteralPath $SessionsFile -Value $json -Encoding utf8
}

function Test-RecorderActivity {
    # Heuristic only: the newest telemetry file was written within $RecentHours.
    # Returns $true when it looks fine. Deckle opens a file at the first wheel event after
    # the toggle goes on, so "toggle on but nothing scrolled yet" also lands here.
    $newest = $null
    if (Test-Path -LiteralPath $TelemetryDir) {
        $newest = Get-ChildItem -LiteralPath $TelemetryDir -Filter 'wheel-events-*.jsonl' -File |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
    }
    if ($newest -and ((Get-Date) - $newest.LastWriteTime).TotalHours -lt $RecentHours) {
        Write-Host ("  Enregistreur : OK, dernier fichier écrit à {0:HH:mm:ss} ({1})." -f $newest.LastWriteTime, $newest.Name) -ForegroundColor Green
        return $true
    }
    Write-Host ""
    Write-Host "  ATTENTION : l'enregistrement semble éteint." -ForegroundColor Yellow
    if ($newest) {
        Write-Host ("  Le dernier fichier de molette date de {0:dd/MM HH:mm} ({1})." -f $newest.LastWriteTime, $newest.Name) -ForegroundColor Yellow
    }
    else {
        Write-Host "  Aucun fichier de molette trouvé dans $TelemetryDir." -ForegroundColor Yellow
    }
    Write-Host "  Allume-le : Deckle -> page Diagnostics -> section Telemetry -> « Record wheel events »." -ForegroundColor Yellow
    Write-Host "  (Le fichier ne s'ouvre qu'au premier cran de molette après l'activation : un test rapide suffit à le vérifier.)" -ForegroundColor DarkYellow
    return $false
}

function Find-Python {
    $cmd = Get-Command python -ErrorAction SilentlyContinue
    if (-not $cmd) { $cmd = Get-Command py -ErrorAction SilentlyContinue }
    if (-not $cmd) { throw "Python est introuvable dans le PATH (essaie « python --version »)." }
    return $cmd.Source
}

# ---------------------------------------------------------------------------
# One labelled session
# ---------------------------------------------------------------------------

function Invoke-WheelSession {
    param([string]$Key)
    $def = $Sessions[$Key]
    $title = $def.Title
    $label = $Key

    Write-Title "$Key - $title"

    if ($def.ContainsKey('Free') -and $def.Free) {
        $typed = ''
        while ([string]::IsNullOrWhiteSpace($typed)) {
            $typed = (Read-Host "Étiquette de la session libre (ex. « MX Bluetooth, molette lente »)").Trim()
        }
        $title = $typed
    }

    Write-Host ""
    Write-Host "À faire :" -ForegroundColor White
    $n = 1
    foreach ($step in $def.Steps) {
        Write-Host ("  {0}. {1}" -f $n, $step)
        $n++
    }
    if ($def.Extra) {
        Write-Host ""
        Write-Host "  $($def.Extra)" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Ne touche à AUCUN autre périphérique de pointage pendant la session (souris, trackpad, manette)," -ForegroundColor Yellow
    Write-Host "sauf celui que la session décrit : sinon ses événements se mélangent aux autres." -ForegroundColor Yellow
    Write-Host ""

    # Facts the session title does not fix.
    $connection = $null
    $optionsSmooth = $null
    if ($def.Mx) {
        $connection = Read-Choice "La MX est connectée en bolt ou en bluetooth ? (bolt/bluetooth)" @('bolt', 'bluetooth')
        $optionsSmooth = Read-Choice "Le lissage (smooth scrolling) d'Options+ est actuellement : (on/off)" @('on', 'off')
    }
    elseif ($def.ContainsKey('Free') -and $def.Free) {
        $c = (Read-Host "Connexion, si ça a un sens (bolt / bluetooth, Entrée pour passer)").Trim().ToLowerInvariant()
        if ($c -in 'bolt', 'bluetooth') { $connection = $c }
    }

    if ($def.Precision) {
        $expected = $def.Precision
        $ok = Read-YesNo "Precision Scrolling est bien sur « $expected » dans Deckle ?" $true
        if ($ok) { $precision = $expected }
        else {
            $precision = Read-Choice "Quel est son état réel ? (on/off)" @('on', 'off')
            Write-Host "  Noté : la session sera enregistrée avec Precision Scrolling = $precision (≠ prévu)." -ForegroundColor Yellow
        }
    }
    else {
        $precision = Read-Choice "Precision Scrolling est actuellement : (on/off)" @('on', 'off')
    }

    Write-Host ""
    if (-not (Test-RecorderActivity)) {
        if (-not (Read-YesNo "Continuer quand même ?" $false)) {
            Write-Host "Session annulée, rien n'a été écrit." -ForegroundColor DarkGray
            return
        }
    }

    Write-Host ""
    [void](Read-Host "Entrée pour commencer")
    $start = [DateTimeOffset]::Now.ToString('o')
    Write-Host ("  Go ! (début {0:HH:mm:ss})" -f [DateTimeOffset]::Now) -ForegroundColor Green
    [void](Read-Host "Entrée quand tu as fini")
    $end = [DateTimeOffset]::Now.ToString('o')

    $note = (Read-Host "Une remarque ? (ex. « raté, main sur le trackpad » ; Entrée pour aucune)").Trim()

    Save-Session -Label $label -Title $title -Start $start -End $end `
        -Connection $connection -OptionsSmooth $optionsSmooth -PrecisionScroll $precision -Note $note

    Write-Host ""
    Write-Host "Session $label enregistrée dans $SessionsFile" -ForegroundColor Green
    Write-Host "  (Une session ratée se refait simplement : chaque passage ajoute une ligne, rien n'est écrasé.)" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# Probe and analysis entries
# ---------------------------------------------------------------------------

function Invoke-HidppProbe {
    Write-Title "Sonde HID++ de la souris (lecture seule)"
    $script = Join-Path $KitDir 'hidpp_probe.py'
    if (-not (Test-Path -LiteralPath $script)) {
        Write-Host "hidpp_probe.py est introuvable à côté de ce script." -ForegroundColor Red
        return
    }
    $argLine = (Read-Host "Arguments à passer à la sonde (ex. -v, --enumerate-only ; Entrée pour aucun)").Trim()
    $probeArgs = if ($argLine) { @($argLine -split '\s+') } else { @() }

    New-Item -ItemType Directory -Force -Path $CapturesDir | Out-Null
    $out = Join-Path $CapturesDir ("probe-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $python = Find-Python

    # -I ignores PYTHON* environment variables (so PYTHONIOENCODING would do nothing); -X utf8 is the
    # switch that still works and keeps accents intact when the output is piped.
    # 2>&1 so the file holds what the screen shows; ForEach-Object turns error records into plain text.
    & $python -I -X utf8 $script @probeArgs 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $out
    $code = $LASTEXITCODE
    Write-Host ""
    Write-Host "Sonde terminée (code $code). Sortie enregistrée : $out" -ForegroundColor Green
}

function Invoke-Analysis {
    Write-Title "Analyse des sessions"
    if (-not (Test-Path -LiteralPath $SessionsFile)) {
        Write-Host "Aucune session enregistrée pour l'instant ($SessionsFile n'existe pas)." -ForegroundColor Yellow
        return
    }
    Write-Host "Rappel : l'enregistrement doit être éteint, sinon la fin du dernier fichier n'est pas encore écrite." -ForegroundColor Yellow
    if (-not (Read-YesNo "L'enregistrement est éteint ?" $true)) {
        Write-Host "Éteins-le d'abord (Deckle -> Diagnostics -> Telemetry -> « Record wheel events »), puis relance A." -ForegroundColor Yellow
        return
    }
    $python = Find-Python
    $report = Join-Path $CapturesDir ("report-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $analysis = Join-Path $KitDir 'wheel_analysis.py'

    # The report is written by --out; the console copy is dropped here and only the summary is echoed below.
    & $python -I -X utf8 $analysis --sessions $SessionsFile $TelemetryDir --out $report | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $report)) {
        Write-Host "L'analyse a échoué (code $LASTEXITCODE)." -ForegroundColor Red
        return
    }

    # Echo the cross-session table so the result is visible without opening the file.
    $lines = Get-Content -LiteralPath $report -Encoding utf8
    $from = [array]::IndexOf($lines, '## Cross-session summary')
    if ($from -ge 0) {
        Write-Host ""
        foreach ($l in $lines[$from..($lines.Count - 1)]) {
            if ($l -like '=====*') { break }
            Write-Host $l
        }
    }
    Write-Host "Rapport complet : $report" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Menu
# ---------------------------------------------------------------------------

function Show-Menu {
    $done = @{}
    foreach ($row in Get-RecordedSessions) { $done[[string]$row.label] = 1 + [int]($done[[string]$row.label]) }
    Write-Title "Mesures de la molette - sessions"
    foreach ($key in $Sessions.Keys) {
        $count = if ($done.ContainsKey($key)) { "  [déjà faite $($done[$key]) fois]" } else { '' }
        $text = if ($Sessions[$key].ContainsKey('Free')) { "Libre (tu tapes l'étiquette)" } else { $Sessions[$key].Title }
        Write-Host ("  {0}  {1}{2}" -f $key, $text, $count)
    }
    Write-Host "  P   Sonde HID++ de la souris (lecture seule)"
    Write-Host "  A   Analyser les sessions enregistrées"
    Write-Host "  Q   Quitter"
}

function Start-Menu {
    while ($true) {
        Show-Menu
        $choice = (Read-Host "Ton choix").Trim().ToUpperInvariant()
        if ($choice -match '^[1-7]$') { $choice = "S$choice" }
        switch -Regex ($choice) {
            '^S[1-7]$' { Invoke-WheelSession -Key $choice; break }
            '^P$' { Invoke-HidppProbe; break }
            '^A$' { Invoke-Analysis; break }
            '^Q$' {
                Write-Host ""
                Write-Host "Avant de partir : éteins l'enregistrement (Deckle -> Diagnostics -> Telemetry -> « Record wheel events »)." -ForegroundColor Yellow
                Write-Host "C'est à ce moment que les derniers événements sont écrits dans le fichier ; analyse (entrée A) après." -ForegroundColor Yellow
                return
            }
            default { Write-Host "Choix inconnu." -ForegroundColor Yellow }
        }
    }
}

# Dot-sourcing the script (to test the helpers) must not open the menu.
if ($MyInvocation.InvocationName -ne '.') {
    Start-Menu
}
