# Third-party notices

Deckle depends on the following third-party components. License texts
are reproduced from the upstream projects.

## Native dependencies

### whisper.cpp

- Repository : https://github.com/ggerganov/whisper.cpp
- License : MIT
- Used as : pre-built native DLLs (`libwhisper.dll`, `ggml.dll`,
  `ggml-base.dll`, `ggml-cpu.dll`, `ggml-vulkan.dll`) loaded at runtime.

### MinGW-w64 runtime

- Project : https://www.mingw-w64.org/
- License : public domain / various permissive (see upstream).
- Used as : runtime DLLs (`libgcc_s_seh-1.dll`, `libstdc++-6.dll`,
  `libwinpthread-1.dll`) shipped alongside the whisper.cpp build that was
  produced with the GCC toolchain.

### Vulkan loader

- Project : https://www.lunarg.com/vulkan-sdk/
- License : Apache 2.0 (loader and tools).
- Used at runtime via the system-installed Vulkan ICD when GPU
  acceleration is requested by whisper.cpp.

## .NET / NuGet packages

The following packages are referenced via `src/Deckle.App/Deckle.App.csproj`.
Each ships under its own license — see the package metadata on
nuget.org or the upstream repository for the full text.

- `Microsoft.WindowsAppSDK` — MIT — Microsoft.
- `Microsoft.Windows.SDK.BuildTools` — MIT — Microsoft.
- `Microsoft.Graphics.Win2D` — MIT — Microsoft.
- `CommunityToolkit.Mvvm` — MIT — Microsoft / .NET Foundation.
- `CommunityToolkit.WinUI.Controls.SettingsControls` — MIT — .NET
  Foundation / Microsoft.
- `CommunityToolkit.WinUI.Extensions` / `Helpers` / `Triggers` — MIT —
  .NET Foundation / Microsoft.
- `Microsoft.ML.OnnxRuntime` — MIT — Microsoft. Runs the external Silero
  VAD on the CPU execution provider.

## Models

Whisper model weights downloaded from
[ggerganov/whisper.cpp on Hugging Face](https://huggingface.co/ggerganov/whisper.cpp)
are distributed under the original Whisper license (MIT). They are not
redistributed in this repository.

### Silero VAD

- Repository : https://github.com/snakers4/silero-vad
- License : MIT — Copyright (c) 2020-present Silero Team.
- Used as : the `silero_vad.onnx` v5 model, downloaded on demand to the
  models directory and run by `Deckle.Inference.Onnx` for external voice
  activity detection. Not redistributed in this repository.
