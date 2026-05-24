# setup-voxtral-env.ps1 — Reproduit l'environnement du POC d'évaluation
# Voxtral à partir d'une machine vide. Idempotent : safe à relancer.
#
# Stack après pivot stack du 2026-05-24 (voir ADR-0011) :
#   - llama.cpp + backend Vulkan + GGUF Voxtral via llama-mtmd-cli (GPU AMD)
#   - venv Python 3.12 minimal pour le judge Ollama et les helpers audio
#
# Prérequis matériel et système :
#   - Windows 11
#   - GPU compatible Vulkan (cible : AMD Radeon RX 7000 series ou compatible)
#   - Python 3.12 installé (scoop install python312)
#   - GCC MinGW (scoop install mingw)
#   - CMake 4.x (scoop install cmake)
#   - Ninja (scoop install ninja)
#   - Vulkan SDK avec variable VULKAN_SDK définie (scoop install vulkan)
#   - git
#
# Référence : ADR-0011 (POC évaluation Voxtral), section "Pivot stack".

$ErrorActionPreference = "Stop"

$BENCH_DIR     = $PSScriptRoot
$VENV_PATH     = Join-Path $BENCH_DIR ".venv-voxtral"
$EXTERNAL_DIR  = Join-Path $BENCH_DIR "external"
$LLAMA_CPP_DIR = Join-Path $EXTERNAL_DIR "llama.cpp"
$MODELS_DIR    = Join-Path $BENCH_DIR "models-cache" "voxtral"

# GGUF Voxtral — bartowski quants. Q4_K_M = bon compromis qualité/taille
# pour 3B sur GPU 20 GB. Le mmproj contient le tokenizer audio Mistral.
$VOXTRAL_REPO = "bartowski/mistralai_Voxtral-Mini-3B-2507-GGUF"
$VOXTRAL_GGUF = "mistralai_Voxtral-Mini-3B-2507-Q4_K_M.gguf"
$VOXTRAL_MMPROJ = "mmproj-mistralai_Voxtral-Mini-3B-2507-bf16.gguf"

# ── 1. Vérifications prérequis ────────────────────────────────────────────
Write-Host "→ Vérification des prérequis…" -ForegroundColor Cyan

function Require-Cmd($name, $hint) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if (-not $cmd) {
        Write-Error "❌ '$name' absent du PATH. $hint"
    }
    Write-Host "  ✓ $name @ $($cmd.Source)" -ForegroundColor Green
}

Require-Cmd "python312" "Installer via 'scoop install python312'"
Require-Cmd "git"       "Installer via 'scoop install git'"
Require-Cmd "cmake"     "Installer via 'scoop install cmake'"
Require-Cmd "ninja"     "Installer via 'scoop install ninja'"
Require-Cmd "gcc"       "Installer via 'scoop install mingw'"

if (-not $env:VULKAN_SDK -or -not (Test-Path $env:VULKAN_SDK)) {
    Write-Error "❌ VULKAN_SDK absent ou invalide ($env:VULKAN_SDK). Installer Vulkan SDK via 'scoop install vulkan' puis redémarrer la session."
}
Write-Host "  ✓ VULKAN_SDK @ $env:VULKAN_SDK" -ForegroundColor Green

# ── 2. Clone + build llama.cpp avec backend Vulkan ─────────────────────────
if (-not (Test-Path $EXTERNAL_DIR)) {
    New-Item -ItemType Directory -Path $EXTERNAL_DIR | Out-Null
}

if (Test-Path $LLAMA_CPP_DIR) {
    Write-Host "→ llama.cpp déjà cloné à $LLAMA_CPP_DIR — pull seulement" -ForegroundColor Yellow
    git -C $LLAMA_CPP_DIR pull --ff-only
} else {
    Write-Host "→ Clone llama.cpp dans $LLAMA_CPP_DIR" -ForegroundColor Cyan
    git clone --depth 1 https://github.com/ggml-org/llama.cpp $LLAMA_CPP_DIR
}

$BUILD_DIR = Join-Path $LLAMA_CPP_DIR "build"
$MTMD_CLI  = Join-Path $BUILD_DIR "bin\Release\llama-mtmd-cli.exe"

