using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.WinUI.Controls;
using Deckle.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Settings;

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
    // (master toggle + children), every leaf kind a SettingsCard. Both derive
    // from Control, so the host panel holds them side by side.
    private FrameworkElement BuildElement(SettingDescriptor s)
        => s.Kind == SettingKind.Group ? BuildGroup(s) : BuildCard(s);

    private SettingsCard BuildCard(SettingDescriptor s)
    {
        var card = new SettingsCard { Header = ResolveHeader(s.LabelKey) };

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
            case SettingKind.Path:
                BuildPath(card, s);
                break;
            case SettingKind.Choice:
                BuildChoice(card, s);
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
        toggle.Toggled += (_, _) =>
        {
            if (_syncingFromModel) return;
            s.SetValue(toggle.IsOn);
        };

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

        var expander = new SettingsExpander { Header = ResolveHeader(s.LabelKey) };

        string? description = ResolveDescription(s.LabelKey);
        if (description is not null) expander.Description = description;

        IconElement? icon = BuildIcon(s.Glyph);
        if (icon is not null) expander.HeaderIcon = icon;

        var master = new ToggleSwitch { IsOn = AsBool(s.GetValue()) };
        // Subscribe AFTER the initial assignment above so it does not fire Toggled.
        master.Toggled += (_, _) =>
        {
            if (_syncingFromModel) return;
            s.SetValue(master.IsOn);
        };

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
            if (child.Kind == SettingKind.Group)
                throw new NotSupportedException(
                    "A Group's children must be leaf settings — folds never nest.");
            if (child.IsAdvanced && !_showAdvanced) continue;

            if (child.Default is not null) defaultedChildren.Add(child);

            // Compose the master into the child's own VisibleWhen so the child is
            // hidden while the master is off (and stays hidden when its own
            // predicate also collapses it).
            Func<bool>? childVisible = child.VisibleWhen;
            SettingDescriptor gated = child with
            {
                VisibleWhen = () => MasterOn() && (childVisible?.Invoke() ?? true),
            };
            expander.Items.Add(BuildCard(gated));
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

    // Folder path via the curated FolderPickerCard. The card owns the picker and
    // Explorer affordances; the composer only bridges its Path to the descriptor
    // selectors. Same guard rationale as the other kinds: set Path before
    // subscribing PathChanged, and suppress the write-back during a model refresh.
    private void BuildPath(SettingsCard card, SettingDescriptor s)
    {
        var args = (PathArgs)s.Args!;

        var picker = new FolderPickerCard
        {
            Mode = args.Mode,
            // Deferred lookup invoked once here: the empty-value fallback display
            // and the Open target both resolve from AppPaths at compose time.
            DefaultPath = args.DefaultPath?.Invoke() ?? string.Empty,
            Path = AsString(s.GetValue()),
        };

        // The card's content stacks the path readout on its own row below the
        // description, so it hosts vertically rather than on the trailing edge.
        card.ContentAlignment = ContentAlignment.Vertical;

        picker.PathChanged += (_, _) =>
        {
            if (_syncingFromModel) return;
            s.SetValue(picker.Path);
        };

        card.Content = picker;

        _refreshers.Add(() =>
        {
            string value = AsString(s.GetValue());
            if (picker.Path != value) picker.Path = value;
            ApplyReactiveState(card, s);
        });
    }

    // Choice among a small fixed set, rendered as a ComboBox on the card's
    // trailing edge — the control the settings-UX doctrine picks for "more than a
    // few" mutually-exclusive options (a radio group, for "a few", is a future
    // style added when a clean radio site needs it). Each ComboBoxItem's label is
    // resolved from the module's .resw, and the current value is matched against
    // the options by value-equality to pick the selection — so the VM keeps its
    // own value type (the theme's "Dark" string, an int index, an enum) and the
    // composer never assumes an index. Same sync discipline as the other kinds:
    // set SelectedIndex before subscribing so the seed does not fire
    // SelectionChanged, and guard the write-back during a model refresh.
    private void BuildChoice(SettingsCard card, SettingDescriptor s)
    {
        // Required by Setting.Choice, so the cast is safe; a wrong-kind args here
        // is a manifest bug, not a runtime input, hence the hard cast.
        var args = (ChoiceArgs)s.Args!;

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

    // The reset tooltip from the OWNING MODULE's .resw (module-aware like the header
    // resolution, falling back to the root map when no module is supplied).
    private string ResolveResetTooltip()
        => _module is null
            ? Loc.Get("SettingsComposer_ResetToDefault")
            : Loc.GetFrom(_module, "SettingsComposer_ResetToDefault");

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
