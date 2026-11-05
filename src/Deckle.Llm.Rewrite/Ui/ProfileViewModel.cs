using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;
using Microsoft.UI.Xaml.Data;
namespace Deckle.Llm.Rewrite;

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
    [NotifyPropertyChangedFor(nameof(Summary))]
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

    // Chosen by the length of the take (RewriteProfile.AutoSelect*). The
    // minutes are always one of RewriteProfile.AutoSelectStopsMinutes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool AutoSelect { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial int AutoSelectFromMinutes { get; set; }

    // The shortcut that starts a dictation rewritten with this profile.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial HotkeyChord? Hotkey { get; set; }

    public string Id { get; private set; } = "";

    // ── State ────────────────────────────────────────────────────────────────

    // Drives IsExpanded so a freshly added profile opens. Never reset: once
    // the expander is open, it no longer matters.
    [ObservableProperty]
    public partial bool IsNew { get; set; }

    // ── Computed display properties ──────────────────────────────────────────

    public string Summary
    {
        get
        {
            string model = string.IsNullOrWhiteSpace(Model) ? "(no model)" : Model;
            string provider = string.IsNullOrWhiteSpace(ProviderName) ? "(no provider)" : ProviderName;
            string summary = $"{provider}  ·  {model}";
            if (ShowsGenerationHints)
            {
                int idx = Math.Clamp((int)CtxIndex, 0, CtxKSteps.Length - 1);
                summary += $"  ·  {CtxKSteps[idx]}K ctx  ·  temp {Temperature:F2}";
            }
            // What makes the profile run, readable with the fold closed: the
            // list of profiles then doubles as the overview of the thresholds.
            if (AutoSelect)
                summary += "  ·  " + string.Format(
                    Loc.GetFrom("Deckle.Llm.Rewrite", "LlmProfile_SummaryFromMinutes_Format"),
                    AutoSelectFromMinutes);
            if (HotkeyChordText.Format(Hotkey) is { } chord)
                summary += $"  ·  {chord}";
            return summary;
        }
    }

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
    partial void OnAutoSelectChanged(bool value) { if (!_isSyncing) PushToSettings(); }
    partial void OnAutoSelectFromMinutesChanged(int value) { if (!_isSyncing) PushToSettings(); }
    partial void OnHotkeyChanged(HotkeyChord? value) { if (!_isSyncing) PushToSettings(); }

    // The stop nearest to a stored value, so a hand-edited 7.5 or 45 lands
    // on the slider instead of off it.
    internal static int NearestStopIndex(int minutes)
    {
        var stops = RewriteProfile.AutoSelectStopsMinutes;
        int best = 0;
        for (int i = 1; i < stops.Count; i++)
            if (Math.Abs(stops[i] - minutes) < Math.Abs(stops[best] - minutes)) best = i;
        return best;
    }

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
        AutoSelect = false;
        AutoSelectFromMinutes = new RewriteProfile().AutoSelectFromMinutes;
        Hotkey = null;
        _isSyncing = false;
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    public void LoadFrom(int index, RewriteProfile profile, bool isNew)
    {
        _isSyncing = true;
        ProfileIndex = index;
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

        Id = profile.Id;
        AutoSelect = profile.AutoSelect;
        AutoSelectFromMinutes = RewriteProfile.AutoSelectStopsMinutes[NearestStopIndex(profile.AutoSelectFromMinutes)];
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
        p.AutoSelect = AutoSelect;
        p.AutoSelectFromMinutes = AutoSelectFromMinutes;
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

// The threshold slider indexes RewriteProfile.AutoSelectStopsMinutes; its
// thumb says the minutes the index stands for.
public sealed class MinutesStopConverter : IValueConverter
{
    public static string Format(int minutes) => string.Format(
        Loc.GetFrom("Deckle.Llm.Rewrite", "LlmProfile_MinutesValue_Format"), minutes);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var stops = RewriteProfile.AutoSelectStopsMinutes;
        int idx = value is double d ? (int)Math.Round(d) : 0;
        return idx >= 0 && idx < stops.Count ? Format(stops[idx]) : value;
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
