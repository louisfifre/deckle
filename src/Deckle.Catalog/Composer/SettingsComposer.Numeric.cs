using System;
using System.Globalization;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
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

    // Renders a slider's value with invariant formatting. The displayed
    // precision follows StepFrequency so binary-float dust never reaches the UI.
    private static string FormatValue(double value, double stepFrequency)
    {
        int decimals = DecimalsFor(stepFrequency);
        return Math.Round(value, decimals)
            .ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

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
}
