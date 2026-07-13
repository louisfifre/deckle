using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

// ── SettingsComposer ─────────────────────────────────────────────────────────
//
// Renders a list of SettingDescriptor into Windows-11 SettingsCard rows inside a
// host Panel. This is the engine of the declarative settings model: a module
// declares WHAT its settings are (typed get/set selectors + a UI kind + a
// localization key + a glyph), and the composer builds the WinUI surface.
//
// Why imperative wiring, not data binding. x:Bind is compile-time generated
// against a known ViewModel type in XAML; it cannot target controls created in
// code. Classic {Binding} would work at runtime but relies on reflection over a
// property-name string — exactly what we avoid (it breaks rename-safety and
// trim/AOT). Instead each descriptor carries typed Func<T>/Action<T> selectors;
// the composer reads the initial value through the getter, writes user edits
// back through the setter, and re-reads the getter whenever the source model
// raises PropertyChanged. No reflection, no expression trees, no magic strings
// beyond the localization key.
//
// Why we subscribe to PropertyChanged even for non-reactive settings. The owning
// ViewModel loads its persisted values AFTER the page (and therefore the composed
// controls) are constructed. Without a subscription the toggles would be frozen
// at their constructor defaults. Re-reading every getter on any PropertyChanged
// keeps the surface in sync with the model — including external mutations such as
// a section "Reset" — and is cheap at the scale of a settings region (tens of
// rows). The model→UI pass is guarded so it cannot bounce back through a
// control's change handler into the setter.
public sealed class SettingsComposer
{
    private readonly Panel _host;
    private readonly INotifyPropertyChanged? _source;

    // Assembly name of the module that declares these settings — the PRI subtree
    // its .resw lives under. Derived from the source VM, which sits in the same
    // module as its resources. Null only when no source is supplied (strings then
    // fall back to the host app's root map).
    private readonly string? _module;

    // One refresher per composed row: re-reads the getter, updates the control,
    // and re-applies reactive enabled/visible state. Invoked on every source
    // PropertyChanged and once at the end of Compose.
    private readonly List<Action> _refreshers = new();

    // Parallel to _refreshers but sparse: only rows (and groups) that carry a
    // resettable Default register here. A dirty-check answers "does this row's
    // value differ from its default?"; its paired reset-action drives the value
    // back to that default through the normal setter. IsDirty()/ResetAll() fold
    // over these — the reset surface for the whole composed region.
    private readonly List<Func<bool>> _dirtyChecks = new();
    private readonly List<Action> _resetActions = new();

    // Guards the model→UI direction so that updating a control's value from the
    // getter does not bounce back through its change handler into the setter
    // (which would re-persist and, for floating-point controls, risk a loop).
    private bool _syncingFromModel;

    // The displayed "dose" captured at Compose, so a group can filter its own
    // advanced children the same way the top level filters advanced settings.
    private bool _showAdvanced = true;

    // Host-supplied factory for the Path kind's picker control. The concrete
    // FolderPickerCard lives in Settings (it needs the Settings window and the
    // module's ETW source), so the floor composer cannot new it up; the app wires
    // this at boot, beside the SettingsHost delegates. Null until wired — a Path
    // setting composed before then throws, surfacing the gap loudly rather than
    // rendering a dead card.
    public static Func<PathArgs, IPathControl>? PathControlFactory { get; set; }

    public SettingsComposer(Panel host, INotifyPropertyChanged? source)
    {
        _host = host;
        _source = source;
        _module = source?.GetType().Assembly.GetName().Name;
    }

    // Builds a card per descriptor into the host, wires change handling, and
    // performs the initial model→UI sync. Advanced-only settings are skipped
    // unless showAdvanced is set (the "displayed dose" lever; the surface is
    // built once, so changing the dose means rebuilding the region).
    public void Compose(IReadOnlyList<SettingDescriptor> settings, bool showAdvanced = true)
    {
        _showAdvanced = showAdvanced;

        foreach (SettingDescriptor s in settings)
        {
            if (s.IsAdvanced && !showAdvanced) continue;
            _host.Children.Add(BuildElement(s));
        }

        if (_source is not null && _refreshers.Count > 0)
            _source.PropertyChanged += OnSourceChanged;

        RefreshAll();
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e) => RefreshAll();

