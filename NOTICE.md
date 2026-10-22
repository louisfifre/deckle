# Third-party notices

Deckle depends on the following third-party components and data sources. This file records their provenance and applicable licenses; the linked upstream projects carry the authoritative license texts.

## Native dependencies

### whisper.cpp

- Repository : https://github.com/ggerganov/whisper.cpp
- License : MIT. Read from the bundle notes that `scripts/commands/release/publish-native-runtime.ps1` writes, which point to the upstream `LICENSE`.
- Used as : pre-built native DLLs (`libwhisper.dll`, `ggml.dll`, `ggml-base.dll`, `ggml-cpu.dll`, `ggml-vulkan.dll`) loaded at runtime.

### MinGW-w64 runtime

- Project : https://www.mingw-w64.org/
- License : GPL-3.0 with the GCC Runtime Library Exception for `libgcc_s_seh-1.dll` and `libstdc++-6.dll`; MIT for `libwinpthread-1.dll`. The runtime exception permits redistributing these DLLs as a dynamically linked runtime. Read from the bundle notes that `scripts/commands/release/publish-native-runtime.ps1` writes into each native runtime release.
- Used as : runtime DLLs (`libgcc_s_seh-1.dll`, `libstdc++-6.dll`, `libwinpthread-1.dll`) shipped alongside the whisper.cpp build that was produced with the GCC toolchain.

### Vulkan loader

- Project : https://www.lunarg.com/vulkan-sdk/
- License : Apache 2.0 (loader and tools).
- Used at runtime via the system-installed Vulkan ICD when GPU acceleration is requested by whisper.cpp.

## Referenced implementations

### RawInput.Touchpad

- Repository : https://github.com/emoacht/RawInput.Touchpad
- License : MIT — Copyright (c) emoacht.
- Used as : reference implementation for the Precision Touchpad HID parsing in `Deckle.Input` (preparsed-data approach, value-caps traversal, interop signatures). Reimplemented and extended (tip switch/confidence via the button path, hybrid-mode reassembly, per-device caching); no source file is vendored.

### ThreeFingerDragOnWindows

- Repository : https://github.com/ClementGre/ThreeFingerDragOnWindows
- License : MIT — Copyright (c) Clément Grennerat.
- Used as : studied reference for the three-finger drag mechanics (relative SendInput injection, fractional delta accumulation). Deckle's recognizer is an independent design; no source file is vendored.

## Reserved: GPLv3 code planned for the taskbar module

Not yet included. The taskbar module is planned to port GPLv3 code from the projects below. No file from them is in the repository today; when one lands, its entry here gains the copyright line and the path of the ported files.

- TranslucentTB, the ExplorerTAP component — https://github.com/TranslucentTB/TranslucentTB — GPL-3.0, as recorded (version 2026.2) in `docs/research/taskbar-briefs-2026-10-18/taskbar-research-2026-10-17.md`.
- Windhawk, the engine and the taskbar mods — https://github.com/ramensoftware/windhawk and https://github.com/ramensoftware/windhawk-mods — by Michael Maltsev (m417z) and the mod authors — GPL-3.0, as recorded in `docs/research/taskbar-briefs-2026-10-18/taskbar-research-2026-10-17.md`.

## .NET / NuGet packages

The following third-party packages are referenced across the projects under `src/`. Versions are pinned centrally in `Directory.Packages.props`. Each package ships under its own license. Every license below was read from the package's `.nuspec` (its `license` element, or its `licenseUrl` when no element is present) and, where the nuspec names a license file, from that file, in the local NuGet cache at the pinned version. The package's own files carry the authoritative text.

Microsoft runtime components under Microsoft license terms. They are not open source; Deckle's GPLv3 section 7 additional permission in `LICENSE` covers combining Deckle with them.

