using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    // Folder path via the curated FolderPickerCard. The card owns the picker and
    // Explorer affordances; the composer only bridges its Path to the descriptor
    // selectors. Same guard rationale as the other kinds: set Path before
    // subscribing PathChanged, and suppress the write-back during a model refresh.
    private void BuildPath(SettingsCard card, SettingDescriptor s)
    {
        var args = (PathArgs)s.Args!;

        // The picker control is module-owned (see PathControlFactory): the composer
        // builds it through the host's factory, which resolves Mode and the deferred
        // DefaultPath (the empty-value fallback computed from AppPaths at compose
        // time), then bridges its Path to the descriptor selectors.
        if (PathControlFactory is null)
            throw new InvalidOperationException(
                "SettingsComposer.PathControlFactory is not wired — the host must " +
                "register the folder-picker control before a Path setting is composed.");

        IPathControl picker = PathControlFactory(args);
        picker.Path = AsString(s.GetValue());

        // The card's content stacks the path readout on its own row below the
        // description, so it hosts vertically rather than on the trailing edge.
        card.ContentAlignment = ContentAlignment.Vertical;

        picker.PathChanged += (_, _) =>
        {
            if (_syncingFromModel) return;
            s.SetValue(picker.Path);
        };

        // Per-card reset when the descriptor carries a Default — the same reset
        // machinery every other kind uses (BuildResetButton + WireReveal + the
        // _dirtyChecks/_resetActions surface + active-when-dirty + tooltip), but
        // laid out by hand instead of through WrapWithReset. WrapWithReset assumes a
        // trailing-edge value control and puts the reset to its LEFT; the Path card
        // hosts its picker VERTICALLY (card.ContentAlignment = Vertical, so the
        // picker sits on its own row below the description). So the reset goes at the
        // TRAILING edge of that row instead — a [picker* | reset] grid mirroring how
        // the hand-authored Whisper ModelsDirectory hangs its reset off the picker's
        // RightContent slot (WhisperPage.xaml). The picker stretches (star column),
        // the reset takes its natural width on the right, revealed on the card's
        // hover/focus like every other reset.
        Action? updateReset = null;
        FrameworkElement content;
        if (s.Default is not null)
        {
            Button reset = BuildResetButton();

            // Reveal on the CARD's hover and the BUTTON's keyboard focus, matching
            // WrapWithReset — either keeps it shown so leaving one while the other
            // holds does not hide it early.
            WireReveal(card, reset);

            // active-when-dirty: the path differing from its Default (typically ""
            // → the empty-means-AppPaths fallback) is what enables the reset,
            // evaluated live through the refresher below. Registered into the shared
            // reset surface so a page-level "Reset all" folds this row in too.
            bool Dirty() => !DefaultEquals(s.GetValue(), s.Default!());
            _dirtyChecks.Add(Dirty);
            _resetActions.Add(() => s.SetValue(s.Default!()));

            // Reset drives the value back to the descriptor's Default through the
            // normal setter; the model's PropertyChanged then re-syncs picker.Path
            // via the refresher, so no manual picker update is needed here.
            reset.Click += (_, _) => s.SetValue(s.Default!());

            // A two-column grid rather than a StackPanel so the picker keeps the full
            // width it had as the card's sole content (star column) and the reset
            // hugs the trailing edge — a horizontal StackPanel would instead shrink
            // the picker to its content width.
            var grid = new Grid { ColumnSpacing = 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(picker.View, 0);
            Grid.SetColumn(reset, 1);
            grid.Children.Add(picker.View);
            grid.Children.Add(reset);
            content = grid;

            // The tooltip string is constant, but resolved once here (not per
            // refresh) so the dirty-driven refresher only flips IsEnabled.
            string tooltip = ResolveResetTooltip();
            updateReset = () =>
            {
                reset.IsEnabled = Dirty();
                ToolTipService.SetToolTip(reset, tooltip);
            };
        }
        else
        {
            // No Default → no reset chrome, the picker is the card's whole content,
            // exactly as before.
            content = picker.View;
        }

        card.Content = content;

        _refreshers.Add(() =>
        {
            string value = AsString(s.GetValue());
            if (picker.Path != value) picker.Path = value;
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // Free-form text via a TextBox, bridged to the descriptor's string selectors.
    // Two shapes from TextArgs: single-line sits on the card's trailing edge with
    // a fixed MinWidth (like the Number box), so the row reads as "label … field";
    // multiline switches AcceptsReturn on, bounds the height, and lays the field on
    // its own row below the description (card.ContentAlignment = Vertical, the same
    // layout BuildPath uses) so a wrapping value has room. Same sync discipline as
    // the other kinds: assign .Text BEFORE subscribing to TextChanged so the seed
    // does not fire it, guard the write-back during a model refresh, route through
    // WrapWithReset for the inline reset, and re-apply reactive state in the
    // refresher. Placeholder and MaxLength are applied when supplied.
    private void BuildText(SettingsCard card, SettingDescriptor s)
    {
        // Required by Setting.Text (which defaults it when omitted), so the cast is
        // safe; a wrong-kind args here is a manifest bug, hence the hard cast.
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
            // A bounded scroller: tall enough for a few lines, capped so a long
            // value scrolls inside the field rather than stretching the card.
            box.MinHeight = 72;
            box.MaxHeight = 160;
            // Hosts on its own row below the description, like the Path picker, so
            // a wrapping value is not crammed onto the trailing edge.
            card.ContentAlignment = ContentAlignment.Vertical;
        }
        else
        {
            // MinWidth matches the Number box so a single-line field sits at a
            // comparable width to the other trailing-edge controls beside it.
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

    // Choice among a small fixed set. The settings-UX doctrine picks the control
    // by the count of options: a RadioButtons group for "a few" (every option laid
    // flat and visible), a ComboBox for "more than a few" (the dropdown that keeps
    // a long set compact). The descriptor's ChoiceArgs.Radio carries that call —
    // set by Setting.Radio, clear by Setting.Choice — and this dispatches on it.
    // Both renderings share the same value semantics: each item's label is resolved
    // from the module's .resw, and the current value is matched against the options
    // by value-equality (IndexOfValue) to pick the selection — so the VM keeps its
    // own value type (the theme's "Dark" string, an int index, an enum) and the
    // composer never assumes an index.
    private void BuildChoice(SettingsCard card, SettingDescriptor s)
    {
        // Required by Setting.Choice/Radio, so the cast is safe; a wrong-kind args
        // here is a manifest bug, not a runtime input, hence the hard cast.
        var args = (ChoiceArgs)s.Args!;

        if (args.Radio) BuildRadio(card, s, args);
        else BuildCombo(card, s, args);
    }

    // The ComboBox rendering — the trailing-edge dropdown for "more than a few"
    // options. Same sync discipline as the other kinds: set SelectedIndex before
    // subscribing so the seed does not fire SelectionChanged, and guard the
    // write-back during a model refresh.
    private void BuildCombo(SettingsCard card, SettingDescriptor s, ChoiceArgs args)
    {
        // MinWidth matches the hand-authored ComboBoxes (GeneralPage theme/overlay)
        // so a composed picker sits at the same width as a bespoke one beside it.
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

    // The RadioButtons rendering — the flat option group for "a few", every choice
    // visible at once. Selection is driven by SelectedIndex exactly like the
    // ComboBox (IndexOfValue → -1 clears the selection when no option matches the
    // persisted value), so the sync discipline is identical: set SelectedIndex
    // before subscribing so the seed does not fire SelectionChanged, guard the
    // write-back during a model refresh, and route through WrapWithReset so a
    // defaulted radio carries the same inline reset as every other kind.
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

        // A RadioButtons group reads best stacked below the header, the Win11 pattern
        // (and the layout the hand-authored corpus-content card used), rather than
        // crammed onto the trailing edge like a single control — so the card hosts
        // vertically, as Path and multiline Text do.
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

    // Index of the option whose value equals the current model value, or -1 (no
    // selection) when none matches — e.g. a persisted value the option set no
    // longer exposes. Object.Equals gives value-equality for the boxed strings
    // and ints the options carry.
    private static int IndexOfValue(ChoiceArgs args, object? value)
    {
        for (int i = 0; i < args.Options.Count; i++)
            if (Equals(args.Options[i].Value, value)) return i;
        return -1;
    }
}

