using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Deckle.Localization;

namespace Deckle.Llm.Rewrite.Views;

// ─── Dictation section of LlmPage: the dictation steps ─────────────────────
//
// specs/deckle-dictation-rewriting.md, group Page. "Rewrite dictations" alone;
// while it is on, "Rewrite while I speak" (one switch for every step, on
// streaming transcription only), the timeline (RewriteStepTimeline, which
// shows and never writes), one card per step (LlmSettings.Dictation.Steps),
// then "Add a step". A step card holds its profile, its length and, while
// rewriting as the person speaks, its block size. Every card reads "From" and
// a length, the first one too (0: from the beginning); the first has no
// remove button, since it always exists.
//
// The lists depend on the defined profiles: the host must call Reload() after
// any mutation of the Profiles section so added, renamed or removed profiles
// show up here. It also depends on the transcription strategy, which lives in
// another module: the host gives it through LlmSettingsModule.TranscriptionStreams,
// and Reload reads it again on every navigation.
//
// Each length field is bounded by its neighbours, so a step stops short of
// them rather than pushing one out of order.
public sealed partial class LlmDictationSection : UserControl
{
    private const string Module = "Deckle.Llm.Rewrite.Views";

    // Where a second step lands when the first starts from the beginning:
    // about three minutes, the length past which a dictation starts reading
    // as a text.
    private const int SecondStepChars = 2000;

    private const double ProfileColumnWidth = 200;
    private const double LengthColumnWidth = 260;

    private bool _loading;

    // The length field of each step card, in step order. A committed length moves its neighbours' bounds in
    // place: rebuilding the cards would pull the field out from under the
    // pointer between two clicks on its arrows.
    private readonly List<NumberBox> _lengthBoxes = new();

    // Set while code writes a length field, so its ValueChanged is not taken
    // for the person's edit.
    private bool _syncingLengths;

    // The "in blocks of" field of each step card: shown while the steps
    // rewrite as the person speaks, hidden and kept otherwise (Page 6).
    private readonly List<FrameworkElement> _blockFields = new();

    public LlmDictationSection()
    {
        InitializeComponent();
    }

    private static LlmSettings Settings => LlmSettingsService.Instance.Current;
    private static DictationRewriteSettings Dictation => Settings.Dictation;

    // Page 3: without streaming transcription nothing is transcribed before
    // Stop, so "Rewrite while I speak" is hidden and every step rewrites at
    // Stop.
    private static bool TranscriptionStreams => LlmSettingsModule.TranscriptionStreams?.Invoke() ?? true;

    // Whether the steps rewrite as the person speaks on this machine: the
    // switch, where it applies.
    private static bool RewritesWhileSpeaking => TranscriptionStreams && Dictation.RewriteWhileSpeaking;

    public void Reload()
    {
        _loading = true;
        EnableToggle.IsOn = Dictation.Enabled;
        WhileSpeakingToggle.IsOn = Dictation.RewriteWhileSpeaking;
        _loading = false;
        RebuildSteps();
    }