- `Microsoft.WindowsAppSDK` 2.5.1 — Microsoft Software License Terms, Microsoft Windows App SDK — Microsoft. Read from the nuspec license file `license.txt`. The packages it pulls in (`Microsoft.WindowsAppSDK.Base`, `.Foundation`, `.InteractiveExperiences`, `.WinUI`, `.DWrite`, `.Widgets`, `.AI`, `.Search` 2.5.5, `.Runtime`) carry the same `license.txt`.
- `Microsoft.WindowsAppSDK.ML` 2.1.94 — Microsoft Software License Terms, Microsoft Windows App SDK and Windows Machine Learning (ML) — Microsoft. Read from its own nuspec license file `license.txt`, which differs from the one above.
- `Microsoft.Windows.AI.MachineLearning` 2.1.74 — Microsoft Software License Terms, Microsoft Windows ML Runtime — Microsoft. Read from the nuspec license file `license.txt`; the package also ships a `ThirdPartyNotices.txt`. Pulled in by `Microsoft.WindowsAppSDK.ML`. Only its managed projection ships with Deckle: the build strips its native files (ONNX Runtime, DirectML, the Windows ML runtime) so that the one ONNX Runtime in the process is the pinned `Microsoft.ML.OnnxRuntime.DirectML`.
- `Microsoft.Windows.SDK.BuildTools` 10.0.26100.4654 — Microsoft Windows SDK license terms — Microsoft. Read from the nuspec `licenseUrl`, `https://aka.ms/WinSDKLicenseURL`. Its companion `Microsoft.Windows.SDK.BuildTools.MSIX` names the license file `sdk_license.txt`, titled Microsoft Software License Terms, Microsoft Windows Software Development Kit (SDK) for Windows 10.
- `Microsoft.Graphics.Win2D` 1.4.0 — Microsoft license terms for the Win2D package — Microsoft. Read from the nuspec `licenseUrl`, `http://www.microsoft.com/web/webpi/eula/eula_win2d_10012014.htm`; the package carries no license file. Draws the HUD rewriting stroke on a Direct2D-backed composition surface.
- `Microsoft.AI.DirectML` 1.15.4 — Microsoft Software License Terms, Microsoft DirectX Machine Learning (DirectML) — Microsoft. Read from the nuspec license file `LICENSE.txt`. Not referenced directly: `Microsoft.ML.OnnxRuntime.DirectML` depends on it, and it ships `DirectML.dll`, a Microsoft redistributable.

Open-source packages.

- `CommunityToolkit.Mvvm` 8.4.2 — MIT — .NET Foundation and Contributors. Read from the nuspec license expression.
- `CommunityToolkit.WinUI.Controls.SettingsControls` 8.2.250402 — MIT — .NET Foundation and Contributors. Read from the nuspec license expression.
- `CommunityToolkit.WinUI.Controls.Sizers` 8.2.250402 — MIT — .NET Foundation and Contributors. Read from the nuspec license expression. Provides the GridSplitter of the Ambient page's preview pane.
- `Microsoft.ML.OnnxRuntime.DirectML` 1.23.0 — MIT — Microsoft. Read from the nuspec license file `LICENSE`. Runs the external Silero VAD on the CPU execution provider of this build.
- `Microsoft.ML.OnnxRuntimeGenAI.DirectML` 0.13.0 — MIT — Microsoft. Read from the nuspec license file `LICENSE`. Runs the local closed-candidate sentence judge through ONNX Runtime GenAI, using DirectML or CPU as selected.
- `Microsoft.ML.Tokenizers` 2.0.0 — MIT — Microsoft. Read from the nuspec license expression. Tokenizes text for the CamemBERT masked-language-model probe.
- `System.Security.Cryptography.ProtectedData` 6.0.0 — MIT — Microsoft. Read from the nuspec license expression. Protects local autocorrect observation data with Windows DPAPI.
- `System.Management` 10.0.12 — MIT — Microsoft. Read from the nuspec license expression. Watches process starts through WMI in `Deckle.Orchestration`.
- `BouncyCastle.Cryptography` 2.6.2 — MIT — Legion of the Bouncy Castle Inc. Read from the nuspec license expression. Provides the DTLS-PSK transport used for Philips Hue Entertainment.
- `Anthropic` 12.57.0 — MIT — Anthropic. Read from the nuspec license expression. The official C# SDK through which `Deckle.Llm`'s Claude engine calls the Messages API.
- `ModelContextProtocol` 2.1.0 and `ModelContextProtocol.AspNetCore` 2.1.0 — Apache-2.0 — Model Context Protocol a Series of LF Projects, LLC. Read from the nuspec license expressions; `ModelContextProtocol.Core`, which they pull in, has the same expression. Serve the MCP HTTP endpoint of `Deckle.Anytype.Mcp`.
- `JsonSchema.Net` 9.4.0 — Open Source Maintenance Fee EULA — Greg Dennis. Read from the nuspec license file `OSMFEULA.txt`, which states the source is MIT. Builds the JSON Schema contract of the MCP surface in `Deckle.Anytype.Mcp`. `Json.More.Net` 3.0.1 and `JsonPointer.Net` 7.0.2, which it pulls in, name the same file.

