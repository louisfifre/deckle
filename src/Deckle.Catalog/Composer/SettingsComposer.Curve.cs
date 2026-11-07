using System;
using System.Collections.Generic;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Globalization.NumberFormatting;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    // The curve block: the one CurveEditor hosted vertically in the card, full
    // width, with the Default-gated trailing reset shared with Path and Editor.
    // The editor writes through the descriptor's setter when a gesture ends (a
    // handle's release, a field's commit, a key nudge), never per tick, so the
    // gesture rule holds here as for a slider. The frame and the sentence are
    // re-read on every refresh, so the plot follows the anchors that set its
    // bounds and the description says what the current points do.
    //
    // The anchors (CurveArgs.Anchors) are the frame's values as fields in the
    // editor's legend: the doctrine's curve exception puts a curve's values in
    // fields beside the drawing, which is also how a screen reader edits them.
    // An anchor with a Role is also moved in the drawing, by the endpoint handle
    // of the frame coordinate it stands for, and written at the handle's release.
    // Each writes at its commit; the ones with a Default join the card's reset
    // and dirtiness, so the one wheel restores the curve and its frame.
    //
    // On an axes frame the curve is its page's instrument (2026-11-07): the plot
    // takes the card's full width and half the page's visible height, the pause
    // strip (CurveArgs.Pause) sits under it with its two durations, whose fields
    // follow the control points, and the reset ends the legend's row instead of
    // a column beside the plot, so the drawing has even margins.
    private void BuildCurve(SettingsCard card, SettingDescriptor s)
    {
        var args = (CurveArgs)s.Args!;

        HostVertically(card);

        CurveFrame frame = args.Frame?.Invoke() ?? CurveFrame.Unit;
        var editor = new CurveEditor
        {
            AllowOutside = args.AllowOutside,
            Frame = frame,
            Floor = args.Floor?.Invoke(),
            Points = AsPoints(s.GetValue()),
        };

        // A curve that describes itself carries its sentence under the plot, so
        // the card's own header and description would only repeat it above the
        // section's title (2026-11-07 review: the second header is removed). The
        // card keeps its name for a screen reader through the editor.
        if (args.Describe is not null)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor, ResolveHeader(s.LabelKey));
            card.Header = null!;
            card.Description = null!;
            editor.Summary = args.Describe(AsPoints(s.GetValue())) ?? ResolveDescription(s.LabelKey);
        }

        editor.PointsCommitted += (_, _) =>
        {
            if (_syncingFromModel) return;
            s.SetValue(editor.Points);
        };

        var anchorRefreshers = new List<Action>();
        var defaultedAnchors = new List<CurveAnchor>();
        if (args.Anchors is { Count: > 0 } anchors)
        {
            // The legend's one grid, the one the control points take under them,
            // so both blocks share their width and their columns.
            var fields = new List<FrameworkElement>();
            foreach (CurveAnchor anchor in anchors)
            {
                fields.Add(BuildAnchorField(anchor, anchorRefreshers));
                if (anchor.Default is not null) defaultedAnchors.Add(anchor);
            }
            editor.Legend.Children.Add(CurveEditor.FieldGrid(fields));
            BindEndpoints(editor, anchors);
        }

        if (args.Pause is { } pause && frame.Axes)
        {
            PauseStrip strip = BuildPauseStrip(pause);
            editor.PlotFooter = strip;
            editor.LegendAfter.Children.Add(CurveEditor.FieldGrid(
            [
                BuildAnchorField(pause.MinimumSpeech, anchorRefreshers),
                BuildAnchorField(pause.Margin, anchorRefreshers),
            ]));
            foreach (CurveAnchor value in new[] { pause.MinimumSpeech, pause.Margin })
                if (value.Default is not null) defaultedAnchors.Add(value);
            anchorRefreshers.Add(() =>
            {
                strip.MinimumSpeech = pause.MinimumSpeech.Get();
                strip.CutMargin = pause.Margin.Get();
                strip.CutAfter = pause.CutAfter();
            });
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
            extraReset: defaultedAnchors.Count > 0 ? ResetAnchors : null,
            resetHost: frame.Axes ? editor.LegendTrailing : null);
        card.Content = content;

        _refreshers.Add(() =>
        {
            foreach (Action refresh in anchorRefreshers) refresh();
            CurvePoints points = AsPoints(s.GetValue());
            if (editor.Points != points) editor.Points = points;
            editor.Frame = args.Frame?.Invoke() ?? CurveFrame.Unit;
            editor.Floor = args.Floor?.Invoke();
            if (args.Describe is not null)
                editor.Summary = args.Describe(points) ?? ResolveDescription(s.LabelKey);
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    // A legend field: the anchor's header as caption, its description as tooltip,
    // a NumberBox at the magnitude grain of its range, at the legend's one width
    // (CurveEditor.LabelledField sets it). The box writes at its
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

    // The endpoint handles of the anchors that carry a Role: each axis takes
    // its anchor's bounds and its field's grain, each handle is named by its
    // anchors' captions, and a moved frame writes back every anchor it changed,
    // clamped as its field would clamp it. No anchor with a Role, no handle.
    private void BindEndpoints(CurveEditor editor, IReadOnlyList<CurveAnchor> anchors)
    {
        CurveAnchor? Find(CurveAnchorRole role)
        {
            foreach (CurveAnchor anchor in anchors)
                if (anchor.Role == role) return anchor;
            return null;
        }

        CurveAnchor? originX = Find(CurveAnchorRole.OriginX), originY = Find(CurveAnchorRole.OriginY);
        CurveAnchor? endX = Find(CurveAnchorRole.EndX), endY = Find(CurveAnchorRole.EndY);
        var limits = new CurveEndpointLimits(Limit(originX), Limit(originY), Limit(endX), Limit(endY));
        if (!limits.OriginMovable && !limits.EndMovable) return;

        editor.EndpointLimits = limits;
        editor.OriginHandleName = Name(originX, originY);
        editor.EndHandleName = Name(endX, endY);
        editor.FrameCommitted += (_, moved) =>
        {
            if (_syncingFromModel) return;
            Write(originX, moved.OriginX);
            Write(originY, moved.OriginY);
            Write(endX, moved.EndX);
            Write(endY, moved.EndY);
        };

        string Name(CurveAnchor? x, CurveAnchor? y)
        {
            var parts = new List<string>();
            if (x is not null) parts.Add(ResolveHeader(x.LabelKey));
            if (y is not null) parts.Add(ResolveHeader(y.LabelKey));
            return string.Join(", ", parts);
        }

        static void Write(CurveAnchor? anchor, double value)
        {
            if (anchor is null) return;
            double clamped = Math.Clamp(value, anchor.Minimum, anchor.Maximum);
            if (anchor.Get() != clamped) anchor.Set(clamped);
        }
    }

    // The pause strip and its two handles, each bounded and grained as its
    // field, each writing at its release or arrow key.
    private PauseStrip BuildPauseStrip(PauseStripArgs pause)
    {
        var strip = new PauseStrip(new PauseStripText(
            Caption: ResolveHeader(pause.LabelKey),
            MinimumSpeech: ResolveEntry(pause.LabelKey, "MinimumSpeech"),
            Kept: ResolveEntry(pause.LabelKey, "Kept"),
            Dropped: ResolveEntry(pause.LabelKey, "Dropped"),
            Cut: ResolveEntry(pause.LabelKey, "Cut"),
            MinimumSpeechName: ResolveHeader(pause.MinimumSpeech.LabelKey),
            MarginName: ResolveHeader(pause.Margin.LabelKey)))
        {
            // The strip has a frame of its own now, so it no longer shares the
            // curve's edges; the insets leave room for the ruler's end labels.
            LeftInset = 24,
            RightInset = 24,
            MinimumSpeechLimit = Limit(pause.MinimumSpeech)!,
            CutMarginLimit = Limit(pause.Margin)!,
            MinimumSpeech = pause.MinimumSpeech.Get(),
            CutMargin = pause.Margin.Get(),
            CutAfter = pause.CutAfter(),
        };
        strip.MinimumSpeechCommitted += (_, value) =>
        {
            if (_syncingFromModel) return;
            pause.MinimumSpeech.Set(Math.Clamp(value, pause.MinimumSpeech.Minimum, pause.MinimumSpeech.Maximum));
        };
        strip.CutMarginCommitted += (_, value) =>
        {
            if (_syncingFromModel) return;
            pause.Margin.Set(Math.Clamp(value, pause.Margin.Minimum, pause.Margin.Maximum));
        };
        return strip;
    }

    // An anchor's bounds with its field's grain, the step a handle lands on.
    private static AxisLimit? Limit(CurveAnchor? anchor) => anchor is null
        ? null
        : new AxisLimit(anchor.Minimum, anchor.Maximum, MagnitudeGrain.StepFor(anchor.Minimum, anchor.Maximum, anchor.Integer));

    // A named entry of a module's strings beside a label's Header, such as
    // "<LabelKey>/Kept"; the composer's own strings when no module is set.
    private string ResolveEntry(string labelKey, string entry)
        => _module is null ? Loc.Get($"{labelKey}/{entry}") : Loc.GetFrom(_module, $"{labelKey}/{entry}");

    private static CurvePoints AsPoints(object? value)
        => value as CurvePoints ?? new CurvePoints(0.42, 0.0, 0.58, 1.0);
}
