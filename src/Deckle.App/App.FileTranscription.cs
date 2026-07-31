using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    // Opens the system file picker for one or more audio files, then hands each
    // path in selection order to the existing monolithic engine. Each file is a
    // complete independent run (decode → transcribe → .txt + clipboard), and the
    // next one starts only after the worker has returned to Idle. This keeps the
    // backend single-threaded and lets one failed file leave the rest of the batch
    // untouched. Concurrency with live dictation remains guarded by the engine.
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

            foreach (var file in files)
            {
                if (!await StartFileTranscriptionAndWaitForIdleAsync(file.Path))
                    break;
            }
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.HudWarning();
            DeckleAppSource.Log.HudWarningDetail($"File transcription entry failed: {ex.Message}");
        }
    }

    private async Task<bool> StartFileTranscriptionAndWaitForIdleAsync(string path)
    {
        var engine = _engine;
        if (engine is null) return false;

        // Finished is raised before the worker's terminal teardown, so it is too
        // early to start the next file. StatusChanged emits Ready only after the
        // state has become Idle; IsBusy is the semantic check, independent of the
        // localized status text.
        var idle = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnStatusChanged(string _)
        {
            if (!engine.IsBusy)
                idle.TrySetResult(true);
        }

        engine.StatusChanged += OnStatusChanged;
        try
        {
            var result = engine.RequestFileTranscription(path);
            if (result != ToggleResult.Started)
                return false;

            _hudWindow?.ShowPreparing();
            await idle.Task;
            return true;
        }
        finally
        {
            engine.StatusChanged -= OnStatusChanged;
        }
    }
}
