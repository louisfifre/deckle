using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Globalization.NumberFormatting;

namespace Deckle.Playground.Controls;

// One reusable tuning row : a labelled slider + editable NumberBox sharing a
// single Value, with a per-parameter "reset to default" wheel. Mirrors the HUD
// tuning page's Slider+NumberBox composite (same sync guard, same step-aligned
// increments, same Compact spin / overwrite-invalid NumberBox), packaged as a
// control so the Segmentation cards can declare a knob in one line and the
// pairing logic lives in exactly one place.
//
// Bind Value two-way to a view-model property ; Minimum / Maximum / Step / Default
// configure the range and the reset target. The reset wheel is enabled only while
// Value differs from Default.
public sealed partial class TunableRow : UserControl
{
    // Guards the slider ↔ box ↔ Value round-trip against feedback loops : a write
    // to any one of the three would otherwise echo back through the other two.
    private bool _syncing;

    // The controls are configured once, on Loaded, when every range parameter is
    // in place — never per-DP during XAML parse. The range DPs (Minimum / Maximum
    // / Step) are set one at a time by the XBF loader ; reconfiguring on each would
    // run Configure against a half-applied range (e.g. Minimum already 30 while
    // Maximum is still the 1.0 default), a transient inverted / degenerate range a
    // WinRT RangeBase setter rejects with E_INVALIDARG — surfaced by the loader as
    // "Failed to assign to property Minimum". This gate defers all control writes
    // until the parameters are coherent.
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
    // as 0.30000004) is rounded away before display — mirrors the HUD row factory.
    private int Digits => Math.Max(0, (int)Math.Ceiling(-Math.Log10(Step <= 0 ? 1 : Step)));

    // Apply the range and increments to both controls. Only ever called from
    // OnLoaded (and post-load Value pushes), so Minimum / Maximum / Step are all
    // their final, mutually-consistent values — no half-applied transient range.
    private void Configure()
    {
        if (!_loaded || ValueSlider is null) return;
        double step = Step <= 0 ? 1.0 : Step;

        ValueSlider.Minimum       = Minimum;
        ValueSlider.Maximum       = Maximum;
        ValueSlider.StepFrequency = step;
        ValueSlider.SmallChange   = step;
        ValueSlider.LargeChange   = step * 10;

        ValueBox.Minimum     = Minimum;
        ValueBox.Maximum     = Maximum;
        ValueBox.SmallChange = step;
        ValueBox.LargeChange = step * 10;

        // Explicit formatter : without it the NumberBox prints the double's full
        // precision (0.1 shows as 0.1000000015). FractionDigits alone is only a
        // MINIMUM — it doesn't round — so a NumberRounder snapped to the step grid
        // is what actually trims the noise. Together they pin the display to a
        // round number on the step.
        ValueBox.NumberFormatter = new DecimalFormatter
        {
            IntegerDigits  = 1,      // keep a leading "0" before the point
            FractionDigits = Digits, // decimals implied by Step (0 for whole units)
            IsGrouped      = false,  // no thousands separator on the ms values
            NumberRounder  = new IncrementNumberRounder
            {
                Increment         = step,
                RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp,
            },
        };
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
}
