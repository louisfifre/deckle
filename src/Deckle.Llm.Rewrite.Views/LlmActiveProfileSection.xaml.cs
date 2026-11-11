using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.WinUI.Controls;
using Deckle.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Deckle.Localization;

namespace Deckle.Llm.Rewrite.Views;

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
// the steps can never overlap. Each step's length field is bounded by its
// neighbours, so a step stops short of them rather than pushing one out of
// the ladder.

public sealed partial class LlmActiveProfileSection : UserControl
{
    // Where a first step lands when the ladder starts empty: about three
    // minutes, the length past which a dictation starts reading as a text.
    private const int FirstStepChars = 2000;

    private const string Module = "Deckle.Llm.Rewrite.Views";

    private bool _loading;

    // The length field of each step card, in ladder order. A committed length
    // moves its neighbours' bounds in place: rebuilding the cards would pull
    // the field out from under the pointer between two clicks on its arrows.
    private readonly List<NumberBox> _lengthBoxes = new();

    // Set while code writes a length field, so its ValueChanged is not taken
    // for the person's edit.
    private bool _syncingLengths;

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
        ActiveProfileCombo.Items.Add(new ComboBoxItem { Content = Loc.GetFrom(Module, "LlmActiveProfile_None") });
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
        // Index-based: item 0 is "None" (plain transcription, stored as null),
        // item i+1 is Profiles[i]. A profile literally named "None" is never
        // confused with it.
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

    // A new step lands at twice the last one, taken by the first profile not
    // yet in the ladder other than the rewrite profile (else the first
    // profile). Doubling keeps the steps growing apart as dictations
    // lengthen; the field moves it from there.
    private static bool AddStep(LengthSwitchSettings ladder)
    {
        if (NextStepChars(ladder) is not { } chars) return false;
        var s = LlmSettingsService.Instance.Current;
        if (s.Profiles.Count == 0) return false;

        RewriteProfile profile = s.Profiles.FirstOrDefault(p =>
                p.Id != s.ActiveProfileId && !ladder.Steps.Any(step => step.ProfileId == p.Id))
            ?? s.Profiles[0];
        ladder.Steps.Add(new LengthStep { FromChars = chars, ProfileId = profile.Id });
        return true;
    }

    private static int? NextStepChars(LengthSwitchSettings ladder)
    {
        if (ladder.Steps.Count == 0) return FirstStepChars;
        int last = ladder.Steps[^1].FromChars;
        if (last >= LengthSwitchSettings.MaxChars) return null;
        return LengthSwitchSettings.Snap(last * 2);
    }

    private void RebuildSteps()
    {
        var s = LlmSettingsService.Instance.Current;

        StepsHost.Children.Clear();
        _lengthBoxes.Clear();
        foreach (LengthStep step in s.LengthSwitch.Steps)
            StepsHost.Children.Add(BuildStepCard(s, step));

        UpdateLengthBounds();
        ApplyLadderState();
    }

    // Each field stops one grain short of its neighbours, so the steps stay
    // strictly increasing whatever is typed. A step's bounds always hold its
    // own value, so moving them never changes it. The arrows step by the
    // grain of the current length, so they move finely on a short length and
    // coarsely on a long one.
    private void UpdateLengthBounds()
    {
        List<LengthStep> steps = LlmSettingsService.Instance.Current.LengthSwitch.Steps;
        _syncingLengths = true;
        try
        {
            for (int i = 0; i < _lengthBoxes.Count && i < steps.Count; i++)
            {
                NumberBox box = _lengthBoxes[i];
                box.Minimum = i > 0
                    ? LengthSwitchSettings.Above(steps[i - 1].FromChars)
                    : LengthSwitchSettings.MinChars;
                box.Maximum = i + 1 < steps.Count
                    ? LengthSwitchSettings.Below(steps[i + 1].FromChars)
                    : LengthSwitchSettings.MaxChars;
                int grain = LengthSwitchSettings.GrainAt(steps[i].FromChars);
                box.SmallChange = grain;
                box.LargeChange = grain * 10;
            }
        }
        finally { _syncingLengths = false; }
    }

    // "From (characters) [2000] [Affinage] [delete]", the spoken duration in
    // the description. The duration is a legend rather than a second field:
    // it is an estimate (CharsPerMinute), and a legend under the header keeps
    // every step's fields at the same edge, where text beside the field would
    // shift the profile list by its own width.
    private SettingsCard BuildStepCard(LlmSettings s, LengthStep step)
    {
        var card = new SettingsCard
        {
            Header = Loc.GetFrom(Module, "LlmLengthStep_Header"),
            Description = FormatSpokenLength(step.FromChars),
        };

        var lengthBox = new NumberBox
        {
            Value = step.FromChars,
            // Arrows on hover and focus, in a flyout that pushes no layout.
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
            MinWidth = 96,
            VerticalAlignment = VerticalAlignment.Center,
            // Digits only, no grouping, as every number field of the app.
            NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter
            {
                IntegerDigits = 1,
                FractionDigits = 0,
                IsGrouped = false,
            },
        };
        AutomationProperties.SetName(lengthBox, Loc.GetFrom(Module, "LlmLengthStep_LengthName"));
        _lengthBoxes.Add(lengthBox);

        // ValueChanged on a NumberBox is the commit (Enter, focus loss, an
        // arrow click), the end of the gesture, so it writes at once.
        lengthBox.ValueChanged += (_, _) =>
        {
            if (_syncingLengths) return;
            // A cleared field gives NaN: the stored length comes back rather
            // than an empty field. A typed figure is kept on the grain and
            // between the neighbours, here rather than trusting the control.
            int chars = double.IsNaN(lengthBox.Value)
                ? step.FromChars
                : Math.Clamp(LengthSwitchSettings.Snap(lengthBox.Value),
                    (int)lengthBox.Minimum, (int)lengthBox.Maximum);
            if (lengthBox.Value != chars)
            {
                _syncingLengths = true;
                try { lengthBox.Value = chars; }
                finally { _syncingLengths = false; }
            }
            if (chars == step.FromChars) return;

            step.FromChars = chars;
            card.Description = FormatSpokenLength(chars);
            LlmSettingsService.Instance.Save();
            UpdateLengthBounds();
            ApplyLadderState();
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
        row.Children.Add(lengthBox);
        row.Children.Add(profileCombo);
        row.Children.Add(remove);
        card.Content = row;
        return card;
    }

    // The spoken duration of a length, since a person judges a dictation by
    // how long they spoke (LengthSwitchSettings.CharsPerMinute).
    internal static string FormatSpokenLength(int chars)
    {
        if (chars < LengthSwitchSettings.CharsPerMinute)
            return Loc.GetFrom(Module, "LlmLengthStep_UnderMinute");
        int minutes = (int)Math.Round(chars / (double)LengthSwitchSettings.CharsPerMinute);
        return string.Format(CultureInfo.CurrentCulture,
            Loc.GetFrom(Module, "LlmLengthStep_Minutes_Format"), minutes);
    }

    // Never greyed for batch rewriting: that setting only takes effect on a
    // streamed take with a rewrite profile set, which this page cannot see
    // (the transcription strategy lives in another module), so the
    // description states the exception instead.
    private void ApplyLadderState()
    {
        LengthSwitchSettings ladder = LlmSettingsService.Instance.Current.LengthSwitch;

        StepsPanel.Visibility = LengthSwitchToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        AddStepButton.IsEnabled = NextStepChars(ladder) is not null
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
