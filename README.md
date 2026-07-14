# Deckle

**Deckle gathers the small tools of a Windows day into one local app.** Hold a key and talk — your words land as text. Skip your accents while typing — they come back. Let your lights follow the screen. One hotkey, one tray icon, and nothing leaving your machine.

An unpackaged Windows 11 desktop app built on WinUI 3, .NET 10, and the Windows App SDK. It lives in the tray and starts with you.

> **Status — personal project, early public release.** Tested on two Windows 11 machines. Windows 11 only, unpackaged (no Microsoft Store). No account, no cloud dependency, no telemetry leaving the device.

<!-- deckle-stats:start -->
## Development pulse

| First commit | Commits | Active days | Lines added | Lines touched | Current tracked lines |
|---:|---:|---:|---:|---:|---:|
| 2026-04-01 | 1,409 | 63 | 247,800 | 367,495 | 127,706 |

<sub>Generated from Git history on 2026-07-01. Counts include tracked text files only for the current line total.</sub>
<!-- deckle-stats:end -->

---

## Why Deckle

- **Local by default.** Speech recognition, autocorrect, screen capture — all run on your own hardware. No API key, no subscription, no round-trip to a server.
- **Private by construction.** Nothing to sign into, nothing phoned home. The little telemetry that exists is opt-in and stays in a local folder you can open and delete.
- **Native to Windows 11.** Fluent surfaces, system materials, a real tray menu, honest per-user autostart — it aims to feel like something Microsoft could have shipped.
- **One roof, many tools.** Rather than five utilities each with its own tray icon, Deckle gathers complementary helpers behind a single quiet process.

---

## What it does

### Voice transcription

**Press a hotkey, talk, release — the text is on your clipboard, ready to paste.** The flagship, and the most polished.

Transcription runs locally through Whisper ([whisper.cpp](https://github.com/ggerganov/whisper.cpp)) with the model you choose — Vulkan-accelerated, with a CPU fallback when there is no GPU. Neural voice-activity detection trims the silence; an optional pre-processing pass sharpens the microphone for the recognizer. If the focused field accepts text, Deckle pastes for you; otherwise the transcription waits on the clipboard. A local rewrite pass through Ollama can clean up the raw text — off by default.

### System-wide autocorrect

**Type anywhere, and the accents you skipped come back — on your terms, app by app.** French-first today.

Deckle watches the keyboard only in apps you have enrolled, and never in password fields, terminals, or code editors. Corrections stay conservative and reversible: the correction inlay lets you take any of them back explicitly. What it learns lives in a personal dictionary you can read, edit, and clear at will.

### Ambient lighting

**Deckle reads your screen in real time and drives your Philips Hue lights to match.** The newest subsystem, still taking shape.

Frames are captured GPU-side through DXGI Output Duplication — no yellow border, minimal latency, nothing written to disk. A color pipeline (linear-light averaging, gamut clipping, OKLCh saturation) keeps hues honest across display profiles, HDR included. Map screen regions to individual lights; calls go straight to your local Hue bridge, with no cloud relay.

### Desktop touches

Small refinements that smooth the day:

- **Three-finger drag** on a precision trackpad — three fingers hold the button and drag, lifting drops after a short grace delay.
- **Taskbar cover** — an opaque band that masks the taskbar when you want the screen edge clean.

### A local task space for your AI assistants

Deckle can run a small local MCP server that exposes an Anytype project space to assistants like Claude or Codex — the same plumbing that helps drive Deckle's own development. Developer-oriented, off the main path.

---

## On the workbench

Honest about what is not ready yet: **read-aloud** (text-to-speech) is scaffolded but dormant, a **richer rewrite** mode is in exploration, and finer **mouse-wheel** control is early groundwork. More assistance modules will follow the same local-first line.

---

## Get Deckle

1. Download the **Deckle-Setup** installer from the [latest release](https://github.com/louisfifre/deckle/releases).
2. Run it. Install is per-user — no admin prompt. Deckle lands in your profile and starts in the tray.
3. On first launch, a short wizard fetches the speech runtime and the models it needs.

To run it at every login: **Settings → General → Launch at startup**. It writes a per-user `HKCU\...\Run` entry — no service, nothing machine-wide.

### Build from source

For contributors and the curious. Read the [contribution notes](CONTRIBUTING.md), bootstrap a fresh Windows 11 machine, then build and run:

```powershell
scripts/lib/bootstrap-dev-env.ps1              # .NET 10, VS 2026, tooling
scripts/lib/build-run.ps1 -Configuration Release
```

The interactive menu at `scripts/deckle.ps1` wraps every dev workflow. The full detail — worker scripts, switches, native-runtime sourcing — lives in [`scripts/README.md`](scripts/README.md).

---

## Privacy & security

- Audio and screen captures never leave the machine; ambient frames stay in GPU memory and are never written to disk.
- A global hotkey is registered while the app runs. The clipboard write and auto-paste happen only after a UI Automation check that the target actually accepts text.
- Autocorrect never observes password fields, and acts only in apps you enroll.
- Telemetry is strictly opt-in, gated by an explicit consent dialog, and stored locally.

---

## Built solo, with leverage

Deckle is also a record of a threshold: modern LLMs let one person — coming from product and UX more than systems engineering — reach into architecture, native platform work, observability, and release discipline faster than would have been reasonable alone. That is liberating and a little uncomfortable, and Deckle is built inside that tension: local, legible, no account, nothing leaving the machine.

---

## Acknowledgements

Built on [whisper.cpp](https://github.com/ggerganov/whisper.cpp), [WinUI 3](https://github.com/microsoft/microsoft-ui-xaml) and the [Windows App SDK](https://github.com/microsoft/WindowsAppSDK), the [Windows Community Toolkit](https://github.com/CommunityToolkit/Windows), [Win2D](https://github.com/microsoft/Win2D), the [Vulkan SDK](https://www.lunarg.com/vulkan-sdk/), and the [Philips Hue](https://developers.meethue.com/) Entertainment API. Full attributions in [NOTICE.md](NOTICE.md).

## License

MIT — see [LICENSE](LICENSE).
