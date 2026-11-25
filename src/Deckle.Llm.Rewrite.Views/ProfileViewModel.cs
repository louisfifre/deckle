using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;
using Deckle.Core;
using Microsoft.UI.Xaml.Data;
using Deckle.Localization;
namespace Deckle.Llm.Rewrite.Views;

// Auto-saves on every change; there is no Save/Cancel surface.
//
// Temperature and CtxIndex are doubles (Slider.Value type). TopP and
// RepeatPenalty use NaN for "not set" (null in the POCO), as NumberBox does.
public partial class ProfileViewModel : ObservableObject
{
    internal static readonly int[] CtxKSteps = { 1, 2, 4, 8, 16, 32, 64, 128, 256 };
    private const double DefaultTemperature = 0.5;
    private const int DefaultNumCtxK = 2;

    private bool _isSyncing;

    public int ProfileIndex { get; set; }

    // Stable across renames; who uses the profile is read by it
    // (ProfileUsers).
    public string ProfileId { get; private set; } = "";

    // ── Editable properties ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string Name { get; set; }

    // The provider by Id; ProviderName is what the summary shows and the
    // section keeps it current from the provider list.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string ProviderId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string ProviderName { get; set; }

    // Temperature and context size are hints a local runtime takes and a
    // hosted API ignores or rejects, so the sliders hide for a hosted provider.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool ShowsGenerationHints { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(OffersReasoningOff), nameof(ShownReasoning))]
    public partial string Model { get; set; }

