using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    // The picker control owns the folder dialog and Explorer affordances; the
    // composer only bridges its Path to the descriptor selectors. Path is set
    // before PathChanged is subscribed so the seed does not write back.
    private void BuildPath(SettingsCard card, SettingDescriptor s)
    {
        var args = (PathArgs)s.Args!;

        if (PathControlFactory is null)
            throw new InvalidOperationException(
                "SettingsComposer.PathControlFactory is not wired — the host must " +
                "register the folder-picker control before a Path setting is composed.");

        IPathControl picker = PathControlFactory(args);
        picker.Path = AsString(s.GetValue());

        // The path readout takes its own row below the description.
        card.ContentAlignment = ContentAlignment.Vertical;

        picker.PathChanged += (_, _) =>
        {
            if (_syncingFromModel) return;
            s.SetValue(picker.Path);
        };

        (FrameworkElement content, Action? updateReset) = WrapWithTrailingReset(card, picker.View, s);
        card.Content = content;

        _refreshers.Add(() =>
        {
            string value = AsString(s.GetValue());
            if (picker.Path != value) picker.Path = value;
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // Per-card reset for vertically hosted content (Path, Editor). WrapWithReset
    // puts the reset left of a trailing-edge control; a vertical row puts it at the
    // trailing edge instead. With no Default the content comes back unchanged.
    private (FrameworkElement Content, Action? UpdateState) WrapWithTrailingReset(
        SettingsCard card, FrameworkElement content, SettingDescriptor s)
    {
        if (s.Default is null) return (content, null);

        Button reset = BuildResetButton();

        WireReveal(card, reset);

        bool Dirty() => !DefaultEquals(s.GetValue(), s.Default!());
        _dirtyChecks.Add(Dirty);
        _resetActions.Add(() => s.SetValue(s.Default!()));

        reset.Click += (_, _) => s.SetValue(s.Default!());

        // A grid, not a horizontal StackPanel: the star column keeps the content at
        // full width, where a StackPanel would shrink it to its own width.
        var grid = new Grid { ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(content, 0);
        Grid.SetColumn(reset, 1);
        grid.Children.Add(content);
        grid.Children.Add(reset);

        string tooltip = ResolveResetTooltip();
        return (grid, () =>
        {
            reset.IsEnabled = Dirty();
            ToolTipService.SetToolTip(reset, tooltip);
        });
    }

    // The composer owns only the card chrome: vertical hosting (an editor needs
    // the row's width), the trailing reset and the reactive state. Unlike every
    // other kind the refresher pushes no value: the element binds to the model
    // itself, and pushing a value into it would fight that binding.
    private void BuildEditor(SettingsCard card, SettingDescriptor s)
    {
        var args = (EditorArgs)s.Args!;

        card.ContentAlignment = ContentAlignment.Vertical;

        FrameworkElement element = args.Build();
        (FrameworkElement content, Action? updateReset) = WrapWithTrailingReset(card, element, s);
        card.Content = content;

        _refreshers.Add(() =>
        {
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // Single-line sits on the card's trailing edge; multiline takes its own row
    // below the description so a wrapping value has room.
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
            card.ContentAlignment = ContentAlignment.Vertical;
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

        (FrameworkElement content, Action? updateReset) = WrapWithReset(card, box, s);
        card.Content = content;

        _refreshers.Add(() =>
        {
            string value = AsString(s.GetValue());
            if (box.Text != value) box.Text = value;
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // The current value is matched against the options by equality (IndexOfValue),
    // so the VM keeps its own value type and the composer never assumes an index.
    private void BuildChoice(SettingsCard card, SettingDescriptor s)
    {
        var args = (ChoiceArgs)s.Args!;

        if (args.Radio) BuildRadio(card, s, args);
        else BuildCombo(card, s, args);
    }

    private void BuildCombo(SettingsCard card, SettingDescriptor s, ChoiceArgs args)
    {
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

        (FrameworkElement content, Action? updateReset) = WrapWithReset(card, combo, s);
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
        // rather than on the trailing edge.
        card.ContentAlignment = ContentAlignment.Vertical;

        (FrameworkElement content, Action? updateReset) = WrapWithReset(card, radio, s);
        card.Content = content;

        _refreshers.Add(() =>
        {
            int index = IndexOfValue(args, s.GetValue());
            if (radio.SelectedIndex != index) radio.SelectedIndex = index;
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // -1 (no selection) when no option matches, e.g. a persisted value the option
    // set does not expose. Object.Equals gives value equality for boxed values.
    private static int IndexOfValue(ChoiceArgs args, object? value)
    {
        for (int i = 0; i < args.Options.Count; i++)
            if (Equals(args.Options[i].Value, value)) return i;
        return -1;
    }
}

