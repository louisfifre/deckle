---
description: Window-chrome workarounds for WinAppSDK bugs, deleted when fixed upstream.
type: agent-instructions
---

# AGENTS.md — Deckle.Shell.WindowChrome

- Each correction names, in its own file, the upstream bug it patches and the condition for deleting it. Delete a correction when the SDK fixes the bug.
- The module lives beside `Deckle.Shell`, not inside it, because the corrections manipulate the XAML visual tree and `Deckle.Shell` stays XAML-free.
