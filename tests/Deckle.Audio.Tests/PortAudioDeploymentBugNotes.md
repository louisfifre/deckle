# PortAudio deployment bug notes

## The app had no microphone because portaudio.dll was not beside Deckle.exe

- **Trigger:** Any dictation, mic check or meter in the app built from v0.87.0 on (the PortAudio capture), observed 2026-11-07 on v0.87.14.
- **Observed symptom:** The HUD showed "Microphone unavailable — Mic won't open (error -10000)." and the log wrote `MicrophoneUnavailable` at `phase=probe`. Its `hresult` field rendered as unreadable characters, a separate logging defect noted in `Deckle.Transcription.Tests/TakeStartCheckBugNotes.md`.
- **Cause:** Established by file checks and a rebuild. `Deckle.Audio.csproj` declared the DLL as `Content` with `Link="portaudio.dll"`. With `EnableMsixTooling`, a library's Content lands in the referencing app under a folder named after the library, `Deckle.Audio\portaudio.dll`. The P/Invoke loader is held to the assembly directory and System32 (`PortAudioInterop.cs`), so it never found the DLL; the load failure is reported as PortAudio's -10000 (inferred from the code path, the load stage itself was not logged by the probe at the time; it is now). Test projects do not apply that layout and got the DLL at their root, so every suite was green. Declared as `None`, the DLL lands at the app root; checked after a Debug build.
- **Violated invariant:** A native library a module P/Invokes sits in the app's output root, the folder the loader searches.
- **Recurrence cue:** A native DLL or data file added as `Content` to a WinUI library (`UseWinUI` + `EnableMsixTooling`), or green tests on a feature that loads a file the app's own output does not hold.
- **Regression coverage:** `PortAudioDeploymentTests.ThePortAudioLibraryIsDeclaredSoItLandsBesideTheApp` holds the declaration. It does not build the app, so a layout change from another cause (a publish profile, an SDK update) is not covered; a publish was not checked.
