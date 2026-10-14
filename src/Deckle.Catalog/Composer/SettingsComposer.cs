using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

// ── SettingsComposer ─────────────────────────────────────────────────────────
//
// Renders SettingDescriptors into SettingsCard rows inside a host Panel: a module
// declares what its settings are, the composer builds the WinUI surface.
//
// Imperative wiring, not data binding: x:Bind cannot target controls created in
// code, and {Binding} relies on reflection over a property-name string, which
// breaks rename safety and trimming. Each descriptor carries typed selectors
// instead; the composer reads through the getter, writes through the setter and
// re-reads the getter whenever the source model raises PropertyChanged.
//
// Every row re-reads on any PropertyChanged, reactive or not, because the owning
// ViewModel loads its persisted values after the page is constructed; without it
// the controls would stay frozen at their constructor defaults.
public sealed partial class SettingsComposer
{
    private readonly Panel _host;
    private readonly INotifyPropertyChanged? _source;

    // Assembly name of the module that declares these settings — the PRI subtree
    // its .resw lives under. Derived from the source VM, which sits in the same
    // module as its resources. Null only when no source is supplied (strings then
    // fall back to the host app's root map).
    private readonly string? _module;

    // One per composed row; run on every source PropertyChanged and once at the
    // end of Compose.
    private readonly List<Action> _refreshers = new();

    // Only rows and folds that carry a Default register here; IsDirty and
    // ResetAll fold over them.
    private readonly List<Func<bool>> _dirtyChecks = new();
    private readonly List<Action> _resetActions = new();

    // Guards the model→UI direction so that updating a control's value from the
    // getter does not bounce back through its change handler into the setter
    // (which would re-persist and, for floating-point controls, risk a loop).
    private bool _syncingFromModel;

    // The displayed "dose" captured at Compose, so a group can filter its own
    // advanced children the same way the top level filters advanced settings.
    private bool _showAdvanced = true;

    // Host-supplied because the picker controls live in Deckle.Settings (see
    // IPathControl). Null until the app wires it at boot; a Path setting composed
    // before then throws rather than rendering a dead card.
    public static Func<PathArgs, IPathControl>? PathControlFactory { get; set; }

    public SettingsComposer(Panel host, INotifyPropertyChanged? source)
    {
        _host = host;
        _source = source;
        _module = source?.GetType().Assembly.GetName().Name;
    }

    // The surface is built once: changing showAdvanced means rebuilding the region.
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
    // The region's collective default state, for a page-level "Reset all". Each
    // row or fold that carries a Default registered a dirty-check and a
    // reset-action at build time.

    // Raised at the end of every RefreshAll, once the dirty-checks have settled,
    // so a "Reset all" button can re-gate itself off IsDirty().
    public event EventHandler? DirtyChanged;

    // Recomputed on demand, never cached, so it cannot drift from the live model.
    public bool IsDirty()
    {
        foreach (Func<bool> dirty in _dirtyChecks)
            if (dirty()) return true;
        return false;
    }

    // Each reset goes through the descriptor's setter; the model's PropertyChanged
    // then re-syncs the controls through RefreshAll, so no manual refresh is needed.
    public void ResetAll()
    {
        foreach (Action reset in _resetActions) reset();
    }

    // Doubles compare within 1e-9: anything finer is float dust, not an edit, so a
    // value the eye reads as default counts as clean. Everything else is plain
    // value equality.
    private static bool DefaultEquals(object? a, object? b)
    {
        if (a is double da && b is double db)
            return Math.Abs(da - db) <= 1e-9;
        return Equals(a, b);
    }

    // A leaf with an advisory is wrapped so the message hangs beneath the card; a
    // fold child gets its advisory through AddChildToExpander instead.
    private FrameworkElement BuildElement(SettingDescriptor s)
    {
        if (s.Kind == SettingKind.Group) return BuildGroup(s);
        if (s.Kind == SettingKind.Section) return BuildSection(s);
        if (s.Kind == SettingKind.Mode) return BuildMode(s);

        SettingsCard card = BuildCard(s);
        return HasAdvisory(s) ? WrapWithAdvisory(card, s) : card;
    }

    // A kind may speak through the advisory channel too: the Hotkey kind says
    // when a captured key still needs a modifier. That message lives in the
    // control, not in the model, so it is registered here by the kind's builder
    // (BuildCard runs before the wrapping that reads it).
    private readonly Dictionary<SettingDescriptor, Func<string?>> _kindAdvisories = new();

    // One advisory refresher per advised card, so a kind can re-evaluate its own
    // message on a UI change that writes nothing to the model (no PropertyChanged).
    private readonly Dictionary<SettingDescriptor, Action> _advisoryRefreshers = new();

    private bool HasAdvisory(SettingDescriptor s)
        => s.Advisory is not null || _kindAdvisories.ContainsKey(s);

    // The kind's own message first, then the descriptor's; null when both are
    // silent. Two messages share the one bar, one per line.
    private string? ResolveAdvisory(SettingDescriptor s)
    {
        string? kind = _kindAdvisories.TryGetValue(s, out Func<string?>? k) ? k() : null;
        string? own = s.Advisory?.Invoke();
        if (kind is null) return own;
        if (own is null) return kind;
        return kind + "\n" + own;
    }

    private void RefreshAdvisory(SettingDescriptor s)
    {
        if (_advisoryRefreshers.TryGetValue(s, out Action? refresh)) refresh();
    }

    // ── Inline advisory ──────────────────────────────────────────────────────
    //
    // Hangs an InfoBar under a fully wired card, re-evaluated on every refresh
    // like EnabledWhen/VisibleWhen. VisibleWhen collapses only the card, so the
    // refresher mirrors the card's Visibility onto the whole container: a hidden
    // setting hides its advisory too.
    private FrameworkElement WrapWithAdvisory(SettingsCard card, SettingDescriptor s)
    {
        // Not closable: the message is a live state of the setting, not a
        // notification the user dismisses.
        var info = new InfoBar
        {
            Severity = InfoBarSeverity.Warning,
            IsClosable = false,
            IsOpen = false,
        };

        var container = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
        };
        container.Children.Add(card);
        container.Children.Add(info);

        void Refresh()
        {
            string? message = ResolveAdvisory(s);
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
            container.Visibility = card.Visibility;
        }

        _refreshers.Add(Refresh);
        _advisoryRefreshers[s] = Refresh;

        return container;
    }

    private SettingsCard BuildCard(SettingDescriptor s)
    {
        // Tag carries the LabelKey as the card's runtime identity — the same key that
        // drives its .resw header doubles as the handle a cross-page search walks the
        // visual tree for, to bring THIS card into view. A code-created element gets no
        // x:Name in the page's NameScope, so Tag is the one stable id it can carry.
        var card = new SettingsCard { Header = ResolveHeader(s.LabelKey), Tag = s.LabelKey };

        // The description goes through the page-wide Info switch
        // (SettingsDescriptions): the card keeps its text and shows it only
        // while the switch is on.
        SettingsDescriptions.Attach(card, ResolveDescription(s.LabelKey));

        IconElement? icon = BuildIcon(s.Glyph);
        if (icon is not null) card.HeaderIcon = icon;

        switch (s.Kind)
        {
            case SettingKind.Toggle:
                BuildToggle(card, s);
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
            case SettingKind.Editor:
                BuildEditor(card, s);
                break;
            case SettingKind.Hotkey:
                BuildHotkey(card, s);
                break;
            case SettingKind.Curve:
                BuildCurve(card, s);
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

        (FrameworkElement content, Action? updateReset) =
            WrapWithReset(card, toggle, s, () => DescribeToggleDefault(s));
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
