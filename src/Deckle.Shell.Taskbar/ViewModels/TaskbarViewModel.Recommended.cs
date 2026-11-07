using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;

namespace Deckle.Shell.Taskbar;

// The Recommended section card. Its switch shows what Windows holds (the
// policy in either hive), not a value Deckle keeps, and a change is a
// gesture that asks Windows for approval:
//
//  - Turning it on runs Hide behind the switch (the composer's
//    confirmOnEnable): the switch turns on only once the value is in place.
//  - Turning it off runs Show after the switch has moved, since the composer
//    writes an off at once. If the prompt is declined or the write fails, the
//    switch goes back to the real state.
//  - When the machine policy holds the section hidden, the switch shows on
//    and cannot be turned off: no prompt, and the card says why.
//
// Both prompts block, so both run off the UI thread. A second change while a
// prompt is open is ignored. Whatever went wrong is told on the card, through
// the one advisory channel, until the next attempt or the next visit.
public partial class TaskbarViewModel
{
    private const string DeclinedKey = "TaskbarPage_RecommendedDeclined";
    private const string FailedKey = "TaskbarPage_RecommendedFailed";
    private const string HeldByMachineKey = "TaskbarPage_RecommendedHeldByMachine";

    private readonly RecommendedSectionPolicy _recommendedSection;
    private bool _recommendedBusy;
    private bool _heldByMachine;
    private string? _recommendedMessageKey;

    [ObservableProperty]
    public partial bool HideRecommended { get; set; }

    private void LoadRecommended()
    {
        if (_recommendedBusy) return;
        _recommendedMessageKey = null;
        ReadRecommendedState();
    }

    // Windows is the source: the switch and the machine note follow it.
    private void ReadRecommendedState()
    {
        _heldByMachine = _recommendedSection.IsHeldByMachine();
        HideRecommended = _recommendedSection.IsHidden();
    }

    // The switch's write path. An on has already been confirmed and written by
    // ConfirmHideRecommendedAsync; an off is the request to give the section
    // back.
    internal void RecommendedSwitched(bool on)
    {
        HideRecommended = on;
        if (!on) _ = ShowRecommendedAsync();
    }

    internal async Task<bool> ConfirmHideRecommendedAsync()
    {
        if (_recommendedBusy) return false;
        _recommendedBusy = true;
        _recommendedMessageKey = null;
        try
        {
            PolicyOutcome outcome = await _recommendedSection.HideAsync();
            _recommendedMessageKey = MessageKeyOf(outcome);
            return outcome == PolicyOutcome.Applied;
        }
        finally
        {
            _recommendedBusy = false;
            _heldByMachine = _recommendedSection.IsHeldByMachine();
            OnPropertyChanged(StatusPropertyName);
        }
    }

    internal async Task ShowRecommendedAsync()
    {
        if (_recommendedBusy) return;
        _recommendedBusy = true;
        _recommendedMessageKey = null;
        try
        {
            PolicyOutcome outcome = await _recommendedSection.ShowAsync();
            _recommendedMessageKey = MessageKeyOf(outcome);
        }
        finally
        {
            _recommendedBusy = false;
            // Back to the real state, whatever happened: a declined prompt
            // finds the policy still in place and the switch returns to on.
            ReadRecommendedState();
            OnPropertyChanged(StatusPropertyName);
        }
    }

    private static string? MessageKeyOf(PolicyOutcome outcome) => outcome switch
    {
        PolicyOutcome.Applied => null,
        PolicyOutcome.PermissionDenied => DeclinedKey,
        PolicyOutcome.HeldByMachine => HeldByMachineKey,
        _ => FailedKey,
    };

    // The card's one line under it: what just went wrong, else the standing
    // note that the machine policy holds the section.
    private string? RecommendedAdvisory() =>
        RecommendedMessageKey is { } key ? Loc.GetFromOptional(TaskbarSettingsModule.ResourceLibrary, key) : null;

    // The .resw key of that line, or null when there is nothing to say.
    internal string? RecommendedMessageKey =>
        _recommendedMessageKey ?? (_heldByMachine ? HeldByMachineKey : null);
}