    [ObservableProperty]
    public partial string SystemPrompt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(TempDisplay))]
    public partial double Temperature { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(CtxDisplay))]
    public partial double CtxIndex { get; set; }

    [ObservableProperty]
    public partial double TopP { get; set; }

    [ObservableProperty]
    public partial double RepeatPenalty { get; set; }

    // The profile's reasoning level. A profile saved before the setting
    // existed (null) reads as Low, the request the engine sends without one.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownReasoning))]
    public partial ReasoningLevel Reasoning { get; set; }

    // Shown for a Claude provider only: the local engines drop the level.
    [ObservableProperty]
    public partial bool ShowsReasoning { get; set; }

    // Whether the profile's model can stop thinking, so the list offers Off.
    public bool OffersReasoningOff => ClaudeEngine.AcceptsOff(Model);

    // The level as the list shows it: a stored Off on a model that cannot
    // stop thinking is sent as Low (ClaudeEngine.ReasoningFor), so it shows
    // as Low. The stored value is kept, should the model change back.
    public ReasoningLevel ShownReasoning =>
        Reasoning == ReasoningLevel.Off && !OffersReasoningOff ? ReasoningLevel.Low : Reasoning;

    // The shortcut that starts a dictation rewritten with this profile: a
    // switch, then the chord it reveals. Off keeps the chord.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool HotkeyEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial HotkeyChord? Hotkey { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    // A profile just added: its page opens with the name box focused. Never
    // reset: the next Reload loads every profile as not new.
    [ObservableProperty]
    public partial bool IsNew { get; set; }

    // ── Computed display properties ──────────────────────────────────────────

    public string Summary
    {
        get
        {
            string model = string.IsNullOrWhiteSpace(Model) ? Loc.Get("LlmProfile_NoModel") : Model;
            string provider = string.IsNullOrWhiteSpace(ProviderName) ? Loc.Get("LlmProfile_NoProvider") : ProviderName;
            string summary = $"{provider}  ·  {model}";
            if (ShowsGenerationHints)
            {
                int idx = Math.Clamp((int)CtxIndex, 0, CtxKSteps.Length - 1);
                // The field labels' own words (Context size, Temperature), not
                // the abbreviations "ctx" and "temp".
                summary += "  ·  " + Loc.Format("LlmProfile_Hints_Format", CtxKSteps[idx], Temperature.ToString("F2"));
            }
            // Who uses it, readable with the fold closed.
            foreach (string user in UserTexts())
                summary += $"  ·  {user}";
            return summary;
        }
    }

    // What the settings say about who uses the profile, in words: the
    // dictation, then the shortcut that starts it.
    private IEnumerable<string> UserTexts()
    {
        LlmSettings settings = LlmSettingsService.Instance.Current;
        RewriteProfile? profile = settings.Profiles.Find(p => p.Id == ProfileId);
        if (profile is null) yield break;

        foreach (ProfileUser user in ProfileUsers.Of(profile, settings))
        {
            if (user.Kind == ProfileUserKind.Dictation)
                yield return Loc.GetFrom("Deckle.Llm.Rewrite.Views", "LlmProfile_UsedByDictation");
            else if (HotkeyChordText.Format(user.Chord) is { } chord)
                yield return chord;
        }
    }

    // The settings that say who uses it changed elsewhere (a dictation step, a
    // shortcut): the card words it again.
    internal void RefreshSummary() => OnPropertyChanged(nameof(Summary));

    public string TempDisplay => Temperature.ToString("F2");

    public string CtxDisplay
    {
        get
        {
            int idx = Math.Clamp((int)CtxIndex, 0, CtxKSteps.Length - 1);
            return $"{CtxKSteps[idx]}K";
        }
    }

    // ── OnChanged → auto-save ────────────────────────────────────────────────

    partial void OnNameChanged(string value) { if (!_isSyncing) PushToSettings(); }
    partial void OnProviderIdChanged(string value) { if (!_isSyncing) PushToSettings(); }
    partial void OnModelChanged(string value) { if (!_isSyncing) PushToSettings(); }
    partial void OnSystemPromptChanged(string value) { if (!_isSyncing) PushToSettings(); }
    partial void OnTemperatureChanged(double value) { if (!_isSyncing) PushToSettings(); }
    partial void OnCtxIndexChanged(double value) { if (!_isSyncing) PushToSettings(); }
    partial void OnTopPChanged(double value) { if (!_isSyncing) PushToSettings(); }
    partial void OnRepeatPenaltyChanged(double value) { if (!_isSyncing) PushToSettings(); }
    partial void OnReasoningChanged(ReasoningLevel value) { if (!_isSyncing) PushToSettings(); }
    partial void OnHotkeyEnabledChanged(bool value) { if (!_isSyncing) PushToSettings(); }
    partial void OnHotkeyChanged(HotkeyChord? value) { if (!_isSyncing) PushToSettings(); }

    // ── Constructor ──────────────────────────────────────────────────────────

    public ProfileViewModel()
    {
        _isSyncing = true;
        Name = "";
        ProviderId = "";
        ProviderName = "";
        ShowsGenerationHints = true;
        Model = "";
        SystemPrompt = "";
        Temperature = DefaultTemperature;
        CtxIndex = Array.IndexOf(CtxKSteps, DefaultNumCtxK);
        TopP = double.NaN;
        RepeatPenalty = double.NaN;
        Reasoning = ReasoningLevel.Low;
        ShowsReasoning = false;
        HotkeyEnabled = false;
        Hotkey = null;
        _isSyncing = false;
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    public void LoadFrom(int index, RewriteProfile profile, bool isNew)
    {
        _isSyncing = true;
        ProfileIndex = index;
        ProfileId = profile.Id;
        IsNew = isNew;

        Name = profile.Name;
        ProviderId = profile.ProviderId;
        Model = profile.Model;
        SystemPrompt = profile.SystemPrompt;
        Temperature = profile.Temperature ?? DefaultTemperature;

        int ctxIdx = Array.IndexOf(CtxKSteps, profile.NumCtxK ?? DefaultNumCtxK);
        CtxIndex = ctxIdx >= 0 ? ctxIdx : Array.IndexOf(CtxKSteps, DefaultNumCtxK);

        TopP = profile.TopP ?? double.NaN;
        RepeatPenalty = profile.RepeatPenalty ?? double.NaN;
        Reasoning = profile.Reasoning ?? ReasoningLevel.Low;

        HotkeyEnabled = profile.HotkeyEnabled;
        Hotkey = profile.Hotkey;

        _isSyncing = false;
    }

    // ── Write-back ───────────────────────────────────────────────────────────

    // Name and Model are trimmed at write time only: trimming the bound
    // property would fight the cursor during typing.
    private void PushToSettings()
    {
        var profiles = LlmSettingsService.Instance.Current.Profiles;
        if (ProfileIndex >= profiles.Count) return;

        var p = profiles[ProfileIndex];
        p.Name = Name.Trim();
        p.ProviderId = ProviderId;
        p.Model = Model.Trim();
        p.SystemPrompt = SystemPrompt;
        p.Temperature = Temperature;
        p.NumCtxK = CtxKSteps[Math.Clamp((int)CtxIndex, 0, CtxKSteps.Length - 1)];
        p.TopP = double.IsNaN(TopP) ? null : (double?)TopP;
        p.RepeatPenalty = double.IsNaN(RepeatPenalty) ? null : (double?)RepeatPenalty;
        p.Reasoning = Reasoning;
        p.HotkeyEnabled = HotkeyEnabled;
        p.Hotkey = Hotkey;
        LlmSettingsService.Instance.Save();
    }
}

// ── Converters for slider tooltips ──────────────────────────────────────────
//
// Top-level, not nested classes, so XAML can instantiate them as
// StaticResources.

public sealed class CtxTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        int idx = value is double d ? (int)d : 0;
        if (idx >= 0 && idx < ProfileViewModel.CtxKSteps.Length)
            return $"{ProfileViewModel.CtxKSteps[idx]}K";
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

// Two decimals, so the slider's doubles never show as "0.15000000001".
public sealed class TempTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is double d ? d.ToString("F2") : (value?.ToString() ?? "");

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
