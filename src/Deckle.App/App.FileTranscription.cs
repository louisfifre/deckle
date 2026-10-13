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
    // The selection goes to the engine as one batch: it returns to Idle only
    // after the whole selection drains.
    private async void TranscribeFilesFromTray()
    {
        // async void with no awaiter, so the whole body is guarded: an exception
        // here must never bubble out and take the tray down with it.
        try
        {
            if (_engine is null)
            {
                // No engine means speech is not provisioned.
                DeckleAppSource.Log.UserFeedbackEmitted(
                    0, // Info
                    "Dictation isn't set up yet",
                    "Open Settings › Dictation to download the speech engine and model.",
                    1); // Overlay
                return;
            }

            // The picker needs an owner HWND and none fits at tray-click time: the
            // tray carrier is already hidden, and a message-only HWND is not a
            // valid dialog owner. This Window only lends its HWND; it is never
            // activated, so nothing flashes on screen.
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
