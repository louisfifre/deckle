# Télécharge les GGUF de la caractérisation perf Voxtral (session 2026-05-26).
#
# Approche : curl.exe avec --continue-at - pour reprendre un download
# interrompu. Skip si le fichier local existe et fait la taille attendue.
# Séquentiel — un seul download à la fois pour ne pas saturer le lien ni
# déclencher de rate-limit HuggingFace.
#
# Sources : toutes chez bartowski, sélectionné pour la cohérence de la
# scheme de quantization à travers les trois familles de modèles.
# Identification : agent web 2026-05-26.

[CmdletBinding()]
param(
    [string]$DestDir = "$PSScriptRoot\..\models-cache",
    [string]$Curl    = "curl.exe"
)

$ErrorActionPreference = 'Stop'

# Catalogue des 9 GGUF à télécharger. Chaque entrée porte sa taille
# attendue en octets — sert de check d'intégrité minimal après download.
$catalogue = @(
    # Session 2026-05-25/26 : variants déjà téléchargés (skip si présents).
    [pscustomobject]@{
        Slug = 'voxtral-3b-q8_0'
        File = 'Voxtral-Mini-3B-2507-Q8_0.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Mini-3B-2507-GGUF/resolve/main/mistralai_Voxtral-Mini-3B-2507-Q8_0.gguf'
        Size = 4273980736
    }
    [pscustomobject]@{
        Slug = 'voxtral-3b-bf16'
        File = 'Voxtral-Mini-3B-2507-bf16.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Mini-3B-2507-GGUF/resolve/main/mistralai_Voxtral-Mini-3B-2507-bf16.gguf'
        Size = 8037057568
    }
    [pscustomobject]@{
        Slug = 'voxtral-3b-q6_k'
        File = 'Voxtral-Mini-3B-2507-Q6_K.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Mini-3B-2507-GGUF/resolve/main/mistralai_Voxtral-Mini-3B-2507-Q6_K.gguf'
        Size = 3301852480
    }
    [pscustomobject]@{
        Slug = 'voxtral-3b-q3_k_m'
        File = 'Voxtral-Mini-3B-2507-Q3_K_M.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Mini-3B-2507-GGUF/resolve/main/mistralai_Voxtral-Mini-3B-2507-Q3_K_M.gguf'
        Size = 2058061120
    }
    [pscustomobject]@{
        Slug = 'voxtral-3b-q2_k'
        File = 'Voxtral-Mini-3B-2507-Q2_K.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Mini-3B-2507-GGUF/resolve/main/mistralai_Voxtral-Mini-3B-2507-Q2_K.gguf'
        Size = 1661551936
    }
    [pscustomobject]@{
        Slug = 'voxtral-24b-q6_k'
        File = 'Voxtral-Small-24B-2507-Q6_K.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Small-24B-2507-GGUF/resolve/main/mistralai_Voxtral-Small-24B-2507-Q6_K.gguf'
        Size = 19346471872
    }
    [pscustomobject]@{
        Slug = 'voxtral-24b-q3_k_m'
        File = 'Voxtral-Small-24B-2507-Q3_K_M.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Small-24B-2507-GGUF/resolve/main/mistralai_Voxtral-Small-24B-2507-Q3_K_M.gguf'
        Size = 11474615232
    }

    # Session 2026-05-27 (overnight bench) : K_M et K_L variants.
    # Tailles bartowski (GB décimaux affichés sur HF). La tolérance de 5 %
    # dans le check de taille compense l'absence de HEAD requests précis.

    # --- Voxtral Mini 3B, K_M et K_L (Q4_K_M déjà présent) ---
    [pscustomobject]@{
        Slug = 'voxtral-3b-q3_k_l'
        File = 'Voxtral-Mini-3B-2507-Q3_K_L.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Mini-3B-2507-GGUF/resolve/main/mistralai_Voxtral-Mini-3B-2507-Q3_K_L.gguf'
        Size = 2210000000
    }
    [pscustomobject]@{
        Slug = 'voxtral-3b-q4_k_l'
        File = 'Voxtral-Mini-3B-2507-Q4_K_L.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Mini-3B-2507-GGUF/resolve/main/mistralai_Voxtral-Mini-3B-2507-Q4_K_L.gguf'
        Size = 2770000000
    }
    [pscustomobject]@{
        Slug = 'voxtral-3b-q5_k_m'
        File = 'Voxtral-Mini-3B-2507-Q5_K_M.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Mini-3B-2507-GGUF/resolve/main/mistralai_Voxtral-Mini-3B-2507-Q5_K_M.gguf'
        Size = 2870000000
    }
    [pscustomobject]@{
        Slug = 'voxtral-3b-q5_k_l'
        File = 'Voxtral-Mini-3B-2507-Q5_K_L.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Mini-3B-2507-GGUF/resolve/main/mistralai_Voxtral-Mini-3B-2507-Q5_K_L.gguf'
        Size = 3120000000
    }

    # --- Voxtral Small 24B, K_M et K_L (Q3_K_M déjà présent) ---
    [pscustomobject]@{
        Slug = 'voxtral-24b-q3_k_l'
        File = 'Voxtral-Small-24B-2507-Q3_K_L.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Small-24B-2507-GGUF/resolve/main/mistralai_Voxtral-Small-24B-2507-Q3_K_L.gguf'
        Size = 12400000000
    }
    [pscustomobject]@{
        Slug = 'voxtral-24b-q4_k_m'
        File = 'Voxtral-Small-24B-2507-Q4_K_M.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Small-24B-2507-GGUF/resolve/main/mistralai_Voxtral-Small-24B-2507-Q4_K_M.gguf'
        Size = 14330000000
    }
    [pscustomobject]@{
        Slug = 'voxtral-24b-q4_k_l'
        File = 'Voxtral-Small-24B-2507-Q4_K_L.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Small-24B-2507-GGUF/resolve/main/mistralai_Voxtral-Small-24B-2507-Q4_K_L.gguf'
        Size = 14830000000
    }
    [pscustomobject]@{
        Slug = 'voxtral-24b-q5_k_m'
        File = 'Voxtral-Small-24B-2507-Q5_K_M.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Small-24B-2507-GGUF/resolve/main/mistralai_Voxtral-Small-24B-2507-Q5_K_M.gguf'
        Size = 16760000000
    }
    [pscustomobject]@{
        Slug = 'voxtral-24b-q5_k_l'
        File = 'Voxtral-Small-24B-2507-Q5_K_L.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Voxtral-Small-24B-2507-GGUF/resolve/main/mistralai_Voxtral-Small-24B-2507-Q5_K_L.gguf'
        Size = 17180000000
    }

    # --- Mistral Small 3.1 24B Instruct (base archi de Voxtral 24B) ---
    # Contrôle archi : Mistral Small isole l'archi du modèle des extras Voxtral.
    [pscustomobject]@{
        Slug = 'mistral-small-24b-q4_k_m'
        File = 'Mistral-Small-3.1-24B-Instruct-2503-Q4_K_M.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Mistral-Small-3.1-24B-Instruct-2503-GGUF/resolve/main/mistralai_Mistral-Small-3.1-24B-Instruct-2503-Q4_K_M.gguf'
        Size = 14330000000
    }
    [pscustomobject]@{
        Slug = 'mistral-small-24b-q5_k_m'
        File = 'Mistral-Small-3.1-24B-Instruct-2503-Q5_K_M.gguf'
        Url  = 'https://huggingface.co/bartowski/mistralai_Mistral-Small-3.1-24B-Instruct-2503-GGUF/resolve/main/mistralai_Mistral-Small-3.1-24B-Instruct-2503-Q5_K_M.gguf'
        Size = 16760000000
    }
)

