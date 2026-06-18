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

    // Guards the model→UI direction so that updating a control's value from the
    // getter does not bounce back through its change handler into the setter
    // (which would re-persist and, for floating-point controls, risk a loop).
    private bool _syncingFromModel;

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
        foreach (SettingDescriptor s in settings)
        {
            if (s.IsAdvanced && !showAdvanced) continue;
            _host.Children.Add(BuildCard(s));
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
    }

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

        card.Content = toggle;

        _refreshers.Add(() =>
        {
            bool value = AsBool(s.GetValue());
            if (toggle.IsOn != value) toggle.IsOn = value;
            ApplyReactiveState(card, s);
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

        card.Content = content;

        _refreshers.Add(() =>
        {
            double value = AsDouble(s.GetValue());
            if (slider.Value != value) slider.Value = value;
            // ValueChanged may not fire if the getter equals the current Value
            // (e.g. nothing changed on this PropertyChanged), so refresh the
            // readout unconditionally to stay in sync after Load()/Reset.
            valueText.Text = FormatValue(value, args.StepFrequency);
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

        card.Content = combo;

        _refreshers.Add(() =>
        {
            int index = IndexOfValue(args, s.GetValue());
            if (combo.SelectedIndex != index) combo.SelectedIndex = index;
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

    // Reactive enabled/visible: re-evaluated on every refresh. Null predicates
    // leave the framework defaults (enabled, visible) untouched.
    private static void ApplyReactiveState(SettingsCard card, SettingDescriptor s)
    {
        if (s.EnabledWhen is not null) card.IsEnabled = s.EnabledWhen();
        if (s.VisibleWhen is not null)
            card.Visibility = s.VisibleWhen() ? Visibility.Visible : Visibility.Collapsed;
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
