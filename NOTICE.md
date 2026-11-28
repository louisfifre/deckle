# Third-party notices

Deckle depends on the following third-party components and data sources. This file records their provenance and applicable licenses; the linked upstream projects carry the authoritative license texts.

## Native dependencies

### PortAudio

- Repository : https://github.com/PortAudio/portaudio
- License : MIT-style, Copyright (c) 1999-2006 Ross Bencina and Phil Burk. Read from the upstream `LICENSE.txt`, which the build script copies beside the DLL as `portaudio.LICENSE.txt` and which ships with it.
- Used as : `portaudio.dll`, the microphone capture's native backend, called through `src/Deckle.Audio/Internal/PortAudioInterop.cs`. Built from a pinned upstream commit, with the WASAPI host API only and the C runtime linked statically, by `scripts/setup/build-portaudio.ps1`, and committed in `src/Deckle.Audio/Native/win-x64` with its provenance (the `README.md` there gives the commit, the CMake options and the SHA256). The ASIO host API is not compiled in: the ASIO SDK's license is a pending product decision.

### onnx-asr (mel front end of the Parakeet backend)

- Repository : https://github.com/istupakov/onnx-asr
- License : MIT, Copyright (c) 2025 Ilya Stupakov. The license text ships beside the file, as `parakeet-onnx-asr.LICENSE.txt`.
- Used as : `nemo128.onnx`, the mel-spectrogram graph bundled in onnx-asr 0.12.0, copied unchanged from the package (138,824 bytes, SHA-256 `5B4A84C52EEAA615DC46D781CC7E4598F9B432184831D57493C48ADCD01371A9`) into `src/Deckle.Transcription.Parakeet/MelFrontEnd` and run as the Parakeet backend's front end, so that its features match the reference implementation the backend is measured against.

### adblock-rust (Deckle Browser's ad blocker)

- Repository : https://github.com/brave/adblock-rust
- License : MPL-2.0, Copyright Brave Software. Not modified: it is the `adblock` crate 0.13.6, a dependency of the small Rust library `native/Deckle.Browser.Adblock`, which exposes it through a C ABI and is built into `deckle_adblock.dll` by `src/Deckle.Browser.Adblock`.
- Used as : the engine that parses filter lists and decides which requests a page makes are blocked and which elements it hides. The other Rust crates it pulls in are listed with their versions in `native/Deckle.Browser.Adblock/Cargo.lock`; their licenses are those of each crate.

### Filter lists and resources (downloaded, not shipped)

Deckle Browser downloads these at run time into its own data folder (`FilterLists` in `src/Deckle.Browser`); no list is committed to this repository or bundled in a build.

- adblock-resources, https://github.com/brave/adblock-resources, MPL-2.0 (its `LICENSE`), Brave Software. The scriptlets and redirect resources (`resources.json`) the engine runs for `##+js(...)` rules.
- EasyList and EasyPrivacy, https://easylist.to, dual licensed under GPL-3.0-or-later and Creative Commons Attribution-ShareAlike 3.0 Unported (https://easylist.to/pages/licence.html), the EasyList authors.
- uBlock Origin's uAssets lists (`filters`, `quick-fixes`, `privacy`), https://github.com/uBlockOrigin/uAssets, GPL-3.0 (its `LICENSE`), Raymond Hill and contributors.

## Referenced implementations

### RawInput.Touchpad

- Repository : https://github.com/emoacht/RawInput.Touchpad
- License : MIT — Copyright (c) emoacht.
- Used as : reference implementation for the Precision Touchpad HID parsing in `Deckle.Input` (preparsed-data approach, value-caps traversal, interop signatures). Reimplemented and extended (tip switch/confidence via the button path, hybrid-mode reassembly, per-device caching); no source file is vendored.

### ThreeFingerDragOnWindows

- Repository : https://github.com/ClementGre/ThreeFingerDragOnWindows
- License : MIT — Copyright (c) Clément Grennerat.
- Used as : studied reference for the three-finger drag mechanics (relative SendInput injection, fractional delta accumulation). Deckle's recognizer is an independent design; no source file is vendored.

## GPLv3 code in the taskbar module

The taskbar module's native half (`native/Deckle.Shell.Taskbar.Native`) is adapted from the projects below, both GPL-3.0 like Deckle. The adapted files carry their attribution in a comment.

