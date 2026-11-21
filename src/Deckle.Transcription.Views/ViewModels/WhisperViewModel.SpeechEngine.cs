using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;
using Deckle.Localization;
using Deckle.Settings;

namespace Deckle.Transcription.Views;

// ── WhisperViewModel — the speech engine choice ──────────────────────────────
//
// Which engine transcribes (Whisper or Parakeet), and the two settings only
// Parakeet reads: its model folder and the precision of its encoder. The choice
// is read once at startup, where the composition root builds the backend and
// applies the gates of the chosen engine, so a change here reaches the next
// start, never the running engine: the page's restart footer carries it, like
// the model and the GPU switch. The two Parakeet values are read at each model
// load.
//
// Parakeet is a preview. Its label says so (the way the Deckle Mail module
// does), and the card says what that means in one sentence.
public partial class WhisperViewModel
{
    [ObservableProperty]
    public partial AsrBackendKind Backend { get; set; }

    [ObservableProperty]
    public partial string ParakeetModelDirectory { get; set; }

    [ObservableProperty]
    public partial ParakeetEncoderPrecision ParakeetEncoder { get; set; }

    partial void OnBackendChanged(AsrBackendKind value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

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

    // Whether the settings that only Whisper reads apply to the chosen engine.
    // The pages hide them when it is not Whisper: a setting that does not apply
    // is hidden, never greyed.
    public bool IsWhisperChosen => Backend == AsrBackendKind.Whisper;

    // The folder as the App sees it, null when nothing answers (tests, previews)
    // or the answer fails. Read on demand: the files change under the page.
    private ParakeetModelFolderState? InspectParakeetFolder()
    {
        try { return SettingsHost.InspectParakeetModelFolder?.Invoke(ParakeetModelDirectory); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { return null; }
    }

    // The speech engine fold. The engine choice sits in the fold's header; the
    // fold holds what only Parakeet reads, each card named by its own
    // VisibleWhen, and the composer's own line says there is nothing to set
    // when Whisper is chosen (Whisper's settings are the cards below the fold).
    //
    // The precision is advanced: FP32 is the default because FP16 can cost
    // fidelity on French speech, and FP16 only halves the memory (no speed gain
    // on DirectML), so almost nobody needs it. Defaults read the EngineSettings
    // POCO initializer, the single source the settings file persists.
    public IReadOnlyList<SettingDescriptor> SpeechEngineSettings =>
    [
        Setting.Mode<AsrBackendKind>("WhisperEngineMode",
            () => Backend,
            value => Backend = value,
            [
                (AsrBackendKind.Whisper, "WhisperEngineWhisper"),
                (AsrBackendKind.Parakeet, "WhisperEngineParakeet"),
            ],
            [
                Setting.Path("WhisperParakeetFolderCard",
                    () => ParakeetModelDirectory,
                    value => ParakeetModelDirectory = value,
                    new PathArgs(FolderPickerMode.Editable,
                        DefaultPath: () => SettingsHost.InspectParakeetModelFolder?.Invoke("").Folder ?? string.Empty),
                    visibleWhen: () => Backend == AsrBackendKind.Parakeet,
                    defaultValue: () => new EngineSettings().ParakeetModelDirectory),
                // A diagnostic, read and never written: the block Windows-style
                // settings pages use for a state (Readout), in an editor card
                // that owns no value and so has no reset.
                Setting.Editor<bool>("WhisperParakeetFilesCard",
                    () => true,
                    _ => { },
                    new EditorArgs(BuildParakeetFilesReadout),
                    visibleWhen: () => Backend == AsrBackendKind.Parakeet),
                Setting.Choice<ParakeetEncoderPrecision>("WhisperParakeetEncoderCard",
                    () => ParakeetEncoder,
                    value => ParakeetEncoder = value,
                    [
                        (ParakeetEncoderPrecision.Fp32, "WhisperParakeetEncoderFp32"),
                        (ParakeetEncoderPrecision.Fp16, "WhisperParakeetEncoderFp16"),
                    ],
                    isAdvanced: true,
                    visibleWhen: () => Backend == AsrBackendKind.Parakeet,
                    defaultValue: () => new EngineSettings().ParakeetEncoder,
                    advisory: ParakeetEncoderAdvisory),
            ],
            defaultValue: () => new EngineSettings().Backend),
    ];

    // FP16 asked, folder known, and no FP16 encoder in it: the load would fail.
    private string? ParakeetEncoderAdvisory()
    {
        if (Backend != AsrBackendKind.Parakeet || ParakeetEncoder != ParakeetEncoderPrecision.Fp16) return null;
        ParakeetModelFolderState? folder = InspectParakeetFolder();
        return folder is { IsComplete: true, HasFp16Encoder: false }
            ? Loc.GetFromOptional("Deckle.Transcription.Views", "WhisperParakeetEncoderMissing/Message")
            : null;
    }

    // Wording and meaning of the folder's state. Null when nothing answers: the
    // readout then shows nothing.
    private static (string Message, ReadoutStatus Status)? DescribeFolder(ParakeetModelFolderState? folder)
    {
        if (folder is null) return null;
        string key = !folder.IsComplete ? "WhisperParakeetFiles_Incomplete"
            : folder.HasFp16Encoder ? "WhisperParakeetFiles_CompleteWithFp16"
            : "WhisperParakeetFiles_Complete";
        string? message = Loc.GetFromOptional("Deckle.Transcription.Views", key);
        return message is null ? null : (message, folder.IsComplete ? ReadoutStatus.Success : ReadoutStatus.Caution);
    }

    // The state of the model folder as a Readout that follows the view model:
    // the folder, the precision and the engine all change what it says.
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
