# Download the GGUFs of the Voxtral perf characterization.
#
# curl.exe --continue-at - resumes an interrupted download; a file already
# at its expected size is skipped. One download at a time, so the link is
# not saturated and Hugging Face does not rate-limit.
#
# Every file comes from bartowski, for one quantization scheme across the
# model families.

[CmdletBinding()]
param(
    [string]$DestDir = "$PSScriptRoot\..\models-cache",
    [string]$Curl    = "curl.exe"
)

$ErrorActionPreference = 'Stop'

# Size is the expected byte count, a minimal integrity check after download.
$catalogue = @(
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

    # From here, sizes are the decimal GB the Hugging Face page shows, not
    # exact bytes.

    # --- Voxtral Mini 3B, K_M and K_L ---
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

    # --- Voxtral Small 24B, K_M and K_L ---
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

    # --- Mistral Small 3.1 24B Instruct, the base of Voxtral 24B ---
    # A control that separates the base architecture from Voxtral's extras.
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

    # ±5 % covers the sizes read off the Hugging Face page.
    $sizeOk = {
        param($actual, $expected)
        $actual -gt 0 -and [Math]::Abs($actual - $expected) -lt (0.05 * $expected)
    }

    # A valid GGUF starts with the 4 bytes 'GGUF'. This catches a 404
    # placeholder that curl --continue-at - would otherwise extend into a
    # file of plausible size that llama.cpp fails to load.
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

    # --location follows Hugging Face's redirect to its LFS host.
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
