---
name: reference-build-onnxruntime-genai-amd-windows-1.0
description: "Recette de build local de Microsoft `onnxruntime-genai` sur Windows AMD avec Visual Studio 2026, MSVC 14.51+, CMake 4.3+, Ninja, DirectML EP. Couvre les écueils observés lors du POC Phi-4 OGA palier 2 du 2026-05-28 : bug `Enter-VsDevShell` sur VS 2026, warnings MSVC 14.51 traités comme erreurs (C4875, STL1011), absence de l'option `Visual Studio 18 2026` dans la whitelist `build.py`. Produit `onnxruntime-genai.dll` + dépendances ORT/DML/D3D12 utilisables en drop-in dans un projet C# qui consomme le NuGet `Microsoft.ML.OnnxRuntimeGenAI.DirectML`."
type: reference
version: 1.0
---

> Source : POC Phi-4 OGA palier 2 du 2026-05-28, machine RX 7900 XT, Windows 11 build 26100, VS 2026 Community 18.5, MSVC 14.51.36231, CMake 4.3.2, Ninja 1.13.2. Recette consolidée à partir d'itérations sur sept builds successifs.

# Identité

Cette fiche décrit comment builder localement `microsoft/onnxruntime-genai` (OGA) sur Windows AMD avec le backend DirectML, depuis un fork local potentiellement patché. Le cas d'usage initial : produire un `onnxruntime-genai.dll` corrigé pour un PoC Deckle (issue upstream #1455 — LoRA jamais activé sur Phi-4-multimodal audio).

Le résultat est un drop-in pour un projet C# qui consomme le NuGet `Microsoft.ML.OnnxRuntimeGenAI.DirectML` : il suffit de remplacer le `onnxruntime-genai.dll` du `bin\<config>\<tfm>\<rid>\` par le build local, ainsi que les natifs ORT/DML/D3D12 qui en dépendent (si l'OGA buildé tire une version ORT plus récente que celle bundlée par le NuGet — observé : ORT 1.25.0-dev exposé par OGA `main`/`v0.13.0` patché, vs ORT 1.23.0 bundlé par NuGet 0.13.0).

# Prérequis machine

Installation par composants Windows, à valider via `Test-Path` avant d'engager.

