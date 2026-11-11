using System;
using System.Globalization;
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
// Page 7): two lanes, "Streaming" and "At stop", each step on its lane from
// its length on, and under them each length with its approximate speaking
// time. It shows and never writes: the values are the step cards' fields
// under it, the settings-ux rule for a preview.
//
// What it draws comes from StepTimelineLayout (Deckle.Llm.Rewrite), where the
// axis and the lanes are decided and tested; this control only lays them out.
// One grid: a column for the lane names, then one equal column per length, so
// a step at 30,000 characters and one at 1,000 both stay readable. A segment
// spans its columns; "As said" reads quieter than a profile, which is where
// something happens to the text.
//
// A new block, built from theme resources only (deckle-interface), so it
// follows light, dark, contrast and the accent by itself.
public sealed partial class RewriteStepTimeline : UserControl
{
    private const string Module = "Deckle.Llm.Rewrite.Views";
    private const double LaneHeight = 28;
    private const double SegmentGap = 2;

    private readonly Grid _grid = new() { ColumnSpacing = 0, RowSpacing = 6 };

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
        foreach (int _ in layout.Lengths)
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LaneHeight) });
        _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LaneHeight) });
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLaneName(Loc.GetFrom(Module, "LlmTimeline_StreamingLane"), row: 0);
        AddLaneName(Loc.GetFrom(Module, "LlmTimeline_AtStopLane"), row: 1);

        foreach (TimelineSegment segment in layout.Streaming) AddSegment(segment, row: 0);
        foreach (TimelineSegment segment in layout.AtStop) AddSegment(segment, row: 1);

        // A length's label starts where its column does; the first one, 0,
        // says nothing the lane names do not.
        for (int column = 1; column < layout.Lengths.Count; column++)
        {
            var label = new TextBlock
            {
                Text = FormatLength(layout.Lengths[column]),
                Style = Resource<Style>("CaptionTextBlockStyle"),
                Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(SegmentGap, 0, SegmentGap, 0),
            };
            Grid.SetRow(label, 2);
            Grid.SetColumn(label, column + 1);
            _grid.Children.Add(label);
        }
    }

    private void AddLaneName(string text, int row)
    {
        var name = new TextBlock
        {
            Text = text,
            Style = Resource<Style>("CaptionTextBlockStyle"),
            Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetRow(name, row);
        _grid.Children.Add(name);
    }

    private void AddSegment(TimelineSegment segment, int row)
    {
        string text = segment.AsSaid ? Loc.GetFrom(Module, "LlmTimeline_AsSaid") : segment.Name ?? "";
        var label = new TextBlock
        {
            Text = text,
            Style = Resource<Style>("CaptionTextBlockStyle"),
            Foreground = segment.AsSaid
                ? Resource<Brush>("TextFillColorSecondaryBrush")
                : Resource<Brush>("TextOnAccentFillColorPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        var block = new Border
        {
            Background = segment.AsSaid
                ? Resource<Brush>("ControlFillColorSecondaryBrush")
                : Resource<Brush>("AccentFillColorDefaultBrush"),
            BorderBrush = segment.AsSaid ? Resource<Brush>("CardStrokeColorDefaultBrush") : null,
            BorderThickness = new Thickness(segment.AsSaid ? 1 : 0),
            CornerRadius = Resource<CornerRadius>("ControlCornerRadius"),
            Margin = new Thickness(SegmentGap, 0, SegmentGap, 0),
            Child = label,
        };
        ToolTipService.SetToolTip(block, text);
        AutomationProperties.SetName(block, text);
        AutomationProperties.SetAccessibilityView(label, AccessibilityView.Raw);
        Grid.SetRow(block, row);
        Grid.SetColumn(block, segment.FromColumn + 1);
        Grid.SetColumnSpan(block, segment.ToColumn - segment.FromColumn);
        _grid.Children.Add(block);
    }

    // "4,000 · 6 min": the length as the fields hold it, then the speaking
    // time it takes on average (DictationRewriteSettings.CharsPerMinute), an
    // order of magnitude rather than a measure.
    internal static string FormatLength(int chars)
    {
        string length = chars.ToString("N0", CultureInfo.CurrentCulture);
        if (chars < DictationRewriteSettings.CharsPerMinute)
            return string.Format(CultureInfo.CurrentCulture,
                Loc.GetFrom(Module, "LlmTimeline_LengthUnderMinute_Format"), length);
        int minutes = (int)Math.Round(chars / (double)DictationRewriteSettings.CharsPerMinute);
        return string.Format(CultureInfo.CurrentCulture,
            Loc.GetFrom(Module, "LlmTimeline_Length_Format"), length, minutes);
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
