using System;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

public sealed partial class SettingsComposer
{
    // The curve block (settings design system, decision 21): the one CurveEditor
    // hosted vertically in the card, full width, with the Default-gated trailing
    // reset shared with Path and Editor. The editor writes through the
    // descriptor's setter when a gesture ends (a handle's release, a field's
    // commit, a key nudge), never per tick, so the gesture rule holds here as for
    // a slider. The frame and the sentence are re-read on every refresh, so the
    // plot follows the sibling cards that set its bounds and the description says
    // what the current points do.
    private void BuildCurve(SettingsCard card, SettingDescriptor s)
    {
        var args = (CurveArgs)s.Args!;

        card.ContentAlignment = ContentAlignment.Vertical;

        var editor = new CurveEditor
        {
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

        (FrameworkElement content, Action? updateReset) = WrapWithTrailingReset(card, editor, s);
        card.Content = content;

        _refreshers.Add(() =>
        {
            CurvePoints points = AsPoints(s.GetValue());
            if (editor.Points != points) editor.Points = points;
            editor.Frame = args.Frame?.Invoke() ?? CurveFrame.Unit;
            editor.Floor = args.Floor?.Invoke();
            if (args.Describe is not null)
            {
                string? sentence = args.Describe(points);
                card.Description = sentence ?? ResolveDescription(s.LabelKey);
            }
            updateReset?.Invoke();
            ApplyReactiveState(card, s);
        });
    }

    private static CurvePoints AsPoints(object? value)
        => value as CurvePoints ?? new CurvePoints(0.42, 0.0, 0.58, 1.0);
}
