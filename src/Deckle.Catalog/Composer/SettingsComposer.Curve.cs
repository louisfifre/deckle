using System;
using System.Collections.Generic;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Globalization.NumberFormatting;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    // The curve block (settings design system, decision 21): the one CurveEditor
    // hosted vertically in the card, full width, with the Default-gated trailing
    // reset shared with Path and Editor. The editor writes through the
    // descriptor's setter when a gesture ends (a handle's release, a field's
    // commit, a key nudge), never per tick, so the gesture rule holds here as for
    // a slider. The frame and the sentence are re-read on every refresh, so the
    // plot follows the anchors that set its bounds and the description says
    // what the current points do.
    //
    // The anchors (CurveArgs.Anchors) are the frame's values as fields in the
    // editor's legend, beside the plot: the doctrine's curve exception puts a
    // curve's values in fields beside the drawing (decisions 21 and 25), and the
    // legend slot is a first form of the still-open legend block (decision 20),
    // to confirm with Louis. Each writes at its commit; the ones with a Default join the card's
    // reset and dirtiness, so the one wheel restores the curve and its frame.
    private void BuildCurve(SettingsCard card, SettingDescriptor s)
    {
        var args = (CurveArgs)s.Args!;

        HostVertically(card);

        var editor = new CurveEditor
        {
            // Taller than the Playground's plot: here the curve is the page's
            // main instrument, at full card width (2026-10-14).
            WideHeight = 320.0,
            AllowOutside = args.AllowOutside,
            Frame = args.Frame?.Invoke() ?? CurveFrame.Unit,
            Floor = args.Floor?.Invoke(),
            Points = AsPoints(s.GetValue()),
        };

        editor.PointsCommitted += (_, _) =>
        {
            if (_syncingFromModel) return;
            s.SetValue(editor.Points);
        };

        var anchorRefreshers = new List<Action>();
        var defaultedAnchors = new List<CurveAnchor>();
        if (args.Anchors is { Count: > 0 } anchors)
        {
            // Two fields per row, the shape the control points take under them.
            var rows = new StackPanel { Spacing = 8 };
            StackPanel? row = null;
            foreach (CurveAnchor anchor in anchors)
            {
                if (row is null || row.Children.Count == 2)
                {
                    row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                    rows.Children.Add(row);
                }
                row.Children.Add(BuildAnchorField(anchor, anchorRefreshers));
                if (anchor.Default is not null) defaultedAnchors.Add(anchor);
            }
            editor.Legend.Children.Add(rows);
        }

        bool AnchorsDirty()
        {
            foreach (CurveAnchor anchor in defaultedAnchors)
                if (!DefaultEquals(anchor.Get(), anchor.Default!())) return true;
            return false;
        }

        void ResetAnchors()
        {
            foreach (CurveAnchor anchor in defaultedAnchors)
                anchor.Set(anchor.Default!());
        }

        (FrameworkElement content, Action? updateReset) = WrapWithTrailingReset(card, editor, s,
            extraDirty: defaultedAnchors.Count > 0 ? AnchorsDirty : null,
            extraReset: defaultedAnchors.Count > 0 ? ResetAnchors : null);
        card.Content = content;

        _refreshers.Add(() =>
        {
            foreach (Action refresh in anchorRefreshers) refresh();
            CurvePoints points = AsPoints(s.GetValue());
            if (editor.Points != points) editor.Points = points;
            editor.Frame = args.Frame?.Invoke() ?? CurveFrame.Unit;
            editor.Floor = args.Floor?.Invoke();
            if (args.Describe is not null)
            {
                // The sentence is a legend in words: it goes through the
                // page-wide Info switch like the .resw description it replaces.
                string? sentence = args.Describe(points);
                SettingsDescriptions.Attach(card, sentence ?? ResolveDescription(s.LabelKey));
            }
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // A legend field: the anchor's header as caption, its description as tooltip,
    // a NumberBox at the magnitude grain of its range. The box writes at its
    // commit (ValueChanged on a NumberBox) and is re-read on every refresh under
    // the model-sync guard, like a Magnitude card's field.
    private FrameworkElement BuildAnchorField(CurveAnchor anchor, List<Action> refreshers)
    {
        double step = MagnitudeGrain.StepFor(anchor.Minimum, anchor.Maximum, anchor.Integer);
        var box = new NumberBox
        {
            Minimum = anchor.Minimum,
            Maximum = anchor.Maximum,
            SmallChange = step,
            LargeChange = step * 10,
            Width = 112,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
            NumberFormatter = new DecimalFormatter
            {
                IntegerDigits = 1,
                FractionDigits = MagnitudeGrain.DecimalsFor(step),
                IsGrouped = false,
            },
            Value = anchor.Get(),
        };

        string? description = ResolveDescription(anchor.LabelKey);
        if (description is not null) ToolTipService.SetToolTip(box, description);

        box.ValueChanged += (_, e) =>
        {
            if (_syncingFromModel) return;
            // A cleared field never persists: NaN is left for the refresh to
            // overwrite with the model's value.
            if (double.IsNaN(e.NewValue)) return;
            anchor.Set(Math.Clamp(e.NewValue, anchor.Minimum, anchor.Maximum));
        };

        refreshers.Add(() =>
        {
            double value = anchor.Get();
            if (!double.IsNaN(value) && box.Value != value) box.Value = value;
        });

        return CurveEditor.LabelledField(ResolveHeader(anchor.LabelKey), box);
    }

    private static CurvePoints AsPoints(object? value)
        => value as CurvePoints ?? new CurvePoints(0.42, 0.0, 0.58, 1.0);
}
