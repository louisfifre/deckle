using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;
using Deckle.Composition;

namespace Deckle.Playground;

// ─── HudPage — row factories ────────────────────────────────────────────────
//
// Step frequencies are deliberately coarse so dragged values land on round
// numbers, not float artefacts. Each call site picks its step; the
// convention is at the top of HudPage.Expanders.cs.

public sealed partial class HudPage
{
    private TextBlock TuningHelp(string text) => new()
    {
        Text = text,
        Style = (Style)Resources["HudTuningHelpTextStyle"],
    };

    // The "Reset section" button reveals on hover or focus, like TuningCard's
    // section reset: instant, no animation. It hides through Opacity, keeping
    // its slot, so the reveal never reflows the header.
    private StackPanel NewTuningSection(string title, Action resetAction)
    {
        var content = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var headerGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleTb = new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(titleTb, 0);
        headerGrid.Children.Add(titleTb);

        var resetContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        resetContent.Children.Add(new FontIcon { Glyph = Glyphs.Reset, FontSize = 14 });
        resetContent.Children.Add(new TextBlock { Text = "Reset section" });

        var resetBtn = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Content = resetContent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -4, 0, -4),
            Opacity = 0,
        };
        ToolTipService.SetToolTip(resetBtn, "Reset this section to its defaults");
        resetBtn.Click += (_, _) => resetAction();
        Grid.SetColumn(resetBtn, 1);
        headerGrid.Children.Add(resetBtn);

        var section = new Expander
        {
            Header = headerGrid,
            Content = content,
            IsExpanded = !_sectionExpansion.TryGetValue(title, out bool expanded) || expanded,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 4, 0, 4),
        };
        section.RegisterPropertyChangedCallback(Expander.IsExpandedProperty,
            (_, _) => _sectionExpansion[title] = section.IsExpanded);
        section.Loaded += SettleInitialSectionState;

        bool pointerOver = false, focused = false;
        void UpdateReveal() => resetBtn.Opacity = pointerOver || focused ? 1 : 0;
        section.PointerEntered += (_, _) => { pointerOver = true;  UpdateReveal(); };
        section.PointerExited  += (_, _) => { pointerOver = false; UpdateReveal(); };
        resetBtn.GotFocus       += (_, _) => { focused = true;  UpdateReveal(); };
        resetBtn.LostFocus      += (_, _) => { focused = false; UpdateReveal(); };

        TuningStack.Children.Add(section);
        return content;
    }

    private Action<double> AddFloatRow(
        StackPanel stack, string label,
        double min, double max, double step, double value,
        Action<double> setter,
        bool rebuild = false)
    {
        // TuningModel stores floats; promoted to double they show binary
        // noise (0.4f as 0.4000006). Rounding to the step's decimals strips it.
        double displayValue = Math.Round(value, StepDecimals(step));

        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = displayValue,
            StepFrequency = step,
            SmallChange = step,
            LargeChange = step * 10,
            VerticalAlignment = VerticalAlignment.Center,
            IsThumbToolTipEnabled = false,
        };
        var numberBox = new NumberBox
        {
            Value = displayValue,
            Minimum = min, Maximum = max,
            SmallChange = step,
            LargeChange = step * 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 120,
        };

        slider.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                numberBox.Value = e.NewValue;
                setter(e.NewValue);
                UpdateUnsavedBar();
                if (rebuild) RequestRebuild();
            }
            finally { _syncing = false; }
        };
        numberBox.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            if (double.IsNaN(e.NewValue)) return;
            _syncing = true;
            try
            {
                double clamped = Math.Clamp(e.NewValue, min, max);
                slider.Value = clamped;
                setter(clamped);
                UpdateUnsavedBar();
                if (rebuild) RequestRebuild();
            }
            finally { _syncing = false; }
        };

        stack.Children.Add(WrapRow(label, slider, numberBox));
        // Lets the gauge transport move the row in place, keeping its
        // controls, focus and layout.
        return next =>
        {
            bool previous = _syncing;
            _syncing = true;
            try
            {
                slider.Value = numberBox.Value = Math.Clamp(next, min, max);
            }
            finally { _syncing = previous; }
        };
    }

    // The decimals the step itself carries: 0.25 needs two, where its order
    // of magnitude alone (-log10) would give one and show 0.25 as 0.2.
    private static int StepDecimals(double step)
    {
        int digits = 0;
        while (digits < 6)
        {
            double scaled = step * Math.Pow(10, digits);
            if (Math.Abs(scaled - Math.Round(scaled)) < 1e-9) break;
            digits++;
        }
        return digits;
    }

    private void AddIntRow(
        StackPanel stack, string label,
        int min, int max, int value,
        Action<int> setter,
        bool rebuild = false)
    {
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value,
            StepFrequency = 1,
            SmallChange = 1, LargeChange = 10,
            VerticalAlignment = VerticalAlignment.Center,
            IsThumbToolTipEnabled = false,
        };
        var numberBox = new NumberBox
        {
            Value = value,
            Minimum = min, Maximum = max,
            SmallChange = 1, LargeChange = 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 120,
        };

        slider.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                int iv = (int)Math.Round(e.NewValue);
                numberBox.Value = iv;
                setter(iv);
                UpdateUnsavedBar();
                if (rebuild) RequestRebuild();
            }
            finally { _syncing = false; }
        };
        numberBox.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            if (double.IsNaN(e.NewValue)) return;
            _syncing = true;
            try
            {
                int iv = Math.Clamp((int)Math.Round(e.NewValue), min, max);
                slider.Value = iv;
                setter(iv);
                UpdateUnsavedBar();
                if (rebuild) RequestRebuild();
            }
            finally { _syncing = false; }
        };

        stack.Children.Add(WrapRow(label, slider, numberBox));
    }

    private void AddToggleRow(
        StackPanel stack, string label,
        bool value, Action<bool> setter,
        bool rebuild = false)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = value,
            OnContent = "on",
            OffContent = "off",
        };
        toggle.Toggled += (_, _) =>
        {
            setter(toggle.IsOn);
            UpdateUnsavedBar();
            if (rebuild) RequestRebuild();
        };

        var grid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelTb = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(labelTb, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(labelTb);
        grid.Children.Add(toggle);
        stack.Children.Add(grid);
    }

    // A toggle, not a slider: a fractional direction would smuggle in a
    // hidden speed multiplier.
    private void AddDirectionRow(
        StackPanel stack, string label,
        float value, Action<float> setter)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = value >= 0f,
            OnContent = "CW (+1)",
            OffContent = "CCW (-1)",
        };
        toggle.Toggled += (_, _) =>
        {
            setter(toggle.IsOn ? 1f : -1f);
            UpdateUnsavedBar();
            RequestRebuild();
        };

        var grid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelTb = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(labelTb, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(labelTb);
        grid.Children.Add(toggle);
        stack.Children.Add(grid);
    }

    // A two-value choice, so RadioButtons rather than a toggle: both options
    // stay readable by name. The material decides which rows the panel
    // shows, so a choice rebuilds the panel (and through it the preview).
    // The rebuild is queued: it would otherwise tear this control down
    // inside its own SelectionChanged.
    private void AddMaterialRow(StackPanel stack, HudMaterial value, Action<HudMaterial> setter)
    {
        // Items follow HudMaterial's order (Grey, Colour), so the index is
        // the material. Anything but Colour selects Grey, as MaterialOf reads
        // it: the settings file stores the material as a number, and an
        // unknown one paints grey.
        var radios = AddRadioRow(stack, "Material", ["Grey", "Colour"],
            value == HudMaterial.Colour ? 1 : 0);
        radios.SelectionChanged += (_, _) =>
        {
            var next = radios.SelectedIndex == 0 ? HudMaterial.Grey : HudMaterial.Colour;
            // A late selection event for the initial index is not a change.
            if (next == value) return;
            setter(next);
            UpdateUnsavedBar();
            DispatcherQueue.TryEnqueue(RebuildTuningPanel);
        };
    }

    // A labelled choice between a few named options, side by side. The
    // caller subscribes SelectionChanged after this returns, so building the
    // row fires nothing.
    private static RadioButtons AddRadioRow(StackPanel stack, string label, string[] items, int selectedIndex)
    {
        var radios = new RadioButtons { MaxColumns = items.Length, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in items) radios.Items.Add(item);
        radios.SelectedIndex = selectedIndex;
        AutomationProperties.SetName(radios, label);

        var grid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelTb = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(labelTb, 0);
        Grid.SetColumn(radios, 1);
        grid.Children.Add(labelTb);
        grid.Children.Add(radios);
        stack.Children.Add(grid);
        return radios;
    }

    private static Grid WrapRow(string label, Slider slider, NumberBox numberBox)
    {
        var grid = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelTb = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(labelTb, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(numberBox, 2);
        grid.Children.Add(labelTb);
        grid.Children.Add(slider);
        grid.Children.Add(numberBox);
        return grid;
    }
}
