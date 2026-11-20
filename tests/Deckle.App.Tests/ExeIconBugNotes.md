# Exe icon bug notes

## Every Deckle window showed the generic icon on the taskbar

- Trigger: any Deckle window with a taskbar button (Settings, Mail, Projects, Folders), and Deckle pinned or launched from its Start shortcut.
- Symptom: the taskbar showed no Deckle mark for any window, while title bars and the tray showed it (reported 2026-11-20).
- Cause: Deckle.exe never embedded an icon; `Deckle.App.csproj` had no `<ApplicationIcon>`. The window `.ico` files were valid and copied next to the exe, and each window called `AppWindow.SetIcon`. Inferred, not observed button by button: the taskbar group, a pinned button and the Start shortcut (`Deckle.Install.Shortcut` points at the exe's first icon) read the exe's resource, not the window's.
- Violated invariant: Deckle.exe carries the window icon at rest as its own resource; see the comment on `<ApplicationIcon>` in `src/Deckle.App/Deckle.App.csproj`.
- Fix: `<ApplicationIcon>` on the window icon at rest, today `Assets\Icons\deckle--state--rest.ico` (first `deckle--state--rest--form--normal.ico`, before the mark kept one form). A shortcut or pin made before the fix may keep the generic icon from Windows' icon cache until it is re-pinned or the cache is refreshed.
- Recurrence cue: Deckle.exe shows the generic icon in Explorer; the taskbar mark differs from the title bar mark.
- Guard: `ExeIconTests.TheExeEmbedsTheWindowIconAtRest` checks the project declares the icon and the file exists. It does not read the built exe's resource.
