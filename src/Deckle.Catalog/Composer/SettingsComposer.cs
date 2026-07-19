using System;
using System.Collections.Generic;
using System.ComponentModel;
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
public sealed partial class SettingsComposer
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
        WireToggle(toggle, s);

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

    // Toggle leaves and Group masters share the same write contract. A normal
    // toggle writes immediately; a consent toggle holds only OFF→ON behind the
    // confirmation gate and reverts the visual without touching the model when
    // refused. The control is always seeded before this method is called.
    private void WireToggle(ToggleSwitch toggle, SettingDescriptor s)
    {
        if (s.ConfirmOnEnable is null)
        {
            toggle.Toggled += (_, _) =>
            {
                if (_syncingFromModel) return;
                s.SetValue(toggle.IsOn);
            };
            return;
        }

        bool confirmInFlight = false;
        toggle.Toggled += async (_, _) =>
        {
            if (_syncingFromModel) return;
            if (!toggle.IsOn)
            {
                s.SetValue(false);
                return;
            }
            if (confirmInFlight) return;

            confirmInFlight = true;
            try
            {
                if (await s.ConfirmOnEnable(_host.XamlRoot))
                {
                    s.SetValue(true);
                }
                else
                {
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
}
