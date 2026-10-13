using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Settings;

namespace Deckle.Transcription;

public partial class WhisperViewModel
{
    // These persist in the shell's Overlay / Paste sections, read at runtime by
    // the HUD and the hotkey paste path; this view model only surfaces them, and
    // saves them through the shell's SettingsService, not TranscriptionSettings.

    [ObservableProperty]
    public partial bool AutoPasteEnabled { get; set; }

    [ObservableProperty]
    public partial bool OverlayEnabled { get; set; }

    [ObservableProperty]
    public partial bool OverlayFadeOnProximity { get; set; }

    [ObservableProperty]
    public partial bool OverlayAnimations { get; set; }

    [ObservableProperty]
    public partial string OverlayPosition { get; set; }

    partial void OnAutoPasteEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        PushBehaviourToSettings();
    }

    partial void OnOverlayEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        PushBehaviourToSettings();
    }

    partial void OnOverlayFadeOnProximityChanged(bool value)
    {
        if (_isSyncing) return;
        PushBehaviourToSettings();
    }

    partial void OnOverlayAnimationsChanged(bool value)
    {
        if (_isSyncing) return;
        PushBehaviourToSettings();
    }

    // "Left" or "Right" of the primary display; the panel reads it once per show.
    [ObservableProperty]
    public partial string TranscriptPanelSide { get; set; }

    partial void OnOverlayPositionChanged(string value)
    {
        if (_isSyncing) return;
        PushBehaviourToSettings();
    }

    partial void OnTranscriptPanelSideChanged(string value)
    {
        if (_isSyncing) return;
        PushBehaviourToSettings();
    }

    // Folds a hand-edited or unknown persisted value onto a real picker option,
    // so the Choice always shows a selection: anything but "Left" is "Right".
    private static string NormalizeTranscriptPanelSide(string? side) =>
        side == "Left" ? "Left" : "Right";

    private void PushBehaviourToSettings()
    {
        var shell = SettingsService.Instance.Current;
        shell.Paste.AutoPasteEnabled = AutoPasteEnabled;
        shell.Overlay.Enabled = OverlayEnabled;
        shell.Overlay.FadeOnProximity = OverlayFadeOnProximity;
        shell.Overlay.Animations = OverlayAnimations;
        shell.Overlay.Position = OverlayPosition;
        shell.Overlay.TranscriptPanelSide = TranscriptPanelSide;
        SettingsService.Instance.Save();
    }
}

