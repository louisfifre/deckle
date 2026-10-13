using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    // An editable NumberBox, with a Slider beside it when the value earns a
    // gesture. The box holds the exact value; the thumb approximates it to the
    // nearest detent.
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
        var args = (MagnitudeArgs)s.Args!;

        double step = MagnitudeGrain.StepFor(args.Minimum, args.Maximum, args.Integer);
        int decimals = MagnitudeGrain.DecimalsFor(step);

        var box = new NumberBox
        {
            Minimum = args.Minimum,
            Maximum = args.Maximum,
            SmallChange = step,
            LargeChange = step * 5,
            // Arrows on hover and focus, in a flyout that pushes no layout.
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            MinWidth = 96,
            VerticalAlignment = VerticalAlignment.Center,
            // No grouping, so a four-digit figure reads "1200", not "1,200".
            // FractionDigits is only a minimum, so the rounder is what caps the
            // readout at the grain: a value stored with binary dust
            // (0.15000000000000002) still shows "0.15".
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

        // A programmatic Value set on either control fires its ValueChanged, which
        // would write the other and bounce back; this flag makes the echo a no-op.
        // Separate from _syncingFromModel, which guards model→UI: this guards
        // slider↔box in either direction.
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
            // moves the thumb.
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
}
