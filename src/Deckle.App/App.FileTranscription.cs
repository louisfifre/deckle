using System;
using Deckle.App;
using Deckle.Transcription;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Deckle.App;

public partial class App
{
    // File transcription entry — the tray "Transcribe a file…" command. Opens
    // the system file picker for one audio file, then hands its path to the
    // existing monolithic engine, which decodes it (Media Foundation → 16 kHz
    // mono) and runs it through the same pipeline as dictation. The transcript
    // is written to a .txt on disk and copied to the clipboard; it is never
    // pasted. Concurrency with a live dictation is refused by the engine's CAS
    // guard, which emits its own "engine busy" feedback — the App does nothing
    // on that result. Completion surfaces only as a HUD message (SavedToFile →
    // ShowFileSaved, wired in App.xaml.cs); nothing opens.
    private async void TranscribeFileFromTray()
    {
        // async void: the tray click arrives on the UI thread with no awaiter,
        // so the whole body is guarded — an unobserved exception here (picker
        // COM failure, transient owner-window creation) must never bubble out
        // and take the tray down with it.
        try
        {
            if (_engine is null)
            {
                // Speech isn't provisioned (native runtime + model absent), so
                // the engine was never composed. Same posture as OnHotkey: tell
                // the user at the moment of intent and point them to setup.
                DeckleAppSource.Log.UserFeedbackEmitted(
                    0, // Info
                    "Dictation isn't set up yet",
                    "Open Settings › Dictation to download the speech engine and model.",
                    1); // Overlay
                return;
            }

            // Owner HWND for the file picker. At tray-click time no visible
            // window exists: the tray carrier window is already hidden (its
            // click action runs after Hide), and the message-only host is a
            // message-only HWND, not a valid dialog owner. So we spin a minimal
            // WinUI Window purely to borrow its HWND — never Activate()d, so
            // nothing flashes on screen — and Close() it in the finally once the
            // picker has returned.
            var ownerWindow = new Window();
            try
            {
                IntPtr hwnd = WindowNative.GetWindowHandle(ownerWindow);

                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.MusicLibrary,
                };
                picker.FileTypeFilter.Add(".mp3");
                picker.FileTypeFilter.Add(".m4a");
                picker.FileTypeFilter.Add(".aac");
                picker.FileTypeFilter.Add(".wav");
                picker.FileTypeFilter.Add(".flac");
                picker.FileTypeFilter.Add(".wma");
                picker.FileTypeFilter.Add(".ogg");
                picker.FileTypeFilter.Add(".opus");
                InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file is null) return; // user cancelled — nothing to do

                // Hand off to the engine. On Started, the HUD goes to Charging
                // (ShowPreparing renders that state — no status string produces
                // it), and the engine drives every later transition (Transcribing,
                // then the SavedToFile message). On IgnoredBusy / IgnoredDisposed
                // the engine emits its own feedback, so the App does nothing.
                var result = _engine.RequestFileTranscription(file.Path);
                if (result == ToggleResult.Started)
                    _hudWindow?.ShowPreparing();
            }
            finally
            {
                ownerWindow.Close();
            }
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.HudWarning();
            DeckleAppSource.Log.HudWarningDetail($"File transcription entry failed: {ex.Message}");
        }
    }
}
