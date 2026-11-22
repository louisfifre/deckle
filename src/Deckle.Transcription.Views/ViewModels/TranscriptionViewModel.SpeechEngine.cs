using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;
using Deckle.Localization;
using Deckle.Settings;

namespace Deckle.Transcription.Views;

// ── TranscriptionViewModel — the speech engine ─────────────────────────────────────
//
// The settings of the speech engine, Parakeet: its model folder and the
// precision of its encoder. Both are read at each model load, so a change
// reaches the next load, after the idle unload or the restart the page's
// footer offers.
public partial class TranscriptionViewModel
{
    [ObservableProperty]
    public partial string ParakeetModelDirectory { get; set; }

    [ObservableProperty]
    public partial ParakeetEncoderPrecision ParakeetEncoder { get; set; }

    partial void OnParakeetModelDirectoryChanged(string value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnParakeetEncoderChanged(ParakeetEncoderPrecision value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    // The folder as the App sees it, null when nothing answers (tests, previews)
    // or the answer fails. Read on demand: the files change under the page.
    private ParakeetModelFolderState? InspectParakeetFolder()
    {
        try { return SettingsHost.InspectParakeetModelFolder?.Invoke(ParakeetModelDirectory); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { return null; }
    }

    // The speech engine fold, with no master: the model folder, the state of its
    // files and the encoder precision.
    //
    // The precision is advanced: FP32 is the default because FP16 can cost
    // fidelity on French speech, and FP16 only halves the memory (no speed gain
    // on DirectML), so almost nobody needs it. Defaults read the EngineSettings
    // POCO initializer, the single source the settings file persists.
    public IReadOnlyList<SettingDescriptor> SpeechEngineSettings =>
    [
        Setting.Section("TranscriptionSpeechEngineFold",
            [
                Setting.Path("TranscriptionParakeetFolderCard",
                    () => ParakeetModelDirectory,
                    value => ParakeetModelDirectory = value,
                    new PathArgs(FolderPickerMode.Editable,
                        DefaultPath: () => SettingsHost.InspectParakeetModelFolder?.Invoke("").Folder ?? string.Empty),
                    defaultValue: () => new EngineSettings().ParakeetModelDirectory),
                // A diagnostic, read and never written: the block Windows-style
                // settings pages use for a state (Readout), in an editor card
                // that owns no value and so has no reset.
                Setting.Editor<bool>("TranscriptionParakeetFilesCard",
                    () => true,
                    _ => { },
                    new EditorArgs(BuildParakeetFilesReadout)),
                Setting.Choice<ParakeetEncoderPrecision>("TranscriptionParakeetEncoderCard",
                    () => ParakeetEncoder,
                    value => ParakeetEncoder = value,
                    [
                        (ParakeetEncoderPrecision.Fp32, "TranscriptionParakeetEncoderFp32"),
                        (ParakeetEncoderPrecision.Fp16, "TranscriptionParakeetEncoderFp16"),
                    ],
                    isAdvanced: true,
                    defaultValue: () => new EngineSettings().ParakeetEncoder,
                    advisory: ParakeetEncoderAdvisory),
            ]),
    ];

    // FP16 asked, folder known, and no FP16 encoder in it: the load would fail.
    private string? ParakeetEncoderAdvisory()
    {
        if (ParakeetEncoder != ParakeetEncoderPrecision.Fp16) return null;
        ParakeetModelFolderState? folder = InspectParakeetFolder();
        return folder is { IsComplete: true, HasFp16Encoder: false }
            ? Loc.GetFromOptional("Deckle.Transcription.Views", "TranscriptionParakeetEncoderMissing/Message")
            : null;
    }

    // Wording and meaning of the folder's state. Null when nothing answers: the
    // readout then shows nothing.
    private static (string Message, ReadoutStatus Status)? DescribeFolder(ParakeetModelFolderState? folder)
    {
        if (folder is null) return null;
        string key = !folder.IsComplete ? "TranscriptionParakeetFiles_Incomplete"
            : folder.HasFp16Encoder ? "TranscriptionParakeetFiles_CompleteWithFp16"
            : "TranscriptionParakeetFiles_Complete";
        string? message = Loc.GetFromOptional("Deckle.Transcription.Views", key);
        return message is null ? null : (message, folder.IsComplete ? ReadoutStatus.Success : ReadoutStatus.Caution);
    }

    // The state of the model folder as a Readout that follows the view model:
    // the folder and the precision both change what it says.
    private Readout BuildParakeetFilesReadout()
    {
        var readout = new Readout();

        void Refresh()
        {
            (string Message, ReadoutStatus Status)? state = DescribeFolder(InspectParakeetFolder());
            readout.Value = state?.Message;
            readout.Status = state?.Status ?? ReadoutStatus.None;
        }

        Refresh();
        PropertyChanged += (_, _) => Refresh();
        return readout;
    }
}