if (-not (Test-Path $DestDir)) {
    New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
}
$DestDir = (Resolve-Path $DestDir).Path

$totalBytes = ($catalogue | Measure-Object Size -Sum).Sum
Write-Host ("Cible : {0} fichiers, ~{1:N1} GB cumulé" -f $catalogue.Count, ($totalBytes / 1GB))
Write-Host "Dest  : $DestDir"
Write-Host ""

$skipped = 0
$downloaded = 0
$failed = @()

foreach ($entry in $catalogue) {
    $dest = Join-Path $DestDir $entry.File
    $expected = $entry.Size

    # Tailles attendues : pour les entrées de la session 2026-05-25/26, HEAD
    # X-Linked-Size précises à l'octet. Pour les ajouts overnight 2026-05-27,
    # approximation depuis la page HF en GB décimaux. Tolérance ±5 % couvre
    # les deux cas. Le check magic 'GGUF' attrape les faux positifs (404
    # HTML, fichier vide).
    $sizeOk = {
        param($actual, $expected)
        $actual -gt 0 -and [Math]::Abs($actual - $expected) -lt (0.05 * $expected)
    }

    # Magic check : un GGUF valide commence par les 4 octets 'GGUF'.
    # Un placeholder HTTP 404 ("Entry not found") ou un fichier corrompu
    # par un curl --continue-at - qui aurait appendu à un placeholder se
    # détecte ici. Sans ce check, le resume reprenait sur des bytes
    # garbage et donnait un fichier qui semble taille correcte mais qui
    # plante au load llama.cpp.
    $hasValidMagic = {
        param($path)
        if (-not (Test-Path $path)) { return $false }
        $len = (Get-Item $path).Length
        if ($len -lt 4) { return $false }
        $fs = [System.IO.File]::OpenRead($path)
        try {
            $buf = New-Object byte[] 4
            $fs.Read($buf, 0, 4) | Out-Null
        } finally {
            $fs.Close()
        }
        $magic = -join ($buf | ForEach-Object { [char]$_ })
        return ($magic -eq 'GGUF')
    }

    if (Test-Path $dest) {
        if (-not (& $hasValidMagic $dest)) {
            Write-Host ("[wipe] {0} : magic invalide ou fichier vide, repart de zéro" -f $entry.File)
            Remove-Item $dest -Force
        } else {
            $actual = (Get-Item $dest).Length
            if (& $sizeOk $actual $expected) {
                Write-Host ("[skip] {0} ({1:N2} GB déjà présent)" -f $entry.File, ($actual / 1GB))
                $skipped++
                continue
            }
            Write-Host ("[resume] {0} : {1:N2} GB / {2:N2} GB attendu" -f `
                $entry.File, ($actual / 1GB), ($expected / 1GB))
        }
    }

    if (-not (Test-Path $dest)) {
        Write-Host ("[start] {0} ({1:N2} GB)" -f $entry.File, ($expected / 1GB))
    }

    # curl --continue-at - reprend là où le fichier local s'arrête.
    # --location suit les redirects HF (lfs.huggingface.co).
    # --silent désactive la barre de progression bavarde, --show-error
    # garde les erreurs visibles, --progress-bar donne une barre simple.
    & $Curl `
        --location `
        --continue-at - `
        --output $dest `
        --progress-bar `
        --retry 3 `
        --retry-delay 5 `
        $entry.Url

    if ($LASTEXITCODE -ne 0) {
        Write-Warning ("curl exit code {0} pour {1}" -f $LASTEXITCODE, $entry.File)
        $failed += $entry.Slug
        continue
    }

    $actual = (Get-Item $dest).Length
    if (-not (& $sizeOk $actual $expected)) {
        Write-Warning ("Taille incohérente pour {0} : {1:N0} octets vs {2:N0} attendus (~{3:N2} GB vs ~{4:N2} GB)" -f `
            $entry.File, $actual, $expected, ($actual / 1GB), ($expected / 1GB))
        $failed += $entry.Slug
        continue
    }

    Write-Host ("[done] {0} ({1:N2} GB)" -f $entry.File, ($actual / 1GB))
    $downloaded++
}

Write-Host ""
Write-Host ("Résultat : {0} téléchargés, {1} déjà présents, {2} échoués" -f `
    $downloaded, $skipped, $failed.Count)

if ($failed.Count -gt 0) {
    Write-Host "Échecs :"
    $failed | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

exit 0