Other third-party packages pulled in transitively by `Deckle.App`, read from its restored `project.assets.json` and from each nuspec:

- `CommunityToolkit.Common` 8.2.1, `CommunityToolkit.WinUI.Extensions`, `.Helpers` and `.Triggers` 8.2.250402 — MIT — nuspec license expressions.
- `Microsoft.ML.OnnxRuntime.Managed` 1.23.0 and `Microsoft.ML.OnnxRuntimeGenAI.Managed` 0.13.0 — MIT — nuspec license files.
- `Microsoft.Web.WebView2` 1.0.3719.77 — BSD-style license, Copyright (C) Microsoft Corporation — nuspec license file `LICENSE.txt`. Pulled in by `Microsoft.WindowsAppSDK.WinUI`.
- `Google.Protobuf` 3.30.2 — BSD-3-Clause — Google Inc. — nuspec license expression. Pulled in by `Microsoft.ML.Tokenizers`.
- `Humanizer.Core` 3.0.10 — MIT — Claire Novotny, Mehdi Khalili — nuspec license expression. Pulled in by `JsonPointer.Net`.

## Bundled assets

### Bitcount Single

- Project : https://github.com/petrvanblokland/TYPETR-Bitcount
- License : SIL Open Font License 1.1 — Copyright 2024 The Bitcount Project Authors.
- Used as : the bundled `BitcountSingle.ttf` display font.

## Lexical data

The autocorrect module (`Deckle.Autocorrect`) ships compact derived lexicons under `src/Deckle.Autocorrect/Data/`, regenerated by the maintainer's build-data gesture from these sources. The licenses apply to the data files, not to Deckle's code.

### Lexique 3.83

- Project : http://www.lexique.org — New, Pallier, Brysbaert et al.
- License : CC BY-SA 4.0 (the data).
- Used as : source of French inflected forms and frequencies, reduced to the derived `lexicon-fr.tsv.gz` (form + occurrences per million). The derived file remains under CC BY-SA 4.0 with this attribution.
- Also used as : source of `verbs-fr.tsv.gz` (form, lemma, inflection codes, verb-only flag), derived from Lexique's verb morphology by the same build-data gesture. The derived file stays under CC BY-SA 4.0.

### Morphalou 3.1

- Project : https://www.ortolang.fr/market/lexicons/morphalou — ATILF (CNRS, UMR 7118).
- License : LGPL-LR (Lesser General Public License For Linguistic Resources) — the data.
- Used as : source of French inflected forms (full verb / noun / adjective paradigms) that Lexique omits. `build-data` takes only the forms absent from Lexique and overlays them at a negligible frequency into the derived French lexicon, closing the conjugation / vocabulary coverage gap. No frequency data is used (Morphalou carries none). The derived forms stay swappable by design — they live in the regeneratable `lexicon-fr.tsv.gz`.
- Attribution : « Analyse et traitement informatique de la langue française
  - UMR 7118 (ATILF) (2019). Morphalou [Lexique]. ORTOLANG. »

### Norvig ngrams (`count_1w.txt`)

- Source : https://norvig.com/ngrams/ — Peter Norvig, derived from the Google Web Trillion Word Corpus (Thorsten Brants, Alex Franz, LDC).
- License : free to use per the source page.
- Used as : legacy full English word counts, reduced to the derived `lexicon-en.tsv.gz`. This file is retained as a derived lab artifact; the live protected-English guard does not load it.

### FranceTerme

- Source : https://www.data.gouv.fr/datasets/base-franceterme-termes-scientifiques-et-techniques-1 via the official XML resource `FranceTerme.xml`.
- License : Licence Ouverte / Open Licence (Etalab), attribution required.
- Used as : source of English foreign equivalents (`Equivalent langue="en"`), reduced to the restricted protected-English seed `lexicon-en-globish.tsv.gz`. The derived artifact keeps conservative ASCII content tokens only and filters French exact/accent-fold/one-edit collisions.
- Source snapshot used for this artifact : downloaded 2026-07-02.