    // Page 7: the switch moves no length; it shows or hides the block sizes
    // and redraws the timeline in the other mode.
    private void WhileSpeakingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || WhileSpeakingToggle.IsOn == Dictation.RewriteWhileSpeaking) return;
        Dictation.RewriteWhileSpeaking = WhileSpeakingToggle.IsOn;
        LlmSettingsService.Instance.Save();
        ApplyState();
    }

    private void EnableToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Dictation.Enabled = EnableToggle.IsOn;
        LlmSettingsService.Instance.Save();
        ApplyState();
    }

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        if (NextStepChars() is not { } chars || Settings.Profiles.Count == 0) return;

        // Taken by the first profile no step names yet (else the first
        // profile), with the block size of the step before it: the fields and
        // the list move it from there.
        List<RewriteStep> steps = Dictation.Steps;
        RewriteProfile profile = Settings.Profiles.FirstOrDefault(p => !steps.Any(step => step.ProfileId == p.Id))
            ?? Settings.Profiles[0];
        steps.Add(new RewriteStep { ProfileId = profile.Id, FromChars = chars, BlockChars = steps[^1].BlockChars });
        LlmSettingsService.Instance.Save();
        RebuildSteps();
    }

    // A new step lands at twice the last one's start (SecondStepChars after a
    // first step from the beginning). Doubling keeps the steps growing apart
    // as dictations lengthen. Null once the last one is at the maximum.
    private static int? NextStepChars()
    {
        int last = Dictation.Steps[^1].FromChars;
        if (last == 0) return SecondStepChars;
        if (last >= DictationRewriteSettings.MaxChars) return null;
        return DictationRewriteSettings.Snap(last * 2);
    }

    private void RebuildSteps()
    {
        StepsHost.Children.Clear();
        _lengthBoxes.Clear();
        _blockFields.Clear();
        List<RewriteStep> steps = Dictation.Steps;
        for (int i = 0; i < steps.Count; i++)
            StepsHost.Children.Add(BuildStepCard(i));

        UpdateLengthBounds();
        ApplyState();
    }

    private void ApplyState()
    {
        StepsPanel.Visibility = Dictation.Enabled ? Visibility.Visible : Visibility.Collapsed;
        WhileSpeakingCard.Visibility = TranscriptionStreams ? Visibility.Visible : Visibility.Collapsed;
        Visibility blocks = RewritesWhileSpeaking ? Visibility.Visible : Visibility.Collapsed;
        foreach (FrameworkElement field in _blockFields) field.Visibility = blocks;
        AddStepButton.IsEnabled = NextStepChars() is not null && Settings.Profiles.Count > 0;
        RefreshTimeline();
    }

    private void RefreshTimeline()
    {
        List<RewriteProfile> profiles = Settings.Profiles;
        Timeline.Show(StepTimelineLayout.For(
            Dictation,
            id => profiles.Find(p => p.Id == id)?.Name,
            TranscriptionStreams));
    }

    // Each field stops one grain short of its neighbours, so the steps stay
    // strictly increasing whatever is typed. A step's bounds always hold its
    // own value, so moving them never changes it. The arrows step by the grain
    // of the current length, so they move finely on a short length and
    // coarsely on a long one.
    private void UpdateLengthBounds()
    {
        List<RewriteStep> steps = Dictation.Steps;
        _syncingLengths = true;
        try
        {
            for (int i = 0; i < _lengthBoxes.Count && i < steps.Count; i++)
            {
                NumberBox box = _lengthBoxes[i];
                // The first step goes down to 0, from the beginning.
                box.Minimum = i > 0 ? DictationRewriteSettings.Above(steps[i - 1].FromChars) : 0;
                box.Maximum = i + 1 < steps.Count
                    ? Math.Max(box.Minimum, DictationRewriteSettings.Below(steps[i + 1].FromChars))
                    : DictationRewriteSettings.MaxChars;
                SetGrain(box, steps[i].FromChars);
            }
        }
        finally { _syncingLengths = false; }
    }

    // [Profile ▾]   From [1000] characters   in blocks of [600]   [🗑]
    private SettingsCard BuildStepCard(int index)
    {
        RewriteStep step = Dictation.Steps[index];
        bool first = index == 0;

        var profileCombo = new ComboBox { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(profileCombo, Loc.GetFrom(Module, "LlmStep_ProfileName"));
        foreach (RewriteProfile profile in Settings.Profiles)
            profileCombo.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
        profileCombo.SelectedIndex = Settings.Profiles.FindIndex(p => p.Id == step.ProfileId);
        profileCombo.SelectionChanged += (_, _) =>
        {
            if (profileCombo.SelectedItem is not ComboBoxItem { Tag: string id } || id == step.ProfileId) return;
            step.ProfileId = id;
            LlmSettingsService.Instance.Save();
            RefreshTimeline();
        };

        var length = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        length.Children.Add(Caption(Loc.GetFrom(Module, "LlmStep_From")));
        length.Children.Add(BuildLengthBox(step));
        length.Children.Add(Caption(Loc.GetFrom(Module, "LlmStep_Characters")));

        // Page 6: the size of the stretch this step's profile rewrites in one
        // go, shown only while the steps rewrite as the person speaks.
        var blocks = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = RewritesWhileSpeaking ? Visibility.Visible : Visibility.Collapsed,
        };
        blocks.Children.Add(Caption(Loc.GetFrom(Module, "LlmStep_InBlocksOf")));
        // The unit is the length's, just before it, as the mockup reads:
        // a second "characters" would push the card past its width.
        blocks.Children.Add(BuildBlockBox(step));
        _blockFields.Add(blocks);

        // The first card keeps the room of the remove button, laid out but
        // not shown, so every card's fields line up on the same edges.
        var remove = new Button
        {
            Content = new FontIcon { Glyph = (string)Application.Current.Resources["Icon.Delete"], FontSize = 14 },
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = first ? 0 : 1,
            IsHitTestVisible = !first,
            IsTabStop = !first,
        };
        if (first) AutomationProperties.SetAccessibilityView(remove, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        string removeName = Loc.GetFrom(Module, "LlmStep_RemoveName");
        AutomationProperties.SetName(remove, removeName);
        ToolTipService.SetToolTip(remove, removeName);
        remove.Click += (_, _) =>
        {
            Dictation.Steps.Remove(step);
            LlmSettingsService.Instance.Save();
            RebuildSteps();
        };
        // A card laid out with its content on the left does not stretch it,
        // so a star column would shrink to its content: the profile and the
        // length take fixed widths instead, room for "From", a five-digit
        // field and "characters", and every card's columns line up.
        var row = new Grid { ColumnSpacing = 24 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ProfileColumnWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LengthColumnWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(row, profileCombo, 0);
        profileCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
        Place(row, length, 1);
        Place(row, blocks, 2);
        Place(row, remove, 3);

        return new SettingsCard
        {
            ContentAlignment = ContentAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = row,
        };

        static void Place(Grid grid, FrameworkElement element, int column)
        {
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
    }

    private NumberBox BuildLengthBox(RewriteStep step)
    {
        NumberBox lengthBox = NewNumberBox(step.FromChars);
        AutomationProperties.SetName(lengthBox, Loc.GetFrom(Module, "LlmStep_LengthName"));
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
                : Math.Clamp(DictationRewriteSettings.Snap(lengthBox.Value),
                    (int)lengthBox.Minimum, (int)lengthBox.Maximum);
            if (lengthBox.Value != chars)
            {
                _syncingLengths = true;
                try { lengthBox.Value = chars; }
                finally { _syncingLengths = false; }
            }
            if (chars == step.FromChars) return;

            step.FromChars = chars;
            LlmSettingsService.Instance.Save();
            UpdateLengthBounds();
            ApplyState();
        };
        return lengthBox;
    }

    // The block size: on the grain of its size, within the block bounds, and
    // written at the commit of the field, like a length.
    private NumberBox BuildBlockBox(RewriteStep step)
    {
        var blockBox = NewNumberBox(step.BlockChars);
        blockBox.Minimum = DictationRewriteSettings.MinBlockChars;
        blockBox.Maximum = DictationRewriteSettings.MaxBlockChars;
        SetGrain(blockBox, step.BlockChars);
        AutomationProperties.SetName(blockBox, Loc.GetFrom(Module, "LlmStep_BlockName"));

        blockBox.ValueChanged += (_, _) =>
        {
            if (_syncingLengths) return;
            int chars = double.IsNaN(blockBox.Value)
                ? step.BlockChars
                : DictationRewriteSettings.SnapBlock(blockBox.Value);
            if (blockBox.Value != chars)
            {
                _syncingLengths = true;
                try { blockBox.Value = chars; }
                finally { _syncingLengths = false; }
            }
            if (chars == step.BlockChars) return;

            step.BlockChars = chars;
            SetGrain(blockBox, chars);
            LlmSettingsService.Instance.Save();
            RefreshTimeline();
        };
        return blockBox;
    }

    private static NumberBox NewNumberBox(int value) => new()
    {
        Value = value,
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

    // The arrows step by the grain of the current value, finely on a short
    // one and coarsely on a long one.
    private static void SetGrain(NumberBox box, int chars)
    {
        int grain = DictationRewriteSettings.GrainAt(chars);
        box.SmallChange = grain;
        box.LargeChange = grain * 10;
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // Scope: the steps, back to one first step from the beginning; the
    // profiles stay, and so does "Rewrite dictations", the person's choice
    // rather than a value to tune (the rule LlmResetDefaults keeps for the
    // module switch). No confirmation: the steps come back in a few clicks.
    private void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = Dictation.Enabled;
        Settings.Dictation = new DictationRewriteSettings();
        LlmSettingsNormalizer.NormalizeDictation(Settings);
        Settings.Dictation.Enabled = enabled;
        LlmSettingsService.Instance.Save();
        Reload();
    }
}
