using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.WinUI.Controls;
using Deckle.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Llm.Rewrite;

// ─── Dictation section of LlmPage: the profile a dictation takes ───────────
//
// The rewrite profile, then the length ladder that hands a longer dictation
// to another profile (LlmSettings.LengthSwitch).
//
// The lists depend on the defined profiles: the host must call Reload() after
// any mutation of the Profiles section so added, renamed or removed profiles
// show up here.
//
// The ladder is one relation between profiles, so it lives here rather than
// in each profile: a threshold only means something against the others, and
// the steps can never overlap. Each step's length list offers only the stops
// between its neighbours, so a step is bounded by them rather than pushing
// one out of the ladder.

public sealed partial class LlmActiveProfileSection : UserControl
{
    // Means "plain transcription". Stored as null in settings, never as this
    // string.
    private const string NoneSentinel = "(None)";

    // Where a first step lands when the ladder starts empty: about three
    // minutes, the length past which a dictation starts reading as a text.
    private const int FirstStepChars = 2000;

    private const string Module = "Deckle.Llm.Rewrite";

    private bool _loading;

    public LlmActiveProfileSection()
    {
        InitializeComponent();
    }

    public void Reload()
    {
        _loading = true;
        var s = LlmSettingsService.Instance.Current;

        // Match the saved choice by stable id (survives renames), then by name
        // for a choice saved without an id. Nothing resolves → (None).
        ActiveProfileCombo.Items.Clear();
        ActiveProfileCombo.Items.Add(new ComboBoxItem { Content = NoneSentinel });
        for (int i = 0; i < s.Profiles.Count; i++)
            ActiveProfileCombo.Items.Add(new ComboBoxItem { Content = s.Profiles[i].Name });
        int index = ResolveProfileIndex(s, s.ActiveProfileId, s.ActiveProfileName);
        ActiveProfileCombo.SelectedIndex = index >= 0 ? index + 1 : 0;

        LengthSwitchToggle.IsOn = s.LengthSwitch.Enabled;

        _loading = false;

        RebuildSteps();
    }