if (Test-Path $MTMD_CLI) {
    Write-Host "→ llama-mtmd-cli déjà build à $MTMD_CLI — skip build" -ForegroundColor Yellow
} else {
    Write-Host "→ Configuration CMake (Vulkan ON)…" -ForegroundColor Cyan
    cmake -B $BUILD_DIR -S $LLAMA_CPP_DIR -G Ninja `
        -DCMAKE_BUILD_TYPE=Release `
        -DGGML_VULKAN=ON `
        -DLLAMA_CURL=OFF

    Write-Host "→ Build llama.cpp (peut prendre ~10–20 min)…" -ForegroundColor Cyan
    cmake --build $BUILD_DIR --config Release --target llama-mtmd-cli

    # Le binaire peut sortir directement dans bin/ avec Ninja, pas bin/Release/
    $alt = Join-Path $BUILD_DIR "bin\llama-mtmd-cli.exe"
    if (-not (Test-Path $MTMD_CLI) -and (Test-Path $alt)) {
        $MTMD_CLI = $alt
    }
    if (-not (Test-Path $MTMD_CLI)) {
        Write-Error "❌ Build terminé mais llama-mtmd-cli.exe introuvable sous $BUILD_DIR\bin\"
    }
}
Write-Host "  ✓ llama-mtmd-cli @ $MTMD_CLI" -ForegroundColor Green

# ── 3. Téléchargement GGUF Voxtral + mmproj depuis HuggingFace ────────────
if (-not (Test-Path $MODELS_DIR)) {
    New-Item -ItemType Directory -Path $MODELS_DIR -Force | Out-Null
}

$gguf_path   = Join-Path $MODELS_DIR $VOXTRAL_GGUF
$mmproj_path = Join-Path $MODELS_DIR $VOXTRAL_MMPROJ

if (-not (Test-Path $gguf_path) -or -not (Test-Path $mmproj_path)) {
    Write-Host "→ Création/réutilisation du venv pour télécharger via huggingface_hub…" -ForegroundColor Cyan
    if (-not (Test-Path $VENV_PATH)) {
        & python312 -m venv $VENV_PATH
    }
    $venv_py  = Join-Path $VENV_PATH "Scripts\python.exe"
    $venv_pip = Join-Path $VENV_PATH "Scripts\pip.exe"
    & $venv_py -m pip install --upgrade --quiet pip wheel setuptools
    & $venv_pip install --quiet huggingface_hub

    Write-Host "→ Téléchargement $VOXTRAL_GGUF + $VOXTRAL_MMPROJ depuis $VOXTRAL_REPO" -ForegroundColor Cyan
    Write-Host "  (acceptation de la license requise sur https://huggingface.co/mistralai/Voxtral-Mini-3B-2507)"
    & $venv_py -c @"
from huggingface_hub import hf_hub_download
for fname in (r'$VOXTRAL_GGUF', r'$VOXTRAL_MMPROJ'):
    print('  →', fname)
    hf_hub_download(
        repo_id=r'$VOXTRAL_REPO',
        filename=fname,
        local_dir=r'$MODELS_DIR',
    )
"@
} else {
    Write-Host "→ GGUF + mmproj déjà présents sous $MODELS_DIR — skip download" -ForegroundColor Yellow
}
Write-Host "  ✓ GGUF      @ $gguf_path" -ForegroundColor Green
Write-Host "  ✓ mmproj    @ $mmproj_path" -ForegroundColor Green

# ── 4. Venv Python minimal — judge Ollama, helpers audio ───────────────────
if (-not (Test-Path $VENV_PATH)) {
    Write-Host "→ Création du venv sous $VENV_PATH" -ForegroundColor Cyan
    & python312 -m venv $VENV_PATH
}
$venv_pip = Join-Path $VENV_PATH "Scripts\pip.exe"
$venv_py  = Join-Path $VENV_PATH "Scripts\python.exe"

Write-Host "→ Upgrade pip / wheel / setuptools" -ForegroundColor Cyan
& $venv_py -m pip install --upgrade --quiet pip wheel setuptools

Write-Host "→ Installation des dépendances minimales (ollama, librosa, soundfile, jiwer, huggingface_hub)" -ForegroundColor Cyan
& $venv_pip install --quiet `
    "numpy" `
    "soundfile" `
    "librosa" `
    "jiwer" `
    "ollama" `
    "huggingface_hub"

# ── 5. Smoke test rapide llama-mtmd-cli ────────────────────────────────────
Write-Host "→ Smoke test llama-mtmd-cli --help" -ForegroundColor Cyan
$null = & $MTMD_CLI --help 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Warning "  ⚠ llama-mtmd-cli --help a renvoyé code $LASTEXITCODE (peut-être normal selon la version)"
} else {
    Write-Host "  ✓ llama-mtmd-cli répond" -ForegroundColor Green
}

# ── 6. Export config pour le bench ─────────────────────────────────────────
$CONFIG_DIR = Join-Path $BENCH_DIR "config"
$env_config = Join-Path $CONFIG_DIR "voxtral_paths.toml"
if (-not (Test-Path $CONFIG_DIR)) {
    New-Item -ItemType Directory -Path $CONFIG_DIR -Force | Out-Null
}
$config_content = @"
# Chemins runtime du POC Voxtral — généré par setup-voxtral-env.ps1.
# Régénérable, ne pas éditer à la main.

[paths]
llama_mtmd_cli = '$MTMD_CLI'
voxtral_gguf   = '$gguf_path'
voxtral_mmproj = '$mmproj_path'
"@
$config_content | Set-Content -Path $env_config -Encoding utf8
Write-Host "  ✓ Config chemins écrite sous $env_config" -ForegroundColor Green

Write-Host ""
Write-Host "✓ Environnement prêt." -ForegroundColor Green
Write-Host ""
Write-Host "Prochaines étapes :"
Write-Host "  1. Login HuggingFace si pas déjà fait :"
Write-Host "     & $VENV_PATH\Scripts\hf.exe auth login"
Write-Host "     + accepter license : https://huggingface.co/mistralai/Voxtral-Mini-3B-2507"
Write-Host "  2. Smoke test :"
Write-Host "     & $venv_py $BENCH_DIR\_voxtral_smoke.py"
Write-Host "  3. Bench Phase 1 :"
Write-Host "     & $venv_py $BENCH_DIR\voxtral_bench.py"
