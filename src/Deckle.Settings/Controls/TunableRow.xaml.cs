using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Globalization.NumberFormatting;

namespace Deckle.Settings.Controls;

// One reusable tuning row : a labelled slider + editable NumberBox sharing a
// single Value (bind it two-way), with a per-parameter reset to Default.
public sealed partial class TunableRow : UserControl
{
    // Guards the slider ↔ box ↔ Value round-trip against feedback loops : a write
    // to any one of the three would otherwise echo back through the other two.
    private bool _syncing;

    // Control writes wait for Loaded, never per-DP during XAML parse: the XBF
    // loader sets Minimum / Maximum / Step one at a time, and a half-applied range
    // (Minimum already 30, Maximum still the 1.0 default) makes the WinRT RangeBase
    // setter throw E_INVALIDARG, surfaced as "Failed to assign to property Minimum".
    private bool _loaded;

    public TunableRow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        Configure();
        PushValueToControls();
        UpdateResetEnabled();
    }

    // ── Display DPs ──────────────────────────────────────────────────────────

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(TunableRow),
            new PropertyMetadata("", OnLabelChanged));
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(TunableRow),
            new PropertyMetadata("", OnDescriptionChanged));
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(TunableRow),
            new PropertyMetadata("", OnUnitChanged));
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    // ── Range DPs ────────────────────────────────────────────────────────────

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(TunableRow),
            new PropertyMetadata(0.0, OnRangeChanged));
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(TunableRow),
            new PropertyMetadata(1.0, OnRangeChanged));
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(TunableRow),
            new PropertyMetadata(1.0, OnRangeChanged));
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

    public static readonly DependencyProperty DefaultProperty =
        DependencyProperty.Register(nameof(Default), typeof(double), typeof(TunableRow),
            new PropertyMetadata(0.0, OnDefaultChanged));
    public double Default { get => (double)GetValue(DefaultProperty); set => SetValue(DefaultProperty, value); }

    // ── Value DP (bind two-way) ──────────────────────────────────────────────

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(TunableRow),
            new PropertyMetadata(0.0, OnValueChanged));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    // ── DP plumbing ──────────────────────────────────────────────────────────

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TunableRow self) self.LabelText.Text = (string)e.NewValue;
    }

    private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TunableRow self) return;
        string text = (string)e.NewValue;
        self.DescriptionText.Text = text;
        self.DescriptionText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TunableRow self) self.UnitText.Text = (string)e.NewValue;
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TunableRow self) return;
        self.Configure();
        self.PushValueToControls();
    }

    private static void OnDefaultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TunableRow self) self.UpdateResetEnabled();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TunableRow self) return;
        self.PushValueToControls();
        self.UpdateResetEnabled();
    }

    // ── Sync ─────────────────────────────────────────────────────────────────

    // Number of decimals implied by the step, so float-precision noise (0.3 shown
    // as 0.30000004) is rounded away before display.
    private int Digits => Math.Max(0, (int)Math.Ceiling(-Math.Log10(Step <= 0 ? 1 : Step)));

    private void Configure()
    {
        if (!_loaded || ValueSlider is null) return;
        double step = Step <= 0 ? 1.0 : Step;

        // Setting Slider.Minimum while the slider still holds its default 0 makes
        // RangeBase clamp the value UP to the new minimum and raise ValueChanged.
        // Unguarded, OnSliderChanged would write that minimum back into Value and,
        // through the TwoWay bind, the view-model. PushValueToControls reposts the
        // real value.
        bool prev = _syncing;
        _syncing = true;
        try
        {
            ValueSlider.Minimum       = Minimum;
            ValueSlider.Maximum       = Maximum;
            ValueSlider.StepFrequency = step;
            ValueSlider.SmallChange   = step;
            ValueSlider.LargeChange   = step * 10;

            ValueBox.Minimum     = Minimum;
            ValueBox.Maximum     = Maximum;
            ValueBox.SmallChange = step;
            ValueBox.LargeChange = step * 10;

            // Without an explicit formatter the NumberBox prints the double's full
            // precision (0.1 shows as 0.1000000015). FractionDigits is only a
            // MINIMUM and does not round, so the NumberRounder trims the noise.
            //
            // The rounder snaps to the step's DECIMAL GRID (10^-Digits), not to the
            // step itself : IncrementNumberRounder.Increment only accepts an integer
            // or an exact 1/n, and a markup Step like "0.1" arrives float-promoted to
            // 0.10000000149…, which the rounder rejects silently and the box shows
            // blank. A power of ten is always an exact 1/n.
            ValueBox.NumberFormatter = new DecimalFormatter
            {
                IntegerDigits  = 1,      // keep a leading "0" before the point
                FractionDigits = Digits, // decimals implied by Step (0 for whole units)
                IsGrouped      = false,  // no thousands separator on the ms values
                NumberRounder  = new IncrementNumberRounder
                {
                    Increment         = 1.0 / Math.Pow(10, Digits), // 1, 0.1, 0.01, …
                    RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp,
                },
            };
        }
        finally { _syncing = prev; }
    }

    private void PushValueToControls()
    {
        if (!_loaded || ValueSlider is null) return;
        double shown = Math.Round(Value, Digits);
        _syncing = true;
        try
        {
            ValueSlider.Value = shown;
            ValueBox.Value    = shown;
        }
        finally { _syncing = false; }
    }

    private void UpdateResetEnabled()
    {
        if (ResetButton is not null)
            ResetButton.IsEnabled = Math.Abs(Value - Default) > 1e-9;
    }

    private void OnSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        Value = Math.Round(e.NewValue, Digits);
    }

    private void OnNumberBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_syncing || double.IsNaN(e.NewValue)) return;
        Value = Math.Round(Math.Clamp(e.NewValue, Minimum, Maximum), Digits);
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => Value = Default;

    // ── Reveal ───────────────────────────────────────────────────────────────

    private bool _pointerOver;
    private bool _resetFocused;

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) { _pointerOver = true; UpdateReveal(); }
    private void OnPointerExited(object sender, PointerRoutedEventArgs e) { _pointerOver = false; UpdateReveal(); }
    private void OnResetGotFocus(object sender, RoutedEventArgs e) { _resetFocused = true; UpdateReveal(); }
    private void OnResetLostFocus(object sender, RoutedEventArgs e) { _resetFocused = false; UpdateReveal(); }

    private void UpdateReveal() =>
        VisualStateManager.GoToState(this, _pointerOver || _resetFocused ? "Revealed" : "Rest", true);
}
