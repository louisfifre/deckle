using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Atelier.Views;

// The summary "Appliquer les marques…" opens before anything moves (board
// 12.6): how many photos go to the Windows Recycle Bin and what they weigh,
// that they can be recovered until the bin is emptied, a strip of the first
// of them, then that the kept and the unmarked photos do not change. Its main
// button names the act and its count ("Mettre 37 photos à la Corbeille");
// "Annuler" leaves everything as it was.
//
// A ContentDialog: the system's own surface for a decision that blocks the
// window until it is taken, with its smoke layer and its keys (Enter, Esc).
// The main button is the default one: what it does can be undone from the
// Recycle Bin, as File Explorer's own "move to the Recycle Bin" is.
internal static class ApplySummary
{
    private const int StripCount = 8;
    private const double StripWidth = 48;
    private const double StripHeight = 32;

    public static ContentDialog Create(MarksPlan plan, XamlRoot root, IFileIconProvider icons)
    {
        int recycles = plan.Recycles.Count;
        var body = new StackPanel { Spacing = 16, MinWidth = 400 };

        var recycle = new StackPanel { Spacing = 12 };
        recycle.Children.Add(Line(PhotoGlyphs.RecycleBin, AtelierText.Format("Photos_Summary_Weight_Format",
            EntryFormat.Size(plan.RecycleBytes, AtelierText.Culture)), secondary: false));
        recycle.Children.Add(Strip(plan, root, icons));
        body.Children.Add(recycle);

        if (plan.KeptUnchanged > 0)
            body.Children.Add(Line(PhotoGlyphs.Keep, PhotoText.Count("Photos_Summary_Kept", plan.KeptUnchanged), secondary: true));
        if (plan.Unmarked > 0)
            body.Children.Add(Line(null, PhotoText.Count("Photos_Summary_Unmarked", plan.Unmarked), secondary: true));

        return new ContentDialog
        {
            XamlRoot = root,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = PhotoText.Count("Photos_Summary_Title", recycles),
            Content = body,
            PrimaryButtonText = PhotoText.Count("Photos_Summary_Confirm", recycles),
            CloseButtonText = AtelierText.Get("Photos_Summary_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
    }

    // An icon column then the sentence, so the three lines align on their text.
    private static Grid Line(string? glyph, string text, bool secondary)
    {
        var line = new Grid { ColumnSpacing = 12 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (glyph is not null)
        {
            line.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
        var sentence = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (secondary) sentence.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        Grid.SetColumn(sentence, 1);
        line.Children.Add(sentence);
        return line;
    }

    // The first photos that go, small, then "+N" for the rest (12.6). Each
    // shows its placeholder until the icon seam hands back its picture.
    private static StackPanel Strip(MarksPlan plan, XamlRoot root, IFileIconProvider icons)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(28, 0, 0, 0) };
        int pixelSize = (int)Math.Round(StripWidth * root.RasterizationScale);
        foreach (PlannedRecycle recycle in plan.Recycles.Take(StripCount))
        {
            var image = new Image { Stretch = Stretch.UniformToFill };
            var tile = new Grid
            {
                Width = StripWidth,
                Height = StripHeight,
                CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
            };
            tile.Children.Add(image);
            ToolTipService.SetToolTip(tile, recycle.Photo.Name);
            strip.Children.Add(tile);
            _ = Fill(image, recycle.Photo.Path, pixelSize, icons);
        }
        int more = plan.Recycles.Count - StripCount;
        if (more > 0)
        {
            strip.Children.Add(new TextBlock
            {
                Text = AtelierText.Format("Photos_Summary_More_Format", more),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            });
        }
        return strip;
    }

    private static async Task Fill(Image image, string path, int pixelSize, IFileIconProvider icons)
    {
        try
        {
            image.Source = await icons.GetThumbnailAsync(path, pixelSize, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