    private void RefreshAll()
    {
        _syncingFromModel = true;
        try
        {
            foreach (Action refresh in _refreshers) refresh();
        }
        finally
        {
            _syncingFromModel = false;
        }

        // The refreshers have just re-evaluated every dirty-check (each reset
        // button's IsEnabled now reflects the model), so the aggregate dirtiness is
        // settled — raise after the loop, not during, so a listener that calls
        // IsDirty() reads the post-refresh truth.
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Reset surface ────────────────────────────────────────────────────────
    //
    // The composed region's collective default-state, for a page-level "Reset all"
    // affordance to gate and act on. Each composed row/group that carries a Default
    // contributed a dirty-check and a reset-action at build time; these fold over
    // them. ResetAll drives each value back through its own setter, exactly the path
    // a per-card reset uses — the model's PropertyChanged then re-syncs the surface
    // (and re-raises DirtyChanged) via RefreshAll, no special re-read needed here.

    // Raised at the END of every RefreshAll, once the dirty-checks have settled —
    // so a "Reset all" button can re-gate its own enabled-state off IsDirty().
    public event EventHandler? DirtyChanged;

    // True when any composed value differs from its default. Cheap at settings
    // scale (tens of rows); recomputed on demand rather than cached, so it cannot
    // drift from the live model.
    public bool IsDirty()
    {
        foreach (Func<bool> dirty in _dirtyChecks)
            if (dirty()) return true;
        return false;
    }

    // Drives every defaulted value back to its default. Each reset-action calls the
    // descriptor's setter, which raises PropertyChanged; RefreshAll then re-syncs
    // the controls and re-evaluates dirtiness — the same round-trip the section
    // resets already rely on, so no manual UI refresh is needed here.
    public void ResetAll()
    {
        foreach (Action reset in _resetActions) reset();
    }

    // Dictated dirtiness equality: doubles compare with the slider/readout tolerance
    // (a difference beyond 1e-9 is a real edit, anything finer is float dust and
    // counts as equal); everything else is plain value-equality. "Dirty" is the
    // negation — NOT equal. Mirrors TunableRow's reset tolerance and the composer's
    // own FormatValue rounding, so what the eye reads as "default" is what this
    // calls clean.
    private static bool DefaultEquals(object? a, object? b)
    {
        if (a is double da && b is double db)
            return Math.Abs(da - db) <= 1e-9;
        return Equals(a, b);
    }

    // Dispatches a descriptor to its element: a Group becomes a SettingsExpander
    // (master toggle + children), a Section the same expander minus the master
    // toggle (header + chevron grouping only), every leaf kind a SettingsCard. All
    // derive from Control, so the host panel holds them side by side. A leaf that
    // carries an Advisory is wrapped so its contextual message hangs beneath the
    // card; a Group or Section never carries one at its own level (the capability
    // lives on leaves only — but a child leaf inside a fold can, see
    // AddChildToExpander).
    private FrameworkElement BuildElement(SettingDescriptor s)
    {
        if (s.Kind == SettingKind.Group) return BuildGroup(s);
        if (s.Kind == SettingKind.Section) return BuildSection(s);

        SettingsCard card = BuildCard(s);
        return s.Advisory is null ? card : WrapWithAdvisory(card, s);
    }

    // ── Inline advisory ──────────────────────────────────────────────────────
    //
    // Wraps a fully-wired card with a sibling InfoBar carrying the descriptor's
    // contextual message — a single channel (no warning/error split: the wording
    // carries the tone), rendered SHARED here rather than per-kind so every leaf
    // gets it the same way. The card keeps every bit of its existing wiring (reset,
    // reactive state, value sync) untouched; the advisory is a frère element added
    // below it, never a replacement.
    //
    // The InfoBar opens with text when Advisory() returns non-null and closes when
    // it returns null, re-evaluated on every refresh exactly like EnabledWhen/
    // VisibleWhen — its refresher is appended to _refreshers like the card's own.
    //
    // VisibleWhen already collapses the CARD (the kind's refresher calls
    // ApplyReactiveState on it), but a collapsed card inside a visible container
    // would leave the InfoBar and an empty slot showing. So this refresher mirrors
    // the card's resolved Visibility onto the whole container — when the setting is
    // hidden, its advisory is hidden with it, holding to "mask, never grey".
    private FrameworkElement WrapWithAdvisory(SettingsCard card, SettingDescriptor s)
    {
        // Severity.Warning is the single tone the channel renders; IsClosable=false
        // because the message is a live state of the setting, not a notification the
        // user dismisses; the title is left empty so the compact bar shows only the
        // caller's one-line message.
        var info = new InfoBar
        {
            Severity = InfoBarSeverity.Warning,
            IsClosable = false,
            IsOpen = false,
        };

        var container = new StackPanel
        {
            Orientation = Orientation.Vertical,
            // A small gap so the bar reads as belonging to the card above it without
            // crowding it — the same spacing the stacked settings sections use.
            Spacing = 4,
        };
        container.Children.Add(card);
        container.Children.Add(info);

        _refreshers.Add(() =>
        {
            string? message = s.Advisory!();
            if (message is null)
            {
                info.IsOpen = false;
            }
            else
            {
                info.Message = message;
                info.IsOpen = true;
            }

            // Mirror the card's own resolved visibility onto the container so the
            // advisory disappears with the card when VisibleWhen collapses it.
            container.Visibility = card.Visibility;
        });

        return container;
    }

    private SettingsCard BuildCard(SettingDescriptor s)
    {
        // Tag carries the LabelKey as the card's runtime identity — the same key that
        // drives its .resw header doubles as the handle a cross-page search walks the
        // visual tree for, to bring THIS card into view. A code-created element gets no
        // x:Name in the page's NameScope, so Tag is the one stable id it can carry.
        var card = new SettingsCard { Header = ResolveHeader(s.LabelKey), Tag = s.LabelKey };

        string? description = ResolveDescription(s.LabelKey);
        if (description is not null) card.Description = description;

        IconElement? icon = BuildIcon(s.Glyph);
        if (icon is not null) card.HeaderIcon = icon;

        switch (s.Kind)
        {
            case SettingKind.Toggle:
                BuildToggle(card, s);
                break;
            case SettingKind.Slider:
                BuildSlider(card, s);
                break;
            case SettingKind.Number:
                BuildNumber(card, s);
                break;
            case SettingKind.Magnitude:
                BuildMagnitude(card, s);
                break;
            case SettingKind.Path:
                BuildPath(card, s);
                break;
            case SettingKind.Choice:
                BuildChoice(card, s);
                break;
            case SettingKind.Text:
                BuildText(card, s);
                break;
            default:
                throw new NotSupportedException(
                    $"SettingKind.{s.Kind} has no composer yet — add it when a setting needs it.");
        }

        return card;
    }

    private void BuildToggle(SettingsCard card, SettingDescriptor s)
    {
        var toggle = new ToggleSwitch { IsOn = AsBool(s.GetValue()) };

        // Subscribe AFTER the initial assignment above so it does not fire Toggled.
        if (s.ConfirmOnEnable is null)
        {
            // The common path: the write-back is synchronous, the model takes the
            // toggle's new state immediately.
            toggle.Toggled += (_, _) =>
            {
                if (_syncingFromModel) return;
                s.SetValue(toggle.IsOn);
            };
        }
        else
        {
            // A consent toggle: the OFF→ON write is HELD until the gate says yes, so
            // the model never transiently flips on. Disabling stays free. The
            // per-toggle confirmInFlight ignores re-entrant flips while the dialog is
            // open (the user clicking the switch again behind a modal), and the revert
            // on refusal is wrapped in _syncingFromModel so flipping the switch back
            // off does not re-enter this handler.
            bool confirmInFlight = false;
            toggle.Toggled += async (_, _) =>
            {
                if (_syncingFromModel) return;
                if (!toggle.IsOn) { s.SetValue(false); return; }
                if (confirmInFlight) return;

                confirmInFlight = true;
                try
                {
                    bool ok = await s.ConfirmOnEnable!(_host.XamlRoot);
                    if (ok)
                    {
                        // Persist ONLY after yes — the first write the model sees is the
                        // confirmed enable.
                        s.SetValue(true);
                    }
                    else
                    {
                        // Refusal: revert the visual to off without persisting, guarded
                        // so the revert does not bounce back through this handler.
                        _syncingFromModel = true;
                        try { toggle.IsOn = false; }
                        finally { _syncingFromModel = false; }
                    }
                }
                finally
                {
                    confirmInFlight = false;
                }
            };
        }

        (FrameworkElement content, Action? updateReset) = WrapWithReset(card, toggle, s);
        card.Content = content;

        _refreshers.Add(() =>
        {
            bool value = AsBool(s.GetValue());
            if (toggle.IsOn != value) toggle.IsOn = value;
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

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
        // Subscribe AFTER the initial assignment above so it does not fire Toggled.
        // The master honours ConfirmOnEnable exactly like a leaf Toggle (BuildToggle):
        // a consent fold holds its OFF→ON write behind the gate so the feature never
        // transiently flips on, and reverts the switch on refusal guarded so the revert
        // does not re-enter this handler. Turning the fold back off is always free.
        if (s.ConfirmOnEnable is null)
        {
            master.Toggled += (_, _) =>
            {
                if (_syncingFromModel) return;
                s.SetValue(master.IsOn);
            };
        }
        else
        {
            bool confirmInFlight = false;
            master.Toggled += async (_, _) =>
            {
                if (_syncingFromModel) return;
                if (!master.IsOn) { s.SetValue(false); return; }
                if (confirmInFlight) return;

                confirmInFlight = true;
                try
                {
                    bool ok = await s.ConfirmOnEnable!(_host.XamlRoot);
                    if (ok)
                    {
                        s.SetValue(true);
                    }
                    else
                    {
                        _syncingFromModel = true;
                        try { master.IsOn = false; }
                        finally { _syncingFromModel = false; }
                    }
                }
                finally
                {
                    confirmInFlight = false;
                }
            };
        }

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

    // Slider over a double, laid out like the RecordingPage "Voice level" rows:
    // a fixed-width Slider with the live value to its right (secondary brush) and
    // an optional unit suffix. Same sync discipline as BuildToggle — set Value
    // before subscribing so the initial assignment does not fire ValueChanged,
    // and guard the write-back so the model→UI refresh below cannot bounce back
    // through ValueChanged into the setter.
    private void BuildSlider(SettingsCard card, SettingDescriptor s)
    {
        // Required by Setting.Slider, so the cast is safe; a wrong-kind args here
        // is a manifest bug, not a runtime input, hence the hard cast.
        var args = (SliderArgs)s.Args!;

        var slider = new Slider
        {
            Minimum = args.Minimum,
            Maximum = args.Maximum,
            StepFrequency = args.StepFrequency,
            Width = 220,
            // The tooltip-on-thumb duplicates the value readout we render
            // ourselves and floats over neighbouring rows — off, as the page does.
            IsThumbToolTipEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
            Value = AsDouble(s.GetValue()),
        };

        // MinWidth keeps the row from reflowing as digits/sign change (e.g.
        // "-55" → "-9"); the secondary brush matches the page's readout.
        var valueText = new TextBlock
        {
            Text = FormatValue(slider.Value, args.StepFrequency),
            MinWidth = 36,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = SecondaryBrush(),
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(slider);
        content.Children.Add(valueText);

        if (!string.IsNullOrEmpty(args.Unit))
        {
            content.Children.Add(new TextBlock
            {
                Text = args.Unit,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = SecondaryBrush(),
            });
        }

        // Subscribe AFTER the initial Value assignment above so it does not fire.
        slider.ValueChanged += (_, e) =>
        {
            // The readout tracks the thumb even during a model-driven refresh, so
            // it stays truthful; only the write-back to the model is suppressed.
            valueText.Text = FormatValue(e.NewValue, args.StepFrequency);
            if (_syncingFromModel) return;
            s.SetValue(e.NewValue);
        };

        (FrameworkElement cardContent, Action? updateReset) = WrapWithReset(card, content, s);
        card.Content = cardContent;

        _refreshers.Add(() =>
        {
            double value = AsDouble(s.GetValue());
            if (slider.Value != value) slider.Value = value;
            // ValueChanged may not fire if the getter equals the current Value
            // (e.g. nothing changed on this PropertyChanged), so refresh the
            // readout unconditionally to stay in sync after Load()/Reset.
            valueText.Text = FormatValue(value, args.StepFrequency);
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // NumberBox over a double, on the card's trailing edge — the same control the
    // hand-authored segmenter and MaxTokens cards use: spin buttons hidden (no
    // flyout pushing the layout, keyboard + wheel only), a fixed MinWidth so the
    // row does not reflow as digits change. Same sync discipline as BuildSlider —
    // seed Value before subscribing so the assignment does not fire ValueChanged,
    // and a NaN-guard so a CLEARED field (NumberBox.Value goes NaN) never reaches
    // the setter, matching the VM's own double.IsNaN guards on the Seg* setters.
    private void BuildNumber(SettingsCard card, SettingDescriptor s)
    {
        // Required by Setting.Number, so the cast is safe; a wrong-kind args here
        // is a manifest bug, not a runtime input, hence the hard cast.
        var args = (NumberArgs)s.Args!;

        var box = new NumberBox
        {
            Minimum = args.Minimum,
            Maximum = args.Maximum,
            SmallChange = args.SmallChange,
            LargeChange = args.LargeChange,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            MinWidth = 100,
            Value = AsDouble(s.GetValue()),
        };

        // Subscribe AFTER the initial Value assignment above so it does not fire.
        box.ValueChanged += (_, _) =>
        {
            if (_syncingFromModel) return;
            // A cleared field surfaces as NaN; swallow it so it never persists, the
            // same guard the VM's Seg* setters apply on the other side.
            if (double.IsNaN(box.Value)) return;
            s.SetValue(box.Value);
        };

        (FrameworkElement content, Action? updateReset) = WrapWithReset(card, box, s);
        card.Content = content;

        _refreshers.Add(() =>
        {
            double value = AsDouble(s.GetValue());
            // Don't write NaN back into the box, and don't fight a value the box
            // already shows (which would also re-fire ValueChanged needlessly).
            if (box.Value != value && !double.IsNaN(value)) box.Value = value;
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // Slider fused with an editable NumberBox over a double — the "magnitude"
    // control: sweep the slider for a fast approximation, or type the NumberBox for
    // an exact figure, both driving one value. The two are kept in lockstep (a
    // slider move writes the box, a box edit moves the thumb) through an internal
    // `coordinating` guard, distinct from _syncingFromModel (which guards the
    // model→UI direction). Unlike BuildSlider the caller gives no StepFrequency: the
    // grain is derived as a "nice" 1-2-5 number from the range (NiceStep), so a
    // magnitude declares only its bounds and unit. The box holds the exact value;
    // the slider thumb approximates it to the nearest detent, which is the point of
    // the pairing — gesture for reach, field for precision. A future wide-range
    // (order-of-magnitude) variant would map the slider logarithmically; no current
    // setting spans that far, so the track stays linear until one does.
    private void BuildMagnitude(SettingsCard card, SettingDescriptor s)
    {
        // Required by Setting.Magnitude, so the cast is safe; a wrong-kind args here
        // is a manifest bug, not a runtime input, hence the hard cast.
        var args = (MagnitudeArgs)s.Args!;

        double step = NiceStep(args.Minimum, args.Maximum);
        int decimals = DecimalsFor(step);

        var slider = new Slider
        {
            Minimum = args.Minimum,
            Maximum = args.Maximum,
            StepFrequency = step,
            Width = 180,
            // The thumb tooltip duplicates the editable field beside it, so off.
            IsThumbToolTipEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
            Value = AsDouble(s.GetValue()),
        };

        var box = new NumberBox
        {
            Minimum = args.Minimum,
            Maximum = args.Maximum,
            SmallChange = step,
            LargeChange = step * 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            MinWidth = 96,
            VerticalAlignment = VerticalAlignment.Center,
            // Fixed precision (fraction digits tracking the nice step) and no
            // grouping separators, so the readout matches the slider's grain and a
            // four-digit figure reads "1200", not "1,200".
            NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter
            {
                IntegerDigits = 1,
                FractionDigits = decimals,
                IsGrouped = false,
            },
            Value = AsDouble(s.GetValue()),
        };

        // Internal lockstep guard: a programmatic Value set on either control fires
        // its ValueChanged, which would write the other and bounce back. This flag
        // makes the second write a no-op, so one user gesture updates both once. It
        // is separate from _syncingFromModel: that guards model→UI (the refresher),
        // this guards slider↔box regardless of direction.
        bool coordinating = false;

        // Subscribe AFTER the initial Value assignments above so they do not fire.
        slider.ValueChanged += (_, e) =>
        {
            if (coordinating) return;
            coordinating = true;
            try { if (box.Value != e.NewValue) box.Value = e.NewValue; }
            finally { coordinating = false; }
            if (_syncingFromModel) return;
            s.SetValue(e.NewValue);
        };

        box.ValueChanged += (_, _) =>
        {
            if (coordinating) return;
            // A cleared field surfaces as NaN; swallow it so it never persists or
            // moves the thumb, the same guard BuildNumber applies.
            if (double.IsNaN(box.Value)) return;
            coordinating = true;
            try { if (slider.Value != box.Value) slider.Value = box.Value; }
            finally { coordinating = false; }
            if (_syncingFromModel) return;
            s.SetValue(box.Value);
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(slider);
        content.Children.Add(box);

        if (!string.IsNullOrEmpty(args.Unit))
        {
            content.Children.Add(new TextBlock
            {
                Text = args.Unit,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = SecondaryBrush(),
            });
        }

        (FrameworkElement cardContent, Action? updateReset) = WrapWithReset(card, content, s);
        card.Content = cardContent;

        _refreshers.Add(() =>
        {
            double value = AsDouble(s.GetValue());
            // Drive both controls from the model under the lockstep guard so neither
            // ValueChanged bounces into the other or back to the setter.
            coordinating = true;
            try
            {
                if (slider.Value != value) slider.Value = value;
                if (box.Value != value && !double.IsNaN(value)) box.Value = value;
            }
            finally { coordinating = false; }
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // The "nice" 1-2-5 step for a magnitude slider, derived from its range so a
    // magnitude declares only bounds. Aims for ~40 detents across the span, then
    // rounds that raw step UP to the nearest 1, 2 or 5 times a power of ten — the
    // classic axis-tick niceness — so the grain reads as a round number (0.05, 1,
    // 50) rather than an arbitrary fraction. The editable field still takes any
    // exact value; this only sets how coarsely the thumb detents.
    private static double NiceStep(double minimum, double maximum)
    {
        double span = Math.Abs(maximum - minimum);
        if (span <= 0) return 1;

        double raw = span / 40.0;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double normalized = raw / magnitude; // in [1, 10)
        double nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

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

    // ── Per-card reset ─────────────────────────────────────────────────────────
    //
    // Wraps a value control with an inline reset affordance when its descriptor
    // carries a Default. Returns the element to assign as the card's Content and an
    // optional updateState closure the row's refresher must call: that closure
    // re-gates the button's IsEnabled off the live dirty-check (active-when-dirty,
    // the Playground model — a reset is offered only when the value actually drifts
    // from its default) and pins its tooltip.
    //
    // When the descriptor has no Default the value control is returned unchanged and
    // updateState is null — no reset chrome for a setting that has no resettable
    // default (a runtime-enumerated value), and the refresher then has nothing extra
    // to do.
    //
    // The button sits LEFT of the value control (least chrome, the value stays where
    // the eye expects it on the trailing edge), reveals on hover/focus exactly like
    // TunableRow, and registers this row's dirty-check and reset-action into the
    // composer's reset surface.
    private (FrameworkElement Content, Action? UpdateState) WrapWithReset(
        SettingsCard card, FrameworkElement valueControl, SettingDescriptor s)
    {
        if (s.Default is null) return (valueControl, null);

        Button reset = BuildResetButton();

        // Reveal on the CARD's hover and the BUTTON's keyboard focus — either keeps
        // it shown, so leaving one while the other holds doesn't hide it early.
        WireReveal(card, reset);

        // active-when-dirty: a difference from the default is what enables the
        // reset, evaluated live so it tracks every model change through the
        // refresher below.
        bool Dirty() => !DefaultEquals(s.GetValue(), s.Default!());
        _dirtyChecks.Add(Dirty);
        _resetActions.Add(() => s.SetValue(s.Default!()));

        reset.Click += (_, _) => s.SetValue(s.Default!());

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            // Tight spacing between the reset glyph and the value control — matches
            // the hand-authored per-card reset rows the composed cards sit beside.
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(reset);
        content.Children.Add(valueControl);

        // The tooltip string is constant, but resolved once here (not per refresh)
        // so the dirty-driven refresher only flips IsEnabled, never re-hits Loc.
        string tooltip = ResolveResetTooltip();
        void UpdateState()
        {
            reset.IsEnabled = Dirty();
            ToolTipService.SetToolTip(reset, tooltip);
        }

        return (content, UpdateState);
    }

    // A subtle 32×32 reset wheel, built imperatively to match the hand-authored
    // per-card reset (WhisperPage's ResetButtonStyle) and the Playground's
    // NewExpander: SubtleButtonStyle from the app root, a Glyphs.Refresh FontIcon,
    // and Opacity 0 at rest so it is invisible until the reveal shows it. The glyph
    // comes from the Glyphs.* C# mirror, the blessed programmatic-FontIcon path.
    private static Button BuildResetButton() => new()
    {
        Style = Application.Current.Resources["SubtleButtonStyle"] as Style,
        Width = 32,
        Height = 32,
        Padding = new Thickness(0),
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = 0,
        Content = new FontIcon { Glyph = Glyphs.Refresh, FontSize = 14 },
    };

    // Instant hover/focus reveal, mirroring TunableRow and the Playground's reset:
    // either the host being pointer-over OR the button holding keyboard focus shows
    // it; both released hides it. No Storyboard — a flat Opacity flip — and the
    // button keeps its layout slot at rest, so the reveal never reflows the row.
    private static void WireReveal(FrameworkElement host, Button button)
    {
        bool pointerOver = false, focused = false;
        void Update() => button.Opacity = pointerOver || focused ? 1 : 0;
        host.PointerEntered += (_, _) => { pointerOver = true; Update(); };
        host.PointerExited += (_, _) => { pointerOver = false; Update(); };
        button.GotFocus += (_, _) => { focused = true; Update(); };
        button.LostFocus += (_, _) => { focused = false; Update(); };
    }

    // The reset tooltip is composer-OWNED and identical for every module, so it
    // resolves from the host app's ROOT map — not the module .resw. (Headers are
    // module strings and resolve per-module; this one is not.) One source for the
    // single string every composed reset shares, whatever module the card lives in.
    private string ResolveResetTooltip()
        => Loc.Get("SettingsComposer_ResetToDefault");

    // Reactive enabled/visible: re-evaluated on every refresh. Null predicates
    // leave the framework defaults (enabled, visible) untouched.
    private static void ApplyReactiveState(Control control, SettingDescriptor s)
    {
        if (s.EnabledWhen is not null) control.IsEnabled = s.EnabledWhen();
        if (s.VisibleWhen is not null)
            control.Visibility = s.VisibleWhen() ? Visibility.Visible : Visibility.Collapsed;
    }

    // Resolves a card's header/description from the OWNING MODULE's .resw — the
    // same entries an x:Uid would read, but at runtime and from the module's own
    // PRI subtree, not the host app's root map. A code-created element gets no
    // x:Uid auto-resolution, and a module's strings are absent from the app root
    // map, so resolving via Loc.Get (root) would silently yield empty headers —
    // the bug this fixes. Segmented x:Uid names map "." → "/" (MRT Core
    // convention), so a card's "<Uid>.Header" entry is read as "<Uid>/Header".
    // The header is mandatory (a miss surfaces a DEBUG marker); the description is
    // optional (a miss leaves the card compact).
    private string ResolveHeader(string labelKey)
        => _module is null ? Loc.Get($"{labelKey}/Header") : Loc.GetFrom(_module, $"{labelKey}/Header");

    private string? ResolveDescription(string labelKey)
        => _module is null ? null : Loc.GetFromOptional(_module, $"{labelKey}/Description");

    // A Choice option's label comes from the same .resw entry its ComboBoxItem's
    // x:Uid would read — "<key>.Content" (segmented "." → "/"), in the module's
    // PRI subtree. Same module-aware resolution as the header; only the suffix
    // differs (a card carries ".Header", a content control carries ".Content").
    private string ResolveOptionLabel(string labelKey)
        => _module is null ? Loc.Get($"{labelKey}/Content") : Loc.GetFrom(_module, $"{labelKey}/Content");

    // The descriptor carries the glyph character itself (from Glyphs.*, the C#
    // mirror of Icons.xaml that exists precisely for programmatic FontIcons), so
    // the icon is built directly with no resource lookup. A code-side
    // Application.Resources[key] is avoided on purpose: it does not walk
    // Theme/Merged dictionaries reliably (see Deckle.Hud HudMessage), and Glyphs
    // is the blessed path here.
    private static IconElement? BuildIcon(string? glyph)
        => string.IsNullOrEmpty(glyph) ? null : new FontIcon { Glyph = glyph };

    private static bool AsBool(object? value) => value is bool b && b;
    private static double AsDouble(object? value) => value is double d ? d : 0d;
    private static string AsString(object? value) => value as string ?? string.Empty;

    // Renders a slider's value with invariant formatting (the readout is a
    // technical number, not a localized one — a "." decimal is intended and
    // stable across cultures). The displayed precision follows StepFrequency:
    // an integer step shows no decimals (-55), a fractional step shows enough to
    // express it (0.05 → "1.05"), so the readout never exposes binary-float dust.
    private static string FormatValue(double value, double stepFrequency)
    {
        int decimals = DecimalsFor(stepFrequency);
        return Math.Round(value, decimals)
            .ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    // Decimal places implied by a step: 1 → 0, 0.05 → 2, 0.001 → 3. Derived by
    // walking the step down by powers of ten until it is whole, capped so a
    // pathological step cannot loop unbounded.
    private static int DecimalsFor(double stepFrequency)
    {
        if (stepFrequency <= 0) return 0;
        int decimals = 0;
        double step = stepFrequency;
        while (decimals < 6 && Math.Abs(step - Math.Round(step)) > 1e-9)
        {
            step *= 10;
            decimals++;
        }
        return decimals;
    }

    // The secondary text brush for the slider's value/unit readouts — the same
    // {ThemeResource TextFillColorSecondaryBrush} the XAML rows use, fetched from
    // the application root where this framework theme key lives. This is a
    // top-level theme brush (unlike the icon brushes the BuildIcon note warns
    // about), so the root-dictionary lookup is the supported path; the other
    // consent dialogs resolve their styles the same way.
    private static Microsoft.UI.Xaml.Media.Brush? SecondaryBrush()
        => Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush;
}
