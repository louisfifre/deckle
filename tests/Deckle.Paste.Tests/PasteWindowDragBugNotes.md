# Paste window drag bug notes

## The window stayed stuck to the cursor after a drag

- **Trigger:** in v0.135.0, press anywhere in the Paste to folder window with the mouse and drag past the system drag threshold.
- **Observed symptom:** reported by the maintainer: the window could not be dropped, it stayed stuck to the cursor, then went away. The log of that run shows the process alive throughout (no crash dump, no error) and the window created a second time 26 s after the first, so the window had been closed, not the app.
- **Cause (inferred from the code, confirmed by the fix on the maintainer's machine):** the window handed the gesture to Windows' own move loop, `SendMessage(WM_NCLBUTTONDOWN, HTCAPTION)` after `ReleaseCapture`, from inside a XAML `PointerMoved` handler. That loop is modal and ran inside the handler; the button's release did not reach it, so it went on moving the window until another click. Why the window then closed was not observed: the likely path is the click or the Escape that freed it, which landed on the close cross or on the window's own Escape handler.
- **Invariant:** a XAML pointer handler never starts a modal Win32 loop; a window without a title bar moves itself from the pointer events it already receives. See the comment at the top of `src/Deckle.Paste.Views/PasteWindow.Drag.cs`.
- **Fix:** past the threshold the window captures the pointer and calls `AppWindow.Move` from the cursor's travel until the press ends. `WindowDragGesture` (Deckle.Paste) holds the arithmetic. Observed working by the maintainer on 2026-11-18 from a Debug build.
- **Recurrence cue:** `WM_NCLBUTTONDOWN`, `WM_SYSCOMMAND` with `SC_MOVE`, or `ReleaseCapture` in a WinUI 3 window's pointer handler; a window that follows the cursor with no button held.
- **Regression coverage:** `WindowDragGestureTests` pins the arithmetic: nothing moves within the threshold, the window follows the pointer past it, and it stops when the press ends. The delivery of the release itself lives in WinUI input and has no headless seam: a manual drag is the check.
