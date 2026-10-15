using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    // ── Path ──────────────────────────────────────────────────────────────────
    //
    // A folder setting is one component wherever it appears, in the form of the
    // General page's "Settings backup" fold (Louis, 2026-10-15): a fold whose
    // header is the setting's title, with its description when it has one, and
    // whose content is the Location line. The line is a SettingsCard the host's
    // control renders (IPathControl): "Location" as its header, the folder's
    // path as its description, and at the trailing edge the reset, then "Choose
    // a folder" and "Open" as words. The path never sits under the fold's
    // header. Standing alone, the setting is that fold (BuildPathExpander). As
    // the child of a fold it is the line alone (BuildPathLine): the parent fold
    // already carries the header, and folds never nest.
    //
    // The control owns the folder dialog, the Explorer affordance and the path
    // display, with its own fallback for an empty value (PathArgs.DefaultPath);
    // the composer only bridges its Path to the descriptor selectors. Path is
    // seeded before PathChanged is subscribed, so the seed does not write back.

    // The standalone form. The fold carries the Tag, the glyph and the reactive
    // state, so a search lands on it and VisibleWhen hides it whole; the line
    // inside carries the reset.
    private SettingsExpander BuildPathExpander(SettingDescriptor s)
    {
        // Tag carries the LabelKey as this fold's runtime identity — see BuildCard.
        var expander = new SettingsExpander { Header = ResolveHeader(s.LabelKey), Tag = s.LabelKey };

        // SettingsExpander hides the description row on null, so a setting
        // without one shows its title alone.
        expander.Description = ResolveDescription(s.LabelKey);

        IconElement? icon = BuildIcon(s.Glyph);
        if (icon is not null) expander.HeaderIcon = icon;

        expander.Items.Add(BuildPathLine(s, expander));
        return expander;
    }

    // The Location line. stateTarget is the element VisibleWhen and EnabledWhen
    // apply to: the standalone fold, or the line itself when it is a fold's
    // child (then it also carries the Tag, as any child card does).
    private SettingsCard BuildPathLine(SettingDescriptor s, Control? stateTarget = null)
    {
        var args = (PathArgs)s.Args!;

        if (PathControlFactory is null)
            throw new InvalidOperationException(
                "SettingsComposer.PathControlFactory is not wired — the host must " +
                "register the folder-picker control before a Path setting is composed.");

        IPathControl picker = PathControlFactory(args);
        SettingsCard line = picker.Line;
        if (stateTarget is null) line.Tag = s.LabelKey;

        picker.Path = AsString(s.GetValue());

        picker.PathChanged += (_, _) =>
        {
            if (_syncingFromModel) return;
            s.SetValue(picker.Path);
        };

        // The reset goes ahead of the actions, [reset | actions] like every
        // card. The actions row is the line's content at rest; it leaves that
        // slot first, since an element belongs to one parent only, and the
        // wrapped row takes it. Without a Default the row comes back unwrapped.
        //
        // An empty default path has no readable form (the control shows its own
        // computed fallback), so the tooltip stays generic for it.
        FrameworkElement actions = picker.Actions;
        line.Content = null;
        (FrameworkElement content, Action? updateReset) = WrapWithReset(line, actions, s,
            () => DefaultValueText.Text(AsString(s.Default!())));
        line.Content = content;

        _refreshers.Add(() =>
        {
            string value = AsString(s.GetValue());
            if (picker.Path != value) picker.Path = value;
            updateReset?.Invoke();
            ApplyReactiveState(stateTarget ?? line, s);
        });

        return line;
    }

    // A card whose content sits under the header, on its own row (a picker, an
    // editor, a curve, a radio group, a multiline field). The Toolkit card's
    // default style right-aligns its content (HorizontalContentAlignment =
    // Right), and its Vertical state keeps that alignment while moving the
    // presenter to the star column: a vertical content then hugs the trailing
    // edge at its own width, which is how the curve sat half-width on the right
    // (2026-10-14). Stretch hands the row's full width to the content.
    private static void HostVertically(SettingsCard card)
    {
        card.ContentAlignment = ContentAlignment.Vertical;
        card.HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }

    // The reset for a vertically hosted content, shared by Editor, Radio and
    // Curve. WrapWithReset puts the reset to the left of a trailing-edge control;
    // a vertical row puts it at the trailing edge instead, in a [content* | reset]
    // grid. extraDirty and extraReset let a kind fold other values into the one
    // wheel (the curve's anchors); a descriptor without a Default still gets the
    // wheel when it has extras to reset.
    private (FrameworkElement Content, Action? UpdateState) WrapWithTrailingReset(
        SettingsCard card, FrameworkElement content, SettingDescriptor s,
        Func<string?>? describeDefault = null,
        Func<bool>? extraDirty = null,
        Action? extraReset = null)
    {
        if (s.Default is null && extraReset is null) return (content, null);

        Button reset = BuildResetButton();

        WireReveal(card, reset);

        bool Dirty() =>
            (s.Default is not null && !DefaultEquals(s.GetValue(), s.Default!()))
            || (extraDirty?.Invoke() ?? false);
        void Reset()
        {
            extraReset?.Invoke();
            if (s.Default is not null) s.SetValue(s.Default!());
        }
        _dirtyChecks.Add(Dirty);
        _resetActions.Add(Reset);

        // The model's PropertyChanged re-syncs the content (the caller's refresher,
        // or the element's own binding), so no manual update is needed here.
        reset.Click += (_, _) => Reset();

        // A two-column grid rather than a StackPanel so the content keeps the full
        // width it had as the card's sole content (star column) and the reset hugs
        // the trailing edge — a horizontal StackPanel would instead shrink the
        // content to its own width.
        var grid = new Grid { ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(content, 0);
        Grid.SetColumn(reset, 1);
        grid.Children.Add(content);
        grid.Children.Add(reset);

        string tooltip = ResolveResetTooltip(describeDefault);
        return (grid, () =>
        {
            reset.IsEnabled = Dirty();
            ToolTipService.SetToolTip(reset, tooltip);
        });
    }

    // A module-owned editor for a value with a shape (a curve, an envelope); the
    // composer owns only the card chrome. Unlike every other kind the refresher
    // pushes no value: the element binds to the model itself, so pushing a value
    // into it would fight that binding.
    private void BuildEditor(SettingsCard card, SettingDescriptor s)
    {
        var args = (EditorArgs)s.Args!;

        HostVertically(card);

        FrameworkElement element = args.Build();
        (FrameworkElement content, Action? updateReset) = WrapWithTrailingReset(card, element, s);
        card.Content = content;

        _refreshers.Add(() =>
        {
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    private void BuildText(SettingsCard card, SettingDescriptor s)
    {
        var args = (TextArgs)s.Args!;

        var box = new TextBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Text = AsString(s.GetValue()),
        };

        if (!string.IsNullOrEmpty(args.Placeholder)) box.PlaceholderText = args.Placeholder;
        // TextBox.MaxLength is 0 = unlimited; only narrow it when a cap is given.
        if (args.MaxLength is int max) box.MaxLength = max;

        if (args.Multiline)
        {
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            // Capped so a long value scrolls inside the field rather than
            // stretching the card.
            box.MinHeight = 72;
            box.MaxHeight = 160;
            // On its own row below the description, so a wrapping value is not
            // crammed onto the trailing edge.
            HostVertically(card);
        }
        else
        {
            box.MinWidth = 200;
        }

        // Subscribe AFTER the initial Text assignment above so it does not fire.
        box.TextChanged += (_, _) =>
        {
            if (_syncingFromModel) return;
            s.SetValue(box.Text);
        };

        (FrameworkElement content, Action? updateReset) = WrapWithReset(card, box, s,
            () => DefaultValueText.Text(AsString(s.Default!())));
        card.Content = content;

        _refreshers.Add(() =>
        {
            string value = AsString(s.GetValue());
            if (box.Text != value) box.Text = value;
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // RadioButtons for a few options, a ComboBox for more: ChoiceArgs.Radio carries
    // that call. Both match the current value against the options by
    // value-equality, so the VM keeps its own value type and the composer never
    // assumes an index.
    private void BuildChoice(SettingsCard card, SettingDescriptor s)
    {
        var args = (ChoiceArgs)s.Args!;

        if (args.Radio) BuildRadio(card, s, args);
        else BuildCombo(card, s, args);
    }

    private void BuildCombo(SettingsCard card, SettingDescriptor s, ChoiceArgs args)
    {
        // MinWidth matches the hand-authored Language ComboBox on WhisperPage, so a
        // composed picker sits at the same width as a bespoke one.
        var combo = new ComboBox { MinWidth = 160 };
        foreach (ChoiceOption option in args.Options)
            combo.Items.Add(new ComboBoxItem { Content = ResolveOptionLabel(option.LabelKey) });
        combo.SelectedIndex = IndexOfValue(args, s.GetValue());

        // Subscribe AFTER the initial SelectedIndex assignment above so it does not fire.
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncingFromModel) return;
            if (combo.SelectedIndex < 0) return;
            s.SetValue(args.Options[combo.SelectedIndex].Value);
        };

        (FrameworkElement content, Action? updateReset) = WrapWithReset(card, combo, s,
            () => DefaultValueText.Choice(args, s.Default!(), ResolveOptionLabel));
        card.Content = content;

        _refreshers.Add(() =>
        {
            int index = IndexOfValue(args, s.GetValue());
            if (combo.SelectedIndex != index) combo.SelectedIndex = index;
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    private void BuildRadio(SettingsCard card, SettingDescriptor s, ChoiceArgs args)
    {
        var radio = new RadioButtons();
        foreach (ChoiceOption option in args.Options)
            radio.Items.Add(ResolveOptionLabel(option.LabelKey));
        radio.SelectedIndex = IndexOfValue(args, s.GetValue());

        // Subscribe AFTER the initial SelectedIndex assignment above so it does not fire.
        radio.SelectionChanged += (_, _) =>
        {
            if (_syncingFromModel) return;
            if (radio.SelectedIndex < 0) return;
            s.SetValue(args.Options[radio.SelectedIndex].Value);
        };

        // A RadioButtons group stacks below the header, the Windows 11 pattern,
        // rather than on the trailing edge. A vertical row takes the trailing
        // reset: WrapWithReset's [reset | radio] row, stretched, would indent the
        // group behind the wheel's slot and show the wheel at the leading edge.
        HostVertically(card);

        (FrameworkElement content, Action? updateReset) = WrapWithTrailingReset(card, radio, s,
            () => DefaultValueText.Choice(args, s.Default!(), ResolveOptionLabel));
        card.Content = content;

        _refreshers.Add(() =>
        {
            int index = IndexOfValue(args, s.GetValue());
            if (radio.SelectedIndex != index) radio.SelectedIndex = index;
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // -1 (no selection) when none matches, such as a persisted value the option set
    // does not expose. Object.Equals gives value-equality for the boxed strings and
    // ints the options carry.
    private static int IndexOfValue(ChoiceArgs args, object? value)
    {
        for (int i = 0; i < args.Options.Count; i++)
            if (Equals(args.Options[i].Value, value)) return i;
        return -1;
    }
}

