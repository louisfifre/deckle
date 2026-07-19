using System;
using System.Collections.Generic;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    // A master toggle that reveals child settings, rendered as the Win11
    // SettingsExpander the hand-authored overlay/backup groups already use: the
    // group's header carries the master ToggleSwitch (in the expander's Content
    // slot, its trailing-edge control), and each child is a SettingsCard in the
    // expander's Items. The master toggle wires exactly like BuildToggle — set
    // IsOn before subscribing so the seed does not fire, guard the write-back
    // during a model refresh.
    //
    // Children are gated on the master by VISIBILITY, not enabled-state: each is
    // composed with its VisibleWhen wrapped to also require the master on, so the
    // row is HIDDEN (collapsed) while the feature is off and reappears on the
    // master's PropertyChanged via the shared RefreshAll. Microsoft-first
    // dependency gating masks, never greys — a dependent that does not apply is
    // hidden, not disabled (settings-UX doctrine: "hidden entirely, never greyed
    // out"). Reusing BuildCard means a child is wired identically to a top-level
    // card — same selectors, same sync discipline, same reactive state.
    private SettingsExpander BuildGroup(SettingDescriptor s)
    {
        var args = (GroupArgs)s.Args!;

        // Tag carries the LabelKey as this fold's runtime identity — see BuildCard.
        var expander = new SettingsExpander { Header = ResolveHeader(s.LabelKey), Tag = s.LabelKey };

        string? description = ResolveDescription(s.LabelKey);
        if (description is not null) expander.Description = description;

        IconElement? icon = BuildIcon(s.Glyph);
        if (icon is not null) expander.HeaderIcon = icon;

        var master = new ToggleSwitch { IsOn = AsBool(s.GetValue()) };
        WireToggle(master, s);

        // The master's current state, read live so child gating tracks it on
        // every RefreshAll (a master toggle raises PropertyChanged, which refreshes
        // all rows including these children).
        bool MasterOn() => AsBool(s.GetValue());

        // Children that carry a resettable default — collected so the group-header
        // reset can drive the WHOLE fold back, master plus every defaulted child,
        // including one currently hidden because the master is off (it still counts
        // toward dirtiness and is still reset). The original descriptor is captured,
        // not the master-gated copy, but the `with` copies Default, so either would
        // do — the original keeps the intent plain.
        var defaultedChildren = new List<SettingDescriptor>();

        foreach (SettingDescriptor child in args.Children)
        {
            if (child.Kind is SettingKind.Group or SettingKind.Section)
                throw new NotSupportedException(
                    "A Group's children must be leaf settings — folds never nest.");
            if (child.IsAdvanced && !_showAdvanced) continue;

            if (child.Default is not null) defaultedChildren.Add(child);

            // Compose the master into the child's own VisibleWhen so the child is
            // hidden while the master is off (and stays hidden when its own
            // predicate also collapses it). This master-gated copy is what the
            // shared helper renders — the gating is composed in BEFORE handing off,
            // so the helper stays agnostic of whether a master exists.
            Func<bool>? childVisible = child.VisibleWhen;
            SettingDescriptor gated = child with
            {
                VisibleWhen = () => MasterOn() && (childVisible?.Invoke() ?? true),
            };
            AddChildToExpander(expander, gated);
        }

        // The group-header reset, beside the master toggle, when the fold has
        // anything resettable — the master itself or any child. It is the
        // section-style "reset the whole fold"; the per-child cards keep their own
        // inline resets (built by BuildCard above), which is desirable, not
        // redundant: one resets a single row, this resets the section.
        Action? updateGroupReset = null;
        bool groupHasDefault = s.Default is not null || defaultedChildren.Count > 0;
        if (groupHasDefault)
        {
            Button reset = BuildResetButton();
            // Reveal on the EXPANDER's hover and the BUTTON's focus, like a per-card
            // reset reveals on its card.
            WireReveal(expander, reset);

            // Group dirty = master-dirty OR any-child-dirty. A child hidden because
            // the master is off still counts (its getter/default are read directly,
            // not through its collapsed card).
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

            // [reset | master] in the expander's trailing-edge Content slot — the
            // master stays where a fold's master toggle is expected, the reset to
            // its left, mirroring the per-card [reset | value] order.
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

    // A header-and-chevron grouping with NO master toggle — "BuildGroup minus the
    // master". The expander's header carries only the optional section-level reset,
    // never a ToggleSwitch: the section has no value of its own and gates nothing,
    // so its children stand on their OWN VisibleWhen with no master to compose in.
    // Otherwise it mirrors BuildGroup exactly — header/description/icon resolved the
    // same way, each child added through the shared AddChildToExpander helper, and
    // the same whole-fold reset folded over any defaulted child.
    private SettingsExpander BuildSection(SettingDescriptor s)
    {
        var args = (SectionArgs)s.Args!;

        // Tag carries the LabelKey as this fold's runtime identity — see BuildCard.
        var expander = new SettingsExpander { Header = ResolveHeader(s.LabelKey), Tag = s.LabelKey };

        string? description = ResolveDescription(s.LabelKey);
        if (description is not null) expander.Description = description;

        IconElement? icon = BuildIcon(s.Glyph);
        if (icon is not null) expander.HeaderIcon = icon;

        // Children that carry a resettable default — collected so the section-header
        // reset can drive the whole fold back, exactly as BuildGroup does. There is
        // no master to include here (the section has no value of its own), so the
        // fold's dirtiness is purely the children's.
        var defaultedChildren = new List<SettingDescriptor>();

        foreach (SettingDescriptor child in args.Children)
        {
            if (child.Kind is SettingKind.Group or SettingKind.Section)
                throw new NotSupportedException(
                    "A Section's children must be leaf settings — folds never nest.");
            if (child.IsAdvanced && !_showAdvanced) continue;

            if (child.Default is not null) defaultedChildren.Add(child);

            // No master to compose in — the child keeps its own VisibleWhen as-is,
            // handed straight to the shared helper.
            AddChildToExpander(expander, child);
        }

        // The section-header reset, in the expander's trailing-edge Content slot,
        // when any child carries a default. Identical to BuildGroup's group reset
        // minus the master term: the fold is dirty when any defaulted child differs
        // from its default, and resetting drives each child back through its setter.
        Action? updateSectionReset = null;
        if (defaultedChildren.Count > 0)
        {
            Button reset = BuildResetButton();
            // Reveal on the EXPANDER's hover and the BUTTON's focus, like BuildGroup.
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

            // The reset alone in the trailing-edge slot — no master toggle beside
            // it, the one difference from BuildGroup's [reset | master] header.
            expander.Content = reset;

            string tooltip = ResolveResetTooltip();
            updateSectionReset = () =>
            {
                reset.IsEnabled = SectionDirty();
                ToolTipService.SetToolTip(reset, tooltip);
            };
        }

        // The section has no value to sync, but it still needs a refresher to drive
        // the reset's IsEnabled off the live dirty-check and to apply its own
        // reactive state (a section gated by VisibleWhen collapses the whole fold).
        // Registered only when there is something to do — a section with no reset
        // and no reactive state contributes no refresher, like a defaultless card.
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

    // ── Child of a fold ────────────────────────────────────────────────────────
    //
    // Adds a fold child to a SettingsExpander's Items, shared by BuildGroup and
    // BuildSection so the two render children identically. The caller composes any
    // master gating into the child's VisibleWhen BEFORE calling this — the helper is
    // agnostic of whether a master exists.
    //
    // The advisory subtlety. SettingsExpander.Items accepts only SettingsCard
    // children (NOT the StackPanel{card, InfoBar} that WrapWithAdvisory returns for a
    // top-level card — see FolderPickerCard's note). So a child carrying an Advisory
    // cannot use that wrapping path inside a fold. Instead the child's card goes in
    // as usual, and the advisory is added as a SECOND Items entry: a borderless
    // SettingsCard whose Content is the Warning InfoBar, with Background and
    // BorderThickness cleared so it reads as a flat contextual note row inside the
    // fold rather than a second framed card. The InfoBar is wired exactly like
    // WrapWithAdvisory (message = Advisory(), IsOpen toggles on null), and the note
    // row mirrors the advised card's resolved Visibility so it hides with the card
    // when VisibleWhen collapses it ("mask, never grey"). This closes the gap where
    // a Group child silently could not carry an advisory.
    private void AddChildToExpander(SettingsExpander expander, SettingDescriptor child)
    {
        SettingsCard card = BuildCard(child);
        expander.Items.Add(card);

        if (child.Advisory is null) return;

        // Severity.Warning is the single tone the channel renders; IsClosable=false
        // because the message is a live state of the setting, not a notification the
        // user dismisses; the title is left empty so the bar shows only the message.
        var info = new InfoBar
        {
            Severity = InfoBarSeverity.Warning,
            IsClosable = false,
            IsOpen = false,
        };

        // A borderless, transparent host card: Items demands a SettingsCard, but
        // clearing its frame makes this read as a flat note row hanging under the
        // advised card, not a second card. No header — the InfoBar carries the whole
        // message.
        var noteRow = new SettingsCard
        {
            Content = info,
            Background = null,
            BorderThickness = new Thickness(0),
        };
        expander.Items.Add(noteRow);

        _refreshers.Add(() =>
        {
            string? message = child.Advisory!();
            if (message is null)
            {
                info.IsOpen = false;
            }
            else
            {
                info.Message = message;
                info.IsOpen = true;
            }

            // Mirror the advised card's own resolved visibility onto the note row so
            // the advisory disappears with the card when VisibleWhen collapses it
            // (the card's kind-refresher has already applied that visibility above).
            noteRow.Visibility = card.Visibility;
        });
    }
}