    private static int ResolveProfileIndex(LlmSettings s, string? id, string? name)
    {
        if (!string.IsNullOrEmpty(id))
        {
            for (int i = 0; i < s.Profiles.Count; i++)
                if (s.Profiles[i].Id == id) return i;
        }
        if (!string.IsNullOrWhiteSpace(name))
        {
            for (int i = 0; i < s.Profiles.Count; i++)
                if (string.Equals(s.Profiles[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
        }
        return -1;
    }

    private void ActiveProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ActiveProfileCombo.SelectedIndex < 0) return;
        var s = LlmSettingsService.Instance.Current;
        // Index-based: item 0 is (None), item i+1 is Profiles[i]. A profile
        // literally named "(None)" is never confused with the sentinel.
        int profileIndex = ActiveProfileCombo.SelectedIndex - 1;
        RewriteProfile? profile = profileIndex >= 0 && profileIndex < s.Profiles.Count
            ? s.Profiles[profileIndex]
            : null;
        s.ActiveProfileId = profile?.Id;
        s.ActiveProfileName = profile?.Name;
        LlmSettingsService.Instance.Save();
    }

    // ── Length ladder ───────────────────────────────────────────────────────

    // Switching on an empty ladder adds its first step, so the switch always
    // does something visible; switching off keeps the steps.
    private void LengthSwitchToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        LengthSwitchSettings ladder = LlmSettingsService.Instance.Current.LengthSwitch;
        ladder.Enabled = LengthSwitchToggle.IsOn;
        if (ladder.Enabled && ladder.Steps.Count == 0) AddStep(ladder);
        LlmSettingsService.Instance.Save();
        RebuildSteps();
    }

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        LengthSwitchSettings ladder = LlmSettingsService.Instance.Current.LengthSwitch;
        if (!AddStep(ladder)) return;
        LlmSettingsService.Instance.Save();
        RebuildSteps();
    }

    // The next stop after the last step, taken by the first profile not yet
    // in the ladder other than the rewrite profile (else the first profile).
    private static bool AddStep(LengthSwitchSettings ladder)
    {
        if (NextFreeStop(ladder) is not { } stop) return false;
        var s = LlmSettingsService.Instance.Current;
        if (s.Profiles.Count == 0) return false;

        RewriteProfile profile = s.Profiles.FirstOrDefault(p =>
                p.Id != s.ActiveProfileId && !ladder.Steps.Any(step => step.ProfileId == p.Id))
            ?? s.Profiles[0];
        ladder.Steps.Add(new LengthStep { FromChars = stop, ProfileId = profile.Id });
        return true;
    }

    private static int? NextFreeStop(LengthSwitchSettings ladder)
    {
        IReadOnlyList<int> stops = LengthSwitchSettings.StopsChars;
        if (ladder.Steps.Count == 0) return LlmSettingsMigrations.NearestStop(FirstStepChars);
        int last = ladder.Steps[^1].FromChars;
        foreach (int stop in stops)
            if (stop > last) return stop;
        return null;
    }

    private void RebuildSteps()
    {
        var s = LlmSettingsService.Instance.Current;
        List<LengthStep> steps = s.LengthSwitch.Steps;

        StepsHost.Children.Clear();
        for (int i = 0; i < steps.Count; i++)
        {
            int below = i > 0 ? steps[i - 1].FromChars : 0;
            int above = i + 1 < steps.Count ? steps[i + 1].FromChars : int.MaxValue;
            StepsHost.Children.Add(BuildStepCard(s, steps[i], below, above));
        }

        ApplyLadderState();
    }

    // "From [2,000 characters, about 3 min] [Affinage] [delete]". The length
    // list offers only the stops between the neighbouring steps.
    private SettingsCard BuildStepCard(LlmSettings s, LengthStep step, int below, int above)
    {
        var lengthCombo = new ComboBox { MinWidth = 240, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(lengthCombo, Loc.GetFrom(Module, "LlmLengthStep_LengthName"));
        var allowed = LengthSwitchSettings.StopsChars.Where(stop => stop > below && stop < above).ToList();
        if (!allowed.Contains(step.FromChars)) allowed.Add(step.FromChars);
        allowed.Sort();
        foreach (int stop in allowed)
            lengthCombo.Items.Add(new ComboBoxItem { Content = FormatStop(stop), Tag = stop });
        lengthCombo.SelectedIndex = allowed.IndexOf(step.FromChars);
        lengthCombo.SelectionChanged += (_, _) =>
        {
            if (lengthCombo.SelectedItem is not ComboBoxItem { Tag: int stop } || stop == step.FromChars) return;
            step.FromChars = stop;
            LlmSettingsService.Instance.Save();
            // The neighbours' lists depend on this value; rebuilt after the
            // combo has finished its own selection change.
            DispatcherQueue.TryEnqueue(RebuildSteps);
        };

        var profileCombo = new ComboBox { MinWidth = 160, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(profileCombo, Loc.GetFrom(Module, "LlmLengthStep_ProfileName"));
        foreach (RewriteProfile profile in s.Profiles)
            profileCombo.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
        profileCombo.SelectedIndex = s.Profiles.FindIndex(p => p.Id == step.ProfileId);
        profileCombo.SelectionChanged += (_, _) =>
        {
            if (profileCombo.SelectedItem is not ComboBoxItem { Tag: string id } || id == step.ProfileId) return;
            step.ProfileId = id;
            LlmSettingsService.Instance.Save();
        };

        string removeName = Loc.GetFrom(Module, "LlmLengthStep_RemoveName");
        var remove = new Button
        {
            Content = new FontIcon { Glyph = (string)Application.Current.Resources["Icon.Delete"], FontSize = 14 },
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(remove, removeName);
        ToolTipService.SetToolTip(remove, removeName);
        remove.Click += (_, _) =>
        {
            s.LengthSwitch.Steps.Remove(step);
            LlmSettingsService.Instance.Save();
            RebuildSteps();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(lengthCombo);
        row.Children.Add(profileCombo);
        row.Children.Add(remove);

        return new SettingsCard
        {
            Header = Loc.GetFrom(Module, "LlmLengthStep_Header"),
            Content = row,
        };
    }

    // The spoken duration beside the count, since a person judges a
    // dictation by how long they spoke (LengthSwitchSettings.CharsPerMinute).
    internal static string FormatStop(int chars)
    {
        string count = chars.ToString("N0", CultureInfo.CurrentCulture);
        if (chars < LengthSwitchSettings.CharsPerMinute)
            return string.Format(Loc.GetFrom(Module, "LlmLengthStep_StopUnderMinute_Format"), count);
        int minutes = (int)Math.Round(chars / (double)LengthSwitchSettings.CharsPerMinute);
        return string.Format(Loc.GetFrom(Module, "LlmLengthStep_Stop_Format"), count, minutes);
    }

    // Never greyed for batch rewriting: that setting only takes effect on a
    // streamed take with a rewrite profile set, which this page cannot see
    // (the transcription strategy lives in another module), so the
    // description states the exception instead.
    private void ApplyLadderState()
    {
        LengthSwitchSettings ladder = LlmSettingsService.Instance.Current.LengthSwitch;

        StepsPanel.Visibility = LengthSwitchToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        AddStepButton.IsEnabled = NextFreeStop(ladder) is not null
            && LlmSettingsService.Instance.Current.Profiles.Count > 0;
    }

    // Scope: the active profile choice and the length ladder; resetting them
    // must not wipe user-authored profiles. No confirmation: both come back
    // in a few clicks.
    private void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new LlmSettings();
        var s = LlmSettingsService.Instance.Current;
        s.ActiveProfileId   = defaults.ActiveProfileId;
        s.ActiveProfileName = defaults.ActiveProfileName;
        s.LengthSwitch      = defaults.LengthSwitch;
        LlmSettingsService.Instance.Save();
        Reload();
    }
}
