# Window description bug notes

## The paste events wrote the foreground window's title

- **Trigger:** every paste attempt at the end of a dictation. `TranscriptionEngine.PasteFromClipboard` described the foreground window through `Win32Util.DescribeHwnd` twice, in `PasteForeground` before the paste and in `PasteSent` after it.
- **Observed symptom:** none on screen. Found on 2026-10-22 by a code audit of what the operational events carry, not in a log file: no Deckle data file was opened. The description read `exe / "title" / focus=Class`, so a line could hold a document name, a mail subject or a page title. According to the audit, both events are Verbose, Push-keyword events with no gate of their own, so they reach the application log (`app.jsonl`) whenever that log is on. That last point was not checked against a written file.
- **Cause:** `DescribeHwnd` called `GetWindowText` on the foreground window and quoted the result. Its header gave the title as part of the diagnostic; the diagnosis it served needed only the process and the focused control's class.
- **Resolution (2026-10-22):** the description is now `exe / focus=Class`; the title is no longer read. Application logs written before the fix may still hold titles until they are deleted.
- **Violated invariant:** a log line describes the machine, never what the person is working on (`deckle-logging`: what the person said, typed or captured stays out).
- **Recurrence cue:** `GetWindowText`, `GetWindowTextLength`, a UI Automation `Name` or `Value`, or any quoted free text in a string that reaches an event payload or message; a "describe" helper whose output grows a field taken from another application.
- **Regression coverage:** `Win32UtilTests.TheDescriptionNeverCarriesTheWindowTitle` (regression) creates a hidden popup window with a known title, describes it, and checks that the title is absent and the process name present.