- TranslucentTB, the ExplorerTAP component, https://github.com/TranslucentTB/TranslucentTB, GPL-3.0 (see `docs/research/taskbar-briefs-2026-10-18/taskbar-research-2026-10-17.md`). Adapted: the XAML Diagnostics connection loop and the one-attempt-per-thread rule (`xamldiag/tap_connection.cpp`), the TAP site and class factory (`xamldiag/tap_site.cpp`), the visual tree watcher (`xamldiag/visual_tree_watcher.cpp`), and the window-hook injection route (`hook_entry.cpp`).
- Windhawk and its Windows 11 Taskbar Styler mod, by Michael Maltsev (m417z) and the mod authors, https://github.com/ramensoftware/windhawk-mods/blob/main/mods/windows-11-taskbar-styler.wh.cpp, GPL-3.0 (see the same research note). Adapted: the unloadable TAP (releasing the extra reference at `SetSite`), the advise-from-another-thread rule, and the release of element references through `IXamlDiagnosticsTestHooks` (`xamldiag/visual_tree_watcher.cpp`, `xamldiag/tap_site.cpp`).

- The same Windows 11 Taskbar Styler mod, for the rule engine (`xamldiag/rules/`). Adapted: the selector syntax and its parser (`ElementMatcherFromString`, `AdjustTypeName`, `xamldiag/rules/selector.cpp`), the ancestor matching with backtracking over `*` (`xamldiag/rules/element_matcher.cpp`), the conversion of a property and a value through a throw-away `<Style><Setter/></Style>` read by the XAML parser, with the fallback type for classes that have no XAML type (`xamldiag/rules/style_resolver.cpp`), and the apply, watch and restore cycle: `ReadLocalValue` to remember, `SetValue` to apply, `RegisterPropertyChangedCallback` with a re-entrancy guard to hold the value against Explorer's code (`xamldiag/rules/property_override.cpp`).
- Windhawk's Taskbar notification icon spacing mod, https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-notification-icon-spacing.wh.cpp, GPL-3.0. Studied for which elements of the vertical tray to size (`NotifyItemIcon` height and width, the quick-settings stack); the rules themselves are Deckle's and live in `src/Deckle.Shell.Taskbar/Native/TaskbarDocumentBuilder.cs`.
- Windhawk's Taskbar clock customization mod, https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-clock-customization.wh.cpp, GPL-3.0. Studied for the clock's element names, its two text lines and its style settings, and for the formatting with `GetTimeFormatEx` and `GetDateFormatEx` (`xamldiag/behaviors/clock.cpp`). The mod gets its text in through symbol hooks; Deckle writes the `TextBlock` text from the XAML side instead.

Both projects are cited for the technique; Deckle's code is its own rewrite around a named-pipe channel, without their third-party libraries.
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
- `System.Security.Cryptography.ProtectedData` 6.0.0 — MIT — Microsoft. Read from the nuspec license expression. Seals local data at rest with Windows DPAPI: the secret vault, the dictation and take-audio recovery stores, and the autocorrect CLI's observation data.
- `System.Management` 10.0.12 — MIT — Microsoft. Read from the nuspec license expression. Watches process starts through WMI in `Deckle.Orchestration`.
- `BouncyCastle.Cryptography` 2.6.2 — MIT — Legion of the Bouncy Castle Inc. Read from the nuspec license expression. Provides the DTLS-PSK transport used for Philips Hue Entertainment.
- `Anthropic` 12.57.0 — MIT — Anthropic. Read from the nuspec license expression. The official C# SDK through which `Deckle.Llm`'s Claude engine calls the Messages API.
- `ModelContextProtocol` 2.1.0 and `ModelContextProtocol.AspNetCore` 2.1.0 — Apache-2.0 — Model Context Protocol a Series of LF Projects, LLC. Read from the nuspec license expressions; `ModelContextProtocol.Core`, which they pull in, has the same expression. Serve the MCP HTTP endpoint of `Deckle.Anytype.Mcp`.
- `JsonSchema.Net` 9.4.0 — Open Source Maintenance Fee EULA — Greg Dennis. Read from the nuspec license file `OSMFEULA.txt`, which states the source is MIT. Builds the JSON Schema contract of the MCP surface in `Deckle.Anytype.Mcp`. `Json.More.Net` 3.0.1 and `JsonPointer.Net` 7.0.2, which it pulls in, name the same file.

