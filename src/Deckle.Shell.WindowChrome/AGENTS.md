---
description: Window-chrome corrections shared by Deckle's XAML windows — workarounds for WinAppSDK chrome bugs, each deleted when the SDK fixes upstream.
type: agent-instructions
---

# AGENTS.md — Deckle.Shell.WindowChrome

Corrections to the native window chrome that every Deckle XAML window needs identically — today the TitleBar caption-inset px/DIP fix. Each correction documents the upstream bug it patches and its deletion condition in its own file; the module empties itself as the SDK catches up.

Lives beside `Deckle.Shell` rather than inside it because the corrections manipulate the XAML visual tree, and `Deckle.Shell` deliberately stays XAML-free (pure WinAppSDK API surface).
