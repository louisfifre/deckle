# setup-voxtral-env.ps1 — Reproduit l'environnement Python du POC d'évaluation
# Voxtral à partir d'une machine vide. Idempotent : safe à relancer.
#
# Prérequis matériel et système :
#   - Windows 11
#   - GPU AMD Radeon RX 7000 series ou compatible ROCm 7.2.1 (Radeon driver
#     26.2.2 minimum). Sur autre GPU ou sans GPU, torch retombera sur CPU
#     mais ce script échouera à l'install des wheels rocm_* — éditer le
#     bloc ROCm pour utiliser torch CPU à la place.
#   - Python 3.12 installé (impératif — les wheels ROCm Windows officielles
#     d'AMD sont cp312-cp312, pas de wheel cp313+). Recommandé via Scoop :
#         scoop install python312
#     Vérifiable via :
#         python312 --version
#
# Référence : ADR-0011 (POC évaluation Voxtral), section "Conséquences".

$ErrorActionPreference = "Stop"

$VENV_PATH = Join-Path $PSScriptRoot ".venv-voxtral"
$ROCM_BASE = "https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1"

# ── 1. Vérification Python 3.12 ────────────────────────────────────────────
$python312 = Get-Command python312 -ErrorAction SilentlyContinue
if (-not $python312) {
    Write-Error "Python 3.12 absent du PATH. Installer via 'scoop install python312' puis relancer."
}
$pyver = & python312 --version
Write-Host "→ $pyver détecté à $($python312.Source)" -ForegroundColor Cyan

# ── 2. Création du venv ────────────────────────────────────────────────────
if (Test-Path $VENV_PATH) {
    Write-Host "→ Venv déjà présent à $VENV_PATH — skip création" -ForegroundColor Yellow
} else {
    Write-Host "→ Création du venv sous $VENV_PATH" -ForegroundColor Cyan
    & python312 -m venv $VENV_PATH
}

$venv_py = Join-Path $VENV_PATH "Scripts\python.exe"
$venv_pip = Join-Path $VENV_PATH "Scripts\pip.exe"

# ── 3. Upgrade pip/wheel/setuptools ────────────────────────────────────────
Write-Host "→ Upgrade pip / wheel / setuptools" -ForegroundColor Cyan
& $venv_py -m pip install --upgrade pip wheel setuptools

# ── 4. ROCm SDK (3 wheels Radeon, ~1.4 GB) ─────────────────────────────────
Write-Host "→ Installation du SDK ROCm 7.2.1 (3 wheels, ~1.4 GB)" -ForegroundColor Cyan
& $venv_pip install --no-cache-dir `
    "$ROCM_BASE/rocm_sdk_core-7.2.1-py3-none-win_amd64.whl" `
    "$ROCM_BASE/rocm_sdk_devel-7.2.1-py3-none-win_amd64.whl" `
    "$ROCM_BASE/rocm_sdk_libraries_custom-7.2.1-py3-none-win_amd64.whl"

# ── 5. Meta-package ROCm ───────────────────────────────────────────────────
Write-Host "→ Installation du meta-package rocm-7.2.1" -ForegroundColor Cyan
& $venv_pip install --no-cache-dir "$ROCM_BASE/rocm-7.2.1.tar.gz"

# ── 6. PyTorch + torchaudio (wheels ROCm officielles) ──────────────────────
Write-Host "→ Installation de PyTorch 2.9.1+rocm7.2.1 (cp312, win_amd64)" -ForegroundColor Cyan
& $venv_pip install --no-cache-dir `
    "$ROCM_BASE/torch-2.9.1%2Brocm7.2.1-cp312-cp312-win_amd64.whl" `
    "$ROCM_BASE/torchaudio-2.9.1%2Brocm7.2.1-cp312-cp312-win_amd64.whl"

# ── 7. Stack HuggingFace + Voxtral + audio + scoring ───────────────────────
Write-Host "→ Installation de transformers, mistral-common[audio], etc." -ForegroundColor Cyan
& $venv_pip install --no-cache-dir `
    "numpy" `
    "transformers>=5.2.0" `
    "accelerate" `
    "mistral-common[audio]" `
    "soundfile" `
    "librosa" `
    "jiwer" `
    "ollama" `
    "huggingface_hub"

# ── 8. Vérification torch ROCm + GPU ───────────────────────────────────────
Write-Host "→ Smoke test torch ROCm" -ForegroundColor Cyan
& $venv_py -c @"
import torch
print('torch:', torch.__version__)
print('cuda_available:', torch.cuda.is_available())
if torch.cuda.is_available():
    print('device_count:', torch.cuda.device_count())
    print('device_name:', torch.cuda.get_device_name(0))
    print('hip_version:', getattr(torch.version, 'hip', 'N/A'))
else:
    print('WARNING: GPU non détecté — Voxtral tournera en CPU only')
"@

Write-Host ""
Write-Host "✓ Environnement prêt." -ForegroundColor Green
Write-Host "  Activer manuellement via : . $VENV_PATH\Scripts\Activate.ps1"
Write-Host ""
Write-Host "Prochaine étape : login HuggingFace si pas déjà fait"
Write-Host "  & $VENV_PATH\Scripts\hf.exe auth login"
Write-Host "  (token requis sur https://huggingface.co/settings/tokens"
Write-Host "  + acceptation de la license sur https://huggingface.co/mistralai/Voxtral-Mini-3B-2507)"
