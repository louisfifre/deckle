using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deckle.Input.Autocorrect;

// One row of the per-app list on AutocorrectPage: an app the user has decided
// on, its on/off state, and the gesture to forget it. The row never touches
// persistence itself — toggling Enabled and invoking Forget call back into the
// view-model (which routes to AutocorrectSettingsService), so the decision
// write stays in one place. DisplayName is a UI-only label; the engine matches
// on ProcessName.
public sealed partial class AutocorrectAppRow : ObservableObject
{
    private readonly Action<AutocorrectAppRow, bool> _onToggled;
    private readonly Action<AutocorrectAppRow> _onForgotten;

    // Guards the seed assignment in the constructor so hydrating the row from
    // the stored decision is not mistaken for a user toggle (mirrors the
    // _isSyncing pattern in the page view-models).
    private bool _syncing;

    public string ProcessName { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    public AutocorrectAppRow(
        string processName,
        string displayName,
        bool enabled,
        Action<AutocorrectAppRow, bool> onToggled,
        Action<AutocorrectAppRow> onForgotten)
    {
        ProcessName = processName;
        DisplayName = displayName;
        _onToggled = onToggled;
        _onForgotten = onForgotten;

        _syncing = true;
        Enabled = enabled;
        _syncing = false;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_syncing) return;
        _onToggled(this, value);
    }

    [RelayCommand]
    private void Forget() => _onForgotten(this);
}
