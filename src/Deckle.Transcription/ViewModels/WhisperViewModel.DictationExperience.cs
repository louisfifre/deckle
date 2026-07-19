using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Settings;

namespace Deckle.Transcription;

public partial class WhisperViewModel
{
    // ── Dictation experience (overlay HUD + auto-paste) ──────────────────────
    //
    // Relocated from GeneralPage in the settings reorg: the on-screen HUD shown
    // during dictation (master toggle + fade-on-proximity, animations, position)
    // and whether the transcript is pasted into the focused window after copy.
    // These describe how dictation surfaces itself and delivers its output, so
    // they live on the Dictation page beside the engine that produces them.
    //
    // Persistence stays in the shell's Overlay / Paste sections (settings.json),
    // read at runtime by the HUD (Deckle.Hud) and the hotkey paste path
    // (App.Hotkeys) — this VM only surfaces them. Pushed through a dedicated
    // PushBehaviourToSettings so the shell save stays separate from the module's
    // TranscriptionSettings save.

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

    partial void OnOverlayPositionChanged(string value)
    {
        if (_isSyncing) return;
        PushBehaviourToSettings();
    }

    // Writes the overlay/paste values back to the shell's Overlay/Paste sections —
    // kept separate from PushToSettings (which persists this module's own
    // TranscriptionSettings) because these live in the shell's settings.json.
    private void PushBehaviourToSettings()
    {
        var shell = SettingsService.Instance.Current;
        shell.Paste.AutoPasteEnabled = AutoPasteEnabled;
        shell.Overlay.Enabled = OverlayEnabled;
        shell.Overlay.FadeOnProximity = OverlayFadeOnProximity;
        shell.Overlay.Animations = OverlayAnimations;
        shell.Overlay.Position = OverlayPosition;
        SettingsService.Instance.Save();
    }
}

