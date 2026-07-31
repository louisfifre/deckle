using System;
using System.Collections.Generic;
using Deckle.App;
using Deckle.Transcription;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Deckle.App;

public partial class App
{
    // File transcription entry — the tray "Transcribe audio files…" command.
    // Opens the system file picker for one or more audio files, then produces all
    // selected paths into the engine-owned FIFO. The engine is the sole consumer:
    // each path becomes an independent run and the next starts only after Idle.
    private async void TranscribeFilesFromTray()
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
            IReadOnlyList<StorageFile> files;
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

                files = await picker.PickMultipleFilesAsync();
            }
            finally
            {
                ownerWindow.Close();
            }

            if (files.Count == 0) return; // user cancelled — nothing to do

            var paths = new string[files.Count];
            for (int i = 0; i < files.Count; i++)
                paths[i] = files[i].Path;

            _engine.EnqueueFileTranscriptions(paths);
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.HudWarning();
            DeckleAppSource.Log.HudWarningDetail($"File transcription entry failed: {ex.Message}");
        }
    }
}
