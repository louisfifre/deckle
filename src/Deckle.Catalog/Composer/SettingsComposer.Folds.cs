using System;
using System.Collections.Generic;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    // A master toggle that reveals child settings, as a SettingsExpander: the
    // master ToggleSwitch sits in the expander's Content slot, each child is a
    // SettingsCard in its Items. Children are gated on the master by visibility,
    // not enabled state: each child's VisibleWhen is wrapped to also require the
    // master on, because a dependent that does not apply is hidden, never greyed
    // (settings-UX doctrine).
    private SettingsExpander BuildGroup(SettingDescriptor s)
    {
        var args = (GroupArgs)s.Args!;

        // Tag carries the LabelKey as this fold's runtime identity — see BuildCard.
        var expander = new SettingsExpander { Header = ResolveHeader(s.LabelKey), Tag = s.LabelKey };

        expander.Description = ResolveDescription(s.LabelKey);

        IconElement? icon = BuildIcon(s.Glyph);
        if (icon is not null) expander.HeaderIcon = icon;

        var master = new ToggleSwitch { IsOn = AsBool(s.GetValue()) };
        WireToggle(master, s);

        bool MasterOn() => AsBool(s.GetValue());

        // A child hidden because the master is off still counts toward the fold's
        // dirtiness and is still reset: its getter and default are read directly,
        // not through its collapsed card.
        var defaultedChildren = new List<SettingDescriptor>();

        foreach (SettingDescriptor child in args.Children)
        {
            if (child.Kind is SettingKind.Group or SettingKind.Section)
                throw new NotSupportedException(
                    "A Group's children must be leaf settings — folds never nest.");
            if (child.IsAdvanced && !_showAdvanced) continue;

            if (child.Default is not null) defaultedChildren.Add(child);

            Func<bool>? childVisible = child.VisibleWhen;
            SettingDescriptor gated = child with
            {
                VisibleWhen = () => MasterOn() && (childVisible?.Invoke() ?? true),
            };
            AddFoldChild(card => expander.Items.Add(card), gated);
        }

        // The fold's header reset. The children keep their own per-card resets on
        // purpose: one resets a row, this resets the whole fold.
        Action? updateGroupReset = null;
        bool groupHasDefault = s.Default is not null || defaultedChildren.Count > 0;
        if (groupHasDefault)
        {
            Button reset = BuildResetButton();
            WireReveal(expander, reset);

            bool GroupDirty()
            {
                if (s.Default is not null && !DefaultEquals(s.GetValue(), s.Default())) return true;
                foreach (SettingDescriptor child in defaultedChildren)
                    if (!DefaultEquals(child.GetValue(), child.Default!())) return true;
                return false;
            }

            void ResetGroup()
            {
                if (s.Default is not null) s.SetValue(s.Default());
                foreach (SettingDescriptor child in defaultedChildren)
                    child.SetValue(child.Default!());
            }

            _dirtyChecks.Add(GroupDirty);
            _resetActions.Add(ResetGroup);
            reset.Click += (_, _) => ResetGroup();

            // Reset left of the master, mirroring the per-card [reset | value] order.
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            };
            header.Children.Add(reset);
            header.Children.Add(master);
            expander.Content = header;

            string tooltip = ResolveResetTooltip();
            updateGroupReset = () =>
            {
                reset.IsEnabled = GroupDirty();
                ToolTipService.SetToolTip(reset, tooltip);
            };
        }
        else
        {
            expander.Content = master;
        }

        _refreshers.Add(() =>
        {
            bool value = AsBool(s.GetValue());
            if (master.IsOn != value) master.IsOn = value;
            updateGroupReset?.Invoke();
            ApplyReactiveState(expander, s);
        });

        return expander;
    }

    // A fold with no master toggle: the section has no value and gates nothing, so
    // its children keep their own VisibleWhen. The header carries only the
    // optional fold reset.
    private SettingsExpander BuildSection(SettingDescriptor s)
    {
        var args = (SectionArgs)s.Args!;

        // Tag carries the LabelKey as this fold's runtime identity — see BuildCard.
        var expander = new SettingsExpander { Header = ResolveHeader(s.LabelKey), Tag = s.LabelKey };

        expander.Description = ResolveDescription(s.LabelKey);

        IconElement? icon = BuildIcon(s.Glyph);
        if (icon is not null) expander.HeaderIcon = icon;

        var defaultedChildren = new List<SettingDescriptor>();

        foreach (SettingDescriptor child in args.Children)
        {
            if (child.Kind is SettingKind.Group or SettingKind.Section)
                throw new NotSupportedException(
                    "A Section's children must be leaf settings — folds never nest.");
            if (child.IsAdvanced && !_showAdvanced) continue;

            if (child.Default is not null) defaultedChildren.Add(child);

            AddFoldChild(card => expander.Items.Add(card), child);
        }

        Action? updateSectionReset = null;
        if (defaultedChildren.Count > 0)
        {
            Button reset = BuildResetButton();
            WireReveal(expander, reset);

            bool SectionDirty()
            {
                foreach (SettingDescriptor child in defaultedChildren)
                    if (!DefaultEquals(child.GetValue(), child.Default!())) return true;
                return false;
            }

            void ResetSection()
            {
                foreach (SettingDescriptor child in defaultedChildren)
                    child.SetValue(child.Default!());
            }

            _dirtyChecks.Add(SectionDirty);
            _resetActions.Add(ResetSection);
            reset.Click += (_, _) => ResetSection();

            expander.Content = reset;

            string tooltip = ResolveResetTooltip();
            updateSectionReset = () =>
            {
                reset.IsEnabled = SectionDirty();
                ToolTipService.SetToolTip(reset, tooltip);
            };
        }

        // No value to sync: the refresher only re-gates the reset and applies the
        // section's own reactive state.
        if (updateSectionReset is not null || s.EnabledWhen is not null || s.VisibleWhen is not null)
        {
            _refreshers.Add(() =>
            {
                updateSectionReset?.Invoke();
                ApplyReactiveState(expander, s);
            });
        }

        return expander;
    }

    // A choice that decides which settings exist — the mode fold. The same
    // SettingsExpander as a Group, with a ComboBox in the header's trailing-edge
    // slot where the master toggle would be; the children are the settings of
    // the modes, each shown or hidden by its own VisibleWhen (the manifest names
    // the mode there), so the composer adds no gating of its own. One
    // information card closes the fold, shown only when the chosen mode leaves
    // every child hidden, so the fold never opens on nothing. The combo wires
    // like BuildCombo — seed the selection before subscribing, guard the
    // write-back during a model refresh — and the fold's reset covers the choice
    // and every defaulted child, children first so that a preset chosen by the
    // reset is not turned back to Custom by the children's own writes landing
    // after it.
    private SettingsExpander BuildMode(SettingDescriptor s)
    {
        var args = (ModeArgs)s.Args!;
        var choice = new ChoiceArgs(args.Options);

        // Tag carries the LabelKey as this fold's runtime identity — see BuildCard.
        var expander = new SettingsExpander { Header = ResolveHeader(s.LabelKey), Tag = s.LabelKey };

        expander.Description = ResolveDescription(s.LabelKey);

        IconElement? icon = BuildIcon(s.Glyph);
        if (icon is not null) expander.HeaderIcon = icon;

        var combo = new ComboBox { MinWidth = 160, VerticalAlignment = VerticalAlignment.Center };
        foreach (ChoiceOption option in args.Options)
            combo.Items.Add(new ComboBoxItem { Content = ResolveOptionLabel(option.LabelKey) });
        combo.SelectedIndex = IndexOfValue(choice, s.GetValue());

        // Subscribe AFTER the initial SelectedIndex assignment above so it does not fire.
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncingFromModel) return;
            if (combo.SelectedIndex < 0) return;
            s.SetValue(args.Options[combo.SelectedIndex].Value);
        };

        var defaultedChildren = new List<SettingDescriptor>();
        var childCards = new List<SettingsCard>();

        foreach (SettingDescriptor child in args.Children)
        {
            if (child.Kind is SettingKind.Group or SettingKind.Section or SettingKind.Mode)
                throw new NotSupportedException(
                    "A Mode's children must be leaf settings — folds never nest.");
            if (child.IsAdvanced && !_showAdvanced) continue;

            if (child.Default is not null) defaultedChildren.Add(child);

            // The child keeps its own VisibleWhen, which names its mode. The
            // first card placed is the child's own; an advisory's note row
            // follows it and is not a child card.
            bool first = true;
            AddFoldChild(card =>
            {
                expander.Items.Add(card);
                if (first) childCards.Add(card);
                first = false;
            }, child);
        }

        // The information card: no header, one line of secondary text, no frame
        // of its own beyond the fold's, like the advisory note rows.
        var nothingToSet = new SettingsCard
        {
            Description = Loc.Get("SettingsComposer_ModeNothingToSet"),
            Visibility = Visibility.Collapsed,
        };
        expander.Items.Add(nothingToSet);

        Action? updateReset = null;
        bool hasReset = s.Default is not null || defaultedChildren.Count > 0;
        if (hasReset)
        {
            Button reset = BuildResetButton();
            WireReveal(expander, reset);

            bool ModeDirty()
            {
                if (s.Default is not null && !DefaultEquals(s.GetValue(), s.Default())) return true;
                foreach (SettingDescriptor child in defaultedChildren)
                    if (!DefaultEquals(child.GetValue(), child.Default!())) return true;
                return false;
            }

            void ResetMode()
            {
                foreach (SettingDescriptor child in defaultedChildren)
                    child.SetValue(child.Default!());
                if (s.Default is not null) s.SetValue(s.Default());
            }

            _dirtyChecks.Add(ModeDirty);
            _resetActions.Add(ResetMode);
            reset.Click += (_, _) => ResetMode();

            // [reset | choice] in the trailing-edge slot, mirroring the group's
            // [reset | master] order.
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            };
            header.Children.Add(reset);
            header.Children.Add(combo);
            expander.Content = header;

            // The tooltip names the default mode when the choice has one; a fold
            // whose reset only covers its children keeps the generic wording.
            string tooltip = ResolveResetTooltip(
                () => s.Default is null ? null : DefaultValueText.Choice(choice, s.Default(), ResolveOptionLabel));
            updateReset = () =>
            {
                reset.IsEnabled = ModeDirty();
                ToolTipService.SetToolTip(reset, tooltip);
            };
        }
        else
        {
            expander.Content = combo;
        }

        // Registered after the children's refreshers, so their visibility is
        // settled when this decides whether the information card shows.
        _refreshers.Add(() =>
        {
            int index = IndexOfValue(choice, s.GetValue());
            if (combo.SelectedIndex != index) combo.SelectedIndex = index;

            bool anyVisible = false;
            foreach (SettingsCard card in childCards)
                if (card.Visibility == Visibility.Visible) { anyVisible = true; break; }
            nothingToSet.Visibility = anyVisible ? Visibility.Collapsed : Visibility.Visible;

            updateReset?.Invoke();
            ApplyReactiveState(expander, s);
        });

        return expander;
    }

    // ── Child of a fold ────────────────────────────────────────────────────────
    //
    // Shared by the three folds and by Compose(inFold: true); the caller
    // composes any master gating into the child's VisibleWhen first, and place
    // puts each card where the fold holds it (an expander's Items, or a panel
    // the page empties into a hand-authored fold). SettingsExpander.Items
    // accepts only SettingsCard children, not the StackPanel{card, InfoBar}
    // WrapWithAdvisory returns, so a fold child's advisory goes in as a second
    // card: a borderless SettingsCard hosting the InfoBar, which hides with the
    // advised card. A Path child is its Location line (BuildCard).
    private void AddFoldChild(Action<SettingsCard> place, SettingDescriptor child)
    {
        SettingsCard card = BuildCard(child);
        place(card);

        if (!HasAdvisory(child)) return;

        // Not closable: the message is a live state of the setting, not a
        // notification the user dismisses.
        var info = new InfoBar
        {
            Severity = InfoBarSeverity.Warning,
            IsClosable = false,
            IsOpen = false,
        };

        var noteRow = new SettingsCard
        {
            Content = info,
            Background = null,
            BorderThickness = new Thickness(0),
        };
        place(noteRow);

        void Refresh()
        {
            string? message = ResolveAdvisory(child);
            if (message is null)
            {
                info.IsOpen = false;
            }
            else
            {
                info.Message = message;
                info.IsOpen = true;
            }

            // The card's own refresher was registered before this one, so its
            // Visibility is already current here.
            noteRow.Visibility = card.Visibility;
        }

        _refreshers.Add(Refresh);
        _advisoryRefreshers[child] = Refresh;
    }
}

