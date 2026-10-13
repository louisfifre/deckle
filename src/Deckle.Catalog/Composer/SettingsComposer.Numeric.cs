using System;
using System.Globalization;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

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
            // Same dust as BuildMagnitude: store the value the readout shows.
            s.SetValue(MagnitudeGrain.Snap(e.NewValue, MagnitudeGrain.DecimalsFor(args.StepFrequency)));
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

    // The one numeric block: an editable NumberBox over a double, with a Slider
    // beside it when the value earns a gesture (MagnitudeArgs.Slider). Sweep the
    // slider for a fast approximation, type the box or nudge its arrows for an
    // exact figure, both driving one value. The two are kept in lockstep (a slider
    // move writes the box, a box edit moves the thumb) through an internal
    // `coordinating` guard, distinct from _syncingFromModel (which guards the
    // model→UI direction). The caller gives no step: the grain is derived as a
    // "nice" 1-2-5 number from the range (MagnitudeGrain.StepFor), floored at one
    // for a count (MagnitudeArgs.Integer), so a magnitude declares only its bounds,
    // unit and shape. The box holds the exact value; the slider thumb approximates
    // it to the nearest detent, which is the point of the pairing — gesture for
    // reach, field for precision.
    //
    // Writes follow the doctrine's gesture rule: once, when the gesture ends. The
    // box commits on Enter, on focus loss or on an arrow click, so its ValueChanged
    // IS the end of a gesture. The slider's ValueChanged fires on every detent
    // while the thumb is dragged, so a GestureHold keeps the value from the pointer
    // press to the release and writes it then; a keyboard or wheel step has no
    // press, so it writes at once. The box tracks the thumb live throughout, as
    // the readout.
    private void BuildMagnitude(SettingsCard card, SettingDescriptor s)
    {
        // Required by Setting.Magnitude, so the cast is safe; a wrong-kind args here
        // is a manifest bug, not a runtime input, hence the hard cast.
        var args = (MagnitudeArgs)s.Args!;

        double step = MagnitudeGrain.StepFor(args.Minimum, args.Maximum, args.Integer);
        int decimals = MagnitudeGrain.DecimalsFor(step);

        var box = new NumberBox
        {
            Minimum = args.Minimum,
            Maximum = args.Maximum,
            SmallChange = step,
            LargeChange = step * 5,
            // Arrows on hover and focus, in a flyout that pushes no layout — the
            // form the Playground rows already use, kept identical here.
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            MinWidth = 96,
            VerticalAlignment = VerticalAlignment.Center,
            // Fixed precision (fraction digits tracking the nice step) and no
            // grouping separators, so the readout matches the grain and a
            // four-digit figure reads "1200", not "1,200". FractionDigits is only a
            // minimum, so the rounder is what caps the readout at the grain: a
            // value stored with binary dust (0.15000000000000002) still shows
            // "0.15". It affects display only; a settings.json written before this
            // fix keeps its dust until the next edit.
            NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter
            {
                IntegerDigits = 1,
                FractionDigits = decimals,
                IsGrouped = false,
                NumberRounder = new Windows.Globalization.NumberFormatting.IncrementNumberRounder
                {
                    Increment = Math.Pow(10, -decimals),
                    RoundingAlgorithm = Windows.Globalization.NumberFormatting.RoundingAlgorithm.RoundHalfUp,
                },
            },
            Value = AsDouble(s.GetValue()),
        };

        Slider? slider = args.Slider
            ? new Slider
            {
                Minimum = args.Minimum,
                Maximum = args.Maximum,
                StepFrequency = step,
                Width = 180,
                // The thumb tooltip duplicates the editable field beside it, so off.
                IsThumbToolTipEnabled = false,
                VerticalAlignment = VerticalAlignment.Center,
                Value = AsDouble(s.GetValue()),
            }
            : null;

        // Internal lockstep guard: a programmatic Value set on either control fires
        // its ValueChanged, which would write the other and bounce back. This flag
        // makes the second write a no-op, so one user gesture updates both once. It
        // is separate from _syncingFromModel: that guards model→UI (the refresher),
        // this guards slider↔box regardless of direction.
        bool coordinating = false;

        if (slider is not null)
        {
            // The slider's Thumb handles the press and captures the pointer, so the
            // slider itself sees these events only with handledEventsToo. Release,
            // capture loss and cancel all end the gesture, so the hold cannot stay
            // armed after the pointer is gone.
            var hold = new GestureHold();
            slider.AddHandler(UIElement.PointerPressedEvent,
                new PointerEventHandler((_, _) => hold.Begin()), handledEventsToo: true);
            var endGesture = new PointerEventHandler((_, _) =>
            {
                if (hold.End() is double held) s.SetValue(held);
            });
            slider.AddHandler(UIElement.PointerReleasedEvent, endGesture, handledEventsToo: true);
            slider.AddHandler(UIElement.PointerCaptureLostEvent, endGesture, handledEventsToo: true);
            slider.AddHandler(UIElement.PointerCanceledEvent, endGesture, handledEventsToo: true);

            // Subscribe AFTER the initial Value assignment above so it does not fire.
            slider.ValueChanged += (_, e) =>
            {
                if (coordinating) return;
                // Slider detents are sums like 0.05 + 2 * 0.05 (0.15000000000000002):
                // snap to the display grain before the value reaches the field, the
                // model, the preset match and settings.json.
                double snapped = MagnitudeGrain.Snap(e.NewValue, decimals);
                coordinating = true;
                try { if (box.Value != snapped) box.Value = snapped; }
                finally { coordinating = false; }
                if (_syncingFromModel) return;
                if (hold.Offer(snapped)) s.SetValue(snapped);
            };
        }

        box.ValueChanged += (_, _) =>
        {
            if (coordinating) return;
            // A cleared field surfaces as NaN; swallow it so it never persists or
            // moves the thumb, the same guard the VM's own NaN checks apply.
            if (double.IsNaN(box.Value)) return;
            // A figure typed finer than the grain (0.1534 on a 0.01 grain) would
            // display rounded while the model kept the longer value, so what is
            // stored and compared would differ from what is read. Store the
            // displayed value instead.
            double snapped = MagnitudeGrain.Snap(box.Value, decimals);
            coordinating = true;
            try
            {
                if (box.Value != snapped) box.Value = snapped;
                if (slider is not null && slider.Value != snapped) slider.Value = snapped;
            }
            finally { coordinating = false; }
            if (_syncingFromModel) return;
            s.SetValue(snapped);
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (slider is not null) content.Children.Add(slider);
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
                if (slider is not null && slider.Value != value) slider.Value = value;
                if (box.Value != value && !double.IsNaN(value)) box.Value = value;
            }
            finally { coordinating = false; }
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // Renders a slider's value with invariant formatting. The displayed
    // precision follows StepFrequency so binary-float dust never reaches the UI.
    private static string FormatValue(double value, double stepFrequency)
    {
        int decimals = MagnitudeGrain.DecimalsFor(stepFrequency);
        return Math.Round(value, decimals)
            .ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