Other third-party packages pulled in transitively by `Deckle.App`, read from its restored `project.assets.json` and from each nuspec:

- `CommunityToolkit.Common` 8.2.1, `CommunityToolkit.WinUI.Extensions`, `.Helpers` and `.Triggers` 8.2.250402 — MIT — nuspec license expressions.
- `Microsoft.ML.OnnxRuntime.Managed` 1.23.0 — MIT — nuspec license file.
- `Microsoft.Web.WebView2` 1.0.3719.77 — BSD-style license, Copyright (C) Microsoft Corporation — nuspec license file `LICENSE.txt`. Pulled in by `Microsoft.WindowsAppSDK.WinUI`.
- `Humanizer.Core` 3.0.10 — MIT — Claire Novotny, Mehdi Khalili — nuspec license expression. Pulled in by `JsonPointer.Net`.

## Bundled assets

### Bitcount Single

- Project : https://github.com/petrvanblokland/TYPETR-Bitcount
- License : SIL Open Font License 1.1 — Copyright 2024 The Bitcount Project Authors.
- Used as : the bundled `BitcountSingle.ttf` display font.

### Public Suffix List

- Project : https://publicsuffix.org
- License : MPL-2.0 (the data). The file's own header carries the notice and the instruction to pull it only from https://publicsuffix.org/list/public_suffix_list.dat.
- Used as : `public_suffix_list.dat`, copied into `src/Deckle.Browser/Data` (the contact email addresses in its comments removed, no rule changed) and embedded in `Deckle.Browser`, read by `RegistrableDomain` so the address field shows the registrable domain of a page.

## Lexical data

The autocorrect module (`Deckle.Autocorrect`) ships compact derived lexicons under `src/Deckle.Autocorrect/Data/`, regenerated by the maintainer's build-data gesture from these sources. The licenses apply to the data files, not to Deckle's code.

### Lexique 3.83

- Project : http://www.lexique.org — New, Pallier, Brysbaert et al.
- License : CC BY-SA 4.0 (the data).
- Used as : source of French inflected forms and frequencies, reduced to the derived `lexicon-fr.tsv.gz` (form + occurrences per million). The derived file remains under CC BY-SA 4.0 with this attribution.

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

### Parakeet TDT 0.6B v3 Ultra

- Models : NVIDIA `nvidia/parakeet-tdt-0.6b-v3` (https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3), post-trained by moondream as `moondream/parakeet-ultra` (https://huggingface.co/moondream/parakeet-ultra), exported to ONNX by Olicorne as `Olicorne/parakeet-tdt-0.6b-v3-ultra-onnx` (https://huggingface.co/Olicorne/parakeet-tdt-0.6b-v3-ultra-onnx).
- License : CC-BY-4.0 (https://creativecommons.org/licenses/by/4.0/). Attribution to NVIDIA for the base model, to moondream for the post-training and to Olicorne for the ONNX export. The weights are unchanged by Deckle.
- Used as : the speech model of the Parakeet backend. Downloaded on demand into `parakeet-tdt-0.6b-v3-ultra` under the models directory, from the repository's commit `db1fd7a2d89672e8e52383505f0149076faab6f7`, each file checked against the SHA-256 pinned in `src/Deckle.Transcription.Parakeet/Setup/ParakeetModelCatalog.cs`. Not redistributed in this repository.

### Silero VAD

- Repository : https://github.com/snakers4/silero-vad
- License : MIT — Copyright (c) 2020-present Silero Team.
- Used as : the `silero_vad.onnx` v6.2 model, downloaded on demand to the models directory and run by `Deckle.Inference.Onnx` for external voice activity detection. Not redistributed in this repository.
- Windhawk's Start menu size mod, https://github.com/ramensoftware/windhawk-mods/blob/main/mods/start-menu-size.wh.cpp, GPL-3.0, "redesigned" Start menu branch. Adapted: setting FrameRoot's Height, MinHeight and MaxHeight and re-applying them when the Start window becomes visible, on the next LayoutUpdated (`xamldiag/behaviors/start_menu_height.cpp`). The height is the window's own, read live, and not a setting of pixels. Also adapted: setting MainMenu's Width to the wanted width less the room FrameRoot keeps around it (`xamldiag/behaviors/start_menu_width.cpp`), held to the room the Start window has.