| Outil | Version observée | Vérification |
|---|---|---|
| Visual Studio 2026 Community | 18.5.11709.299 | `vswhere -latest -property installationVersion` |
| MSVC C++ x64/x86 build tools | 14.51.36231 | `Test-Path "$VsRoot\VC\Tools\MSVC\14.51.36231\include\map"` |
| Windows 11 SDK | 10.0.26100.0 | `Test-Path "C:\Program Files (x86)\Windows Kits\10\lib\10.0.26100.0"` |
| C++ ATL for v144 | requis | inclus avec le workload C++ desktop |
| CMake | 4.3.2 | `cmake --version` |
| Ninja | 1.13.2 | `ninja --version` |
| Python 3.12 | scoop `python312` | `python312 --version` ; requiert `pip install requests` |
| .NET 10 | 10.0.204 | `dotnet --version` (uniquement si on rebuilde aussi le C# binding) |
| Git | 2.54+ | `git --version` |
| AMD graphics driver | 25.x+ pour RX 7900 XT | DirectML supportant tout GPU DX12 |

**Le workload Visual Studio Installer à cocher est « Desktop development with C++ »**. Sans lui, l'install MSVC ne ship que le sous-ensemble OneCore (`lib\onecore\x64\` seul, pas de `lib\x64\` desktop, pas de `include\` MSVC stdlib) — tout build C++ standard plante. Symptôme observé : `LINK : fatal error LNK1104: cannot open file 'MSVCRTD.lib'` au premier try-compile CMake, puis `fatal error C1083: Cannot open include file: 'map'` quand les libs sont contournées. Mémoire `project_deckle_vs2026_msvc_incomplete` consigne ce piège.

# Procédure

## Étape 1 — Clone et checkout du tag aligné

Cloner upstream à un tag dont la version NuGet est utilisée par le projet consommateur. Critique pour préserver la compatibilité ABI du wrapper managé.

```powershell
cd D:\workspace
git clone --depth 1 --branch v0.13.0 https://github.com/microsoft/onnxruntime-genai.git
cd onnxruntime-genai
git checkout -b fix/<your-branch-name>
```

Le tag `v0.13.0` (commit `2d30e49ff403`) est aligné sur le NuGet `Microsoft.ML.OnnxRuntimeGenAI.DirectML 0.13.0`. Vérifier alignement projet consommateur :

```powershell
Select-String -Path 'path\to\project.csproj' -Pattern 'OnnxRuntimeGenAI'
```

## Étape 2 — Patch local éventuel

Appliquer le patch sur la branche locale. Pour le patch LoRA activation Phi-4-mm produit par Deckle, voir [`docs/research/phi4-oga-lora-activation--2026-05-28.patch`](../research/phi4-oga-lora-activation--2026-05-28.patch).

```powershell
git apply path\to\your.patch
git commit -am "your commit message"
```

## Étape 3 — Patch de `build.py` pour autoriser VS 2026

`build.py` à `v0.13.0` whitelist statique des générateurs CMake — `Visual Studio 18 2026` n'y est pas. Ajouter la ligne nécessaire ; ce patch est trivial mais doit être appliqué avant le premier `build.py` :

```python
# build.py, autour de la ligne 102 (argparse définition de --cmake_generator)
parser.add_argument(
    "--cmake_generator",
    choices=[
        "MinGW Makefiles",
        "Ninja",
        "NMake Makefiles",
        "Unix Makefiles",
        "Visual Studio 17 2022",
        "Visual Studio 18 2026",  # <-- ajouter cette ligne
        "Xcode",
    ],
    ...
)
```

En pratique, le générateur effectif retenu pour le build documenté ci-dessous est `Ninja` (plus rapide, indépendant de la détection VS via la registry). L'option VS 2026 reste utile pour les builds qui veulent ouvrir la solution dans l'IDE.

## Étape 4 — Dépendance Python `requests`

`build.py` importe `util.dependency_resolver` qui importe `requests`. Pas listé en `requirements.txt`. À installer manuellement :

```powershell
python312 -m pip install requests
```

## Étape 5 — Script wrapper PowerShell

Le script ci-dessous active l'environnement MSVC via `Microsoft.VisualStudio.DevShell.dll`, ajoute le répertoire de `vswhere.exe` au PATH (que `VsDevCmd.bat` cherche sans préfixe), neutralise les nouveaux warnings/erreurs introduits par MSVC 14.51 via `$env:CL`, et invoque `build.py`. À déposer à la racine du clone OGA :

```powershell
# D:\workspace\onnxruntime-genai\build-deckle.ps1

$ErrorActionPreference = 'Stop'
Set-Location D:\workspace\onnxruntime-genai

$vsRoot = 'D:\bin\visual-studio\visual-studio-2026'

Import-Module "$vsRoot\Common7\Tools\Microsoft.VisualStudio.DevShell.dll"

# vswhere doit être sur le PATH avant Enter-VsDevShell (VsDevCmd.bat shell-out non préfixé).
$env:Path = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer;' + $env:Path

Enter-VsDevShell -VsInstallPath $vsRoot -DevCmdArguments '-arch=x64 -host_arch=x64' -SkipAutomaticLocation | Out-Null

# MSVC 14.51 (VS 2026) — patches CL injectés globalement.
#   /wd4875  — silence C4875 sur le pattern GSL [[gsl::suppress(int_literal)]].
#   /D_SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS — STL1011 sur <experimental/coroutine>.
$env:CL = '/wd4875 /D_SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS'

& python312 build.py --use_dml --config Release --parallel --skip_tests --skip_wheel `
    --build_dir build\Windows --cmake_generator Ninja

exit $LASTEXITCODE
```

Lancement :

```powershell
powershell -ExecutionPolicy Bypass -File .\build-deckle.ps1 *> build-deckle.log
```

Durée observée : ~3-5 min pour CMake configure + download deps (ORT, gsl, dlib, dr_libs, nlohmann_json, onnxruntime_extensions, pybind11, …), ~10-15 min pour la compile complète sur RX 7900 XT machine (152 unités Ninja).

## Étape 6 — Échec attendu sur le sub-build `examples/c/`

Le build complet plante à la phase finale `examples/c/` avec :

```
fatal error C1083: Cannot open include file: 'onnxruntime_cxx_api.h': No such file or directory
```

Les sous-binaires `model_chat.exe`, `model_qa.exe`, `model_mm.exe`, `whisper.exe`, `nemotron_speech.exe` sont des programmes d'exemple qui dépendent d'un layout d'include différent. **Cet échec n'affecte pas la production du DLL principal** — la ligne `[147/152] Linking CXX shared library onnxruntime-genai.dll` est imprimée *avant* l'échec, et le DLL est disponible dans le répertoire de build.

## Étape 7 — Localisation des artefacts produits

Layout du répertoire de build après un build réussi (DLL principal + deps stagées) :

```
D:\workspace\onnxruntime-genai\build\Windows\Release\
├── onnxruntime-genai.dll              ← cible principale, 2.3 MB
├── onnxruntime-genai.lib              ← lib d'import correspondante
└── Release\
    ├── onnxruntime.dll                ← ORT, version ≥ celle requise par OGA build
    ├── onnxruntime_providers_shared.dll
    ├── DirectML.dll                   ← EP DML, version cohérente avec ORT
    └── D3D12Core.dll                  ← runtime DX12, requis par DML
```

## Étape 8 — Drop-in dans le projet C# consommateur

Si le NuGet `Microsoft.ML.OnnxRuntimeGenAI.DirectML` consommé bundle une version ORT *différente* de celle tirée par le build OGA local, le wrapper managé va échouer à l'init avec `DllNotFoundException` et un message ORT type `« The requested API version [N] is not available, only API versions [1, M] are supported »`. Pour aligner l'ABI, recopier **tous** les natifs depuis le build local vers le `bin\<config>\<tfm>\<rid>\` du projet, en conservant les originaux en backup :

```powershell
$srcRel    = "D:\workspace\onnxruntime-genai\build\Windows\Release"
$projBin   = "path\to\project\bin\Debug\net10.0-windows\win-x64"

$natives = @{
    "onnxruntime-genai.dll"                = "$srcRel\onnxruntime-genai.dll"
    "onnxruntime.dll"                       = "$srcRel\Release\onnxruntime.dll"
    "onnxruntime_providers_shared.dll"      = "$srcRel\Release\onnxruntime_providers_shared.dll"
    "DirectML.dll"                          = "$srcRel\Release\DirectML.dll"
    "D3D12Core.dll"                         = "$srcRel\Release\D3D12Core.dll"
}

foreach ($name in $natives.Keys) {
    $dst = Join-Path $projBin $name
    $backup = "$dst.stock-backup"
    if (-not (Test-Path $backup) -and (Test-Path $dst)) {
        Copy-Item $dst $backup
    }
    Copy-Item $natives[$name] $dst -Force
}
```

# Variantes

## Build CUDA (NVIDIA)

Remplacer `--use_dml` par `--use_cuda --cuda_home <path>` dans le script wrapper. Toutes les autres étapes restent valides.

## Build CPU-only

Retirer `--use_dml`. Sortie : un `onnxruntime-genai.dll` lié uniquement à `onnxruntime.dll` CPU. Le drop-in dans un projet C# qui consomme `Microsoft.ML.OnnxRuntimeGenAI` (CPU NuGet, sans suffixe `.DirectML`) suit la même mécanique mais avec moins de natifs à aligner.

## Build C# binding

Ajouter `--build_csharp` au wrapper. Génère aussi le `Microsoft.ML.OnnxRuntimeGenAI.dll` managé. Utile si le patch local modifie la P/Invoke surface — ce n'était pas le cas du patch Phi-4 OGA LoRA activation.

# Notes upstream

- L'incompatibilité `Enter-VsDevShell` ↔ VS 2026 (PATH/LIB non setup correctement) mérite un report Microsoft sur le repo `microsoft/vssetup.powershell` ou directement contre les outils VS Installer.
- Les warnings MSVC 14.51 sur GSL (`C4875` `[[gsl::suppress]] non-string literal`) et `<experimental/coroutine>` (`STL1011`) impacteront tout consommateur GSL ou dlib qui compile en `/WX`. Mérite des PRs upstream sur les libs concernées (deja merged peut-être ailleurs, à vérifier).
- L'absence de `Visual Studio 18 2026` dans la whitelist `build.py` est une PR triviale (3 lignes) à proposer à microsoft/onnxruntime-genai dès qu'un build sur cette version sera supporté officiellement.

# Pointers

- [`docs/research/research--asr-native-windows-amd-routes--2026-05-28.md`](../research/research--asr-native-windows-amd-routes--2026-05-28.md) — fiche research du POC qui a produit cette recette ; section *POC palier 2*.
- [`docs/research/phi4-oga-lora-activation--2026-05-28.patch`](../research/phi4-oga-lora-activation--2026-05-28.patch) — patch consommé en exemple de cette procédure.
- Memory `project_deckle_vs2026_msvc_incomplete` — détail du piège install MSVC OneCore-only et procédure de remédiation.
