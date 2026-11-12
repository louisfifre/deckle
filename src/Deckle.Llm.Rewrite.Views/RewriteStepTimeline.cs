using System;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Deckle.Localization;

namespace Deckle.Llm.Rewrite.Views;

// ── RewriteStepTimeline ───────────────────────────────────────────────────────
//
// The dictation steps on one axis (specs/deckle-dictation-rewriting.md,
// Page 9 to 12): one thin row per step with its name, then a scale of lengths
// with their approximate speaking time, then, while "Rewrite while I speak"
// is on, one caption for the pale blocks. It shows and never writes: the
// values are the step cards' fields under it, the settings-ux rule for a
// preview.
//
// What it draws comes from StepTimelineLayout (Deckle.Llm.Rewrite), where the
// axis, the rows and the blocks are decided and tested; this control only
// lays them out. The axis is proportional: a track is a grid whose star
// columns are the lengths between its spans' edges, so a span takes its share
// of the width without measuring anything.
//
// Built from theme resources only (deckle-interface), so it follows light,
// dark, contrast and the accent by itself: the system accent for a span, a
// neutral fill for the track, and the accent at reduced opacity for a pale
// block. No theme brush is a pale accent; an opacity on the accent brush
// keeps the hue of whatever accent the person chose, in either theme.
public sealed partial class RewriteStepTimeline : UserControl
{
    private const string Module = "Deckle.Llm.Rewrite.Views";
    private const double RowHeight = 20;
    private const double TrackHeight = 8;
    private const double BlockGap = 1;
    private const double PaleOpacity = 0.4;

    private readonly Grid _grid = new() { ColumnSpacing = 12, RowSpacing = 6 };

    public RewriteStepTimeline()
    {
        Content = _grid;
        AutomationProperties.SetName(this, Loc.GetFrom(Module, "LlmTimeline_Name"));
        ActualThemeChanged += (_, _) => Render();
    }

    private StepTimelineLayout? _layout;

    public void Show(StepTimelineLayout layout)
    {
        _layout = layout;
        Render();
    }

    private void Render()
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();
        if (_layout is not { } layout) return;

        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < layout.Rows.Count; i++)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
            AddRow(layout, layout.Rows[i], i);
        }

        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddScale(layout, row: layout.Rows.Count);

        if (layout.WhileSpeaking)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var caption = Caption(Loc.GetFrom(Module, "LlmTimeline_Caption"));
            caption.TextWrapping = TextWrapping.Wrap;
            Grid.SetRow(caption, layout.Rows.Count + 1);
            Grid.SetColumnSpan(caption, 2);
            _grid.Children.Add(caption);
        }
    }

    private void AddRow(StepTimelineLayout layout, TimelineRow row, int index)
    {
        var name = new TextBlock
        {
            Text = row.Name,
            Style = Resource<Style>("BodyTextBlockStyle"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 160,
        };
        AutomationProperties.SetAccessibilityView(name, AccessibilityView.Raw);
        Grid.SetRow(name, index);
        _grid.Children.Add(name);

        // The track's columns: every edge of a span, and the axis' ends.
        int[] edges = row.Spans
            .SelectMany(span => new[] { span.FromChars, span.ToChars })
            .Append(0).Append(layout.AxisChars)
            .Distinct().Order().ToArray();
        var track = new Grid
        {
            Height = TrackHeight,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Resource<Brush>("ControlStrongFillColorDisabledBrush"),
            CornerRadius = new CornerRadius(TrackHeight / 4),
        };
        for (int i = 0; i + 1 < edges.Length; i++)
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(edges[i + 1] - edges[i], GridUnitType.Star) });

        foreach (TimelineSpan span in row.Spans)
        {
            var block = new Border
            {
                Background = Resource<Brush>("AccentFillColorDefaultBrush"),
                Opacity = span.Pale ? PaleOpacity : 1,
                CornerRadius = track.CornerRadius,
                // Neighbouring blocks read as blocks; a range alone needs no gap.
                Margin = layout.WhileSpeaking ? new Thickness(BlockGap, 0, BlockGap, 0) : new Thickness(0),
            };
            int from = Array.IndexOf(edges, span.FromChars);
            Grid.SetColumn(block, from);
            Grid.SetColumnSpan(block, Array.IndexOf(edges, span.ToChars) - from);
            track.Children.Add(block);
        }

        string description = layout.WhileSpeaking
            ? Format("LlmTimeline_RowBlocks_Format", row.Name, row.BlockChars, row.ToChars)
            : Format("LlmTimeline_RowRange_Format", row.Name, row.FromChars, row.ToChars);
        AutomationProperties.SetName(track, description);
        ToolTipService.SetToolTip(track, description);
        Grid.SetRow(track, index);
        Grid.SetColumn(track, 1);
        _grid.Children.Add(track);
    }

    // The ticks fall at equal intervals: the scale cuts the width into half
    // intervals, and a label spans the two halves around its tick, centred,
    // except the first and the last, which keep to the ends.
    private void AddScale(StepTimelineLayout layout, int row)
    {
        var scale = new Grid();
        int intervals = layout.Ticks.Count - 1;
        for (int i = 0; i < intervals * 2; i++)
            scale.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < layout.Ticks.Count; i++)
        {
            var label = Caption(FormatLength(layout.Ticks[i]));
            if (i == 0)
            {
                label.HorizontalAlignment = HorizontalAlignment.Left;
            }
            else if (i == intervals)
            {
                // The last half interval alone is too narrow for a label: it
                // takes the interval before its tick, against the end.
                label.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(label, i * 2 - 2);
                Grid.SetColumnSpan(label, 2);
            }
            else
            {
                label.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(label, i * 2 - 1);
                Grid.SetColumnSpan(label, 2);
            }
            scale.Children.Add(label);
        }

        Grid.SetRow(scale, row);
        Grid.SetColumn(scale, 1);
        _grid.Children.Add(scale);
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = Resource<Style>("CaptionTextBlockStyle"),
        Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
        TextWrapping = TextWrapping.NoWrap,
    };

    // "12,000 ≈ 17 min": the length as the fields hold it, then the
    // speaking time it takes on average (DictationRewriteSettings.
    // CharsPerMinute), an order of magnitude rather than a measure. 0 alone.
    internal static string FormatLength(int chars)
    {
        if (chars == 0) return 0.ToString(CultureInfo.CurrentCulture);
        string length = chars.ToString("N0", CultureInfo.CurrentCulture);
        if (chars < DictationRewriteSettings.CharsPerMinute)
            return string.Format(CultureInfo.CurrentCulture,
                Loc.GetFrom(Module, "LlmTimeline_LengthUnderMinute_Format"), length);
        int minutes = (int)Math.Round(chars / (double)DictationRewriteSettings.CharsPerMinute);
        return string.Format(CultureInfo.CurrentCulture,
            Loc.GetFrom(Module, "LlmTimeline_Length_Format"), length, minutes);
    }

    private static string Format(string key, string name, int a, int b) =>
        string.Format(CultureInfo.CurrentCulture, Loc.GetFrom(Module, key),
            name, a.ToString("N0", CultureInfo.CurrentCulture), b.ToString("N0", CultureInfo.CurrentCulture));

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