### Kaikki / wiktextract — French Wiktionary extraction

- Source : https://kaikki.org/frwiktionary/ — Tatu Ylonen, machine-readable extraction of https://fr.wiktionary.org produced by [wiktextract](https://github.com/tatuylonen/wiktextract).
- License : dual CC BY-SA 4.0 / GFDL (the Wiktionary text and data).
- Used as : source of topical surface forms (entries carrying « Lexique en français de X » categories, with their inline inflections), reduced to the derived domain-pack artifacts `pack-fr-*.tsv.gz` (form + flat floor frequency). The derived files remain under CC BY-SA with this attribution; each pack's fabrication report under `src/Deckle.Autocorrect.Lab/PackReports/` records what was kept and refused.

### Kaikki / wiktextract — English Wiktionary extraction

- Source : https://kaikki.org/dictionary/ — Tatu Ylonen, machine-readable extraction of https://en.wiktionary.org produced by [wiktextract](https://github.com/tatuylonen/wiktextract). A distinct dump from the French one above: kaikki serves the English Wiktionary from its `/dictionary/` hub, every other language from `/<lang>wiktionary/`.
- License : dual CC BY-SA 4.0 / GFDL (the Wiktionary text and data).
- Used as : source of topical surface forms (entries carrying the `en:Computing`, `en:Internet`, `en:Software` and `en:Programming` categories, with their inline inflections), reduced to the derived domain-pack artifacts `pack-en-*.tsv.gz` (form + flat floor frequency). The derived files remain under CC BY-SA with this attribution; each pack's fabrication report under `src/Deckle.Autocorrect.Lab/PackReports/` records what was kept and refused.
- Source snapshot used for this artifact : downloaded 2026-07-29.

### wordfreq

- Source : https://github.com/rspeer/wordfreq — Robyn Speer (data frozen 2021).
- License : code MIT; frequency data CC BY-SA 4.0 (aggregated from multiple corpora, see upstream attribution).
- Used as : source of French frequencies for the domain-pack promotion overlay (`pack-fr-*.frequencies.tsv` under `src/Deckle.Autocorrect.Lab/PackReports/`) — genuinely common pack forms ship above the flat floor with a wordfreq-derived occurrences-per-million value. The derived overlay and the pack artifacts it feeds remain under CC BY-SA with this attribution.

### Wikipédia in French

- Source : https://fr.wikipedia.org — plain-text article extracts.
- License : CC BY-SA 4.0 (the text).
- Used as : training and evaluation corpus for the contextual pair model; only the derived aggregate `pair-bigrams-fr.tsv.gz` (counts, no article text) is versioned, under CC BY-SA 4.0 with this attribution.

## Models

Whisper model weights downloaded from [ggerganov/whisper.cpp on Hugging Face](https://huggingface.co/ggerganov/whisper.cpp) are distributed under the original Whisper license (MIT). They are not redistributed in this repository.

### Silero VAD

- Repository : https://github.com/snakers4/silero-vad
- License : MIT — Copyright (c) 2020-present Silero Team.
- Used as : the `silero_vad.onnx` v6.2 model, downloaded on demand to the models directory and run by `Deckle.Inference.Onnx` for external voice activity detection. Not redistributed in this repository.

### CamemBERT (ONNX)

- Repository : https://huggingface.co/Xenova/camembert-base — ONNX export of [almanach/camembert-base](https://huggingface.co/almanach/camembert-base) (Inria — Martin, Müller, Suárez et al.).
- License : MIT — the CamemBERT weights.
- Used as : the `camembert-base` masked-LM (`model.onnx` + `tokenizer.json` + `sentencepiece.bpe.model`), downloaded on demand to the models directory and run by `Deckle.Autocorrect.Mlm` for the post-sentence reranker. Not redistributed in this repository.

### Qwen3 (ONNX Runtime GenAI)

- Repository : https://huggingface.co/Qwen/Qwen3-1.7B — Qwen Team.
- License : Apache 2.0.
- Used as : an optional locally built ONNX Runtime GenAI export for the autocorrect closed-candidate sentence judge. Model weights are not redistributed in this repository.
