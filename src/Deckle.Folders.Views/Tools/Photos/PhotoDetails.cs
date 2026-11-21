using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Folders.Views;

// What the details panel holds for the photos selected in the gallery
// (spec, Details panel; boards 12.4 and 12.5): the kind and the weight, the
// name, the mark with its two buttons and their keys, then what the camera
// recorded. Several photos selected show their count and the mark alone, which
// the two buttons then set on all of them (Open 33: Explorer's details pane
// counts a multiple selection).
//
// One photo ends with its description (PhotoDescriptionSection) when the app
// has a model to ask (FoldersToolCatalog). Left out of this wave, so not drawn
// as dead controls: the people found, which wait for the local model.
//
// The panel lives while its selection does: a mark set by key or by pointer
// presses its button in place (ShowMark), so a click keeps its focus.
internal sealed class PhotoDetails
{
    private readonly ToggleButton _keep;
    private readonly ToggleButton _delete;

    private PhotoDetails(string kind, string title, Action<PhotoMark> mark, bool large, CaptureReading? reading,
                         PhotoDescriptionSection? description = null)
    {
        _keep = MarkButton(PhotoGlyphs.Keep, FoldersText.Get("Photos_Mark_Keep"), () => mark(PhotoMark.Keep));
        _delete = MarkButton(PhotoGlyphs.Delete, FoldersText.Get("Photos_Mark_Delete"), () => mark(PhotoMark.Delete));

        var buttons = new Grid { ColumnSpacing = 8 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_delete, 1);
        buttons.Children.Add(_keep);
        buttons.Children.Add(_delete);

        var markContent = new StackPanel { Spacing = 8 };
        markContent.Children.Add(buttons);
        markContent.Children.Add(new TextBlock
        {
            Text = FoldersText.Get(large ? "Photos_Mark_KeysLarge" : "Photos_Mark_Keys"),
            Style = Resource<Style>("CaptionTextBlockStyle"),
            Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        var sheet = new DetailsSheet(kind, title).Section(FoldersText.Get("Photos_Section_Mark"), markContent);
        if (reading is not null) AddCapture(sheet, reading);
        if (description is not null) sheet.Section(FoldersText.Get("Photos_Section_Description"), description.Element);
        Element = sheet.Element;
    }

    public UIElement Element { get; }

    // One photo. rank and total are given while the photo is seen large
    // (12.5: "13 sur 412"). descriptions is null when the app has no model to
    // ask: the section is then left out rather than drawn dead.
    public static PhotoDetails For(PhotoTile tile, CaptureReading reading, Action<PhotoMark> mark,
                                   bool large, int rank, int total, PhotoDescriptions? descriptions)
    {
        string kind = FoldersText.Format("Photos_Kind_Format", KindOf(tile.Photo),
            EntryFormat.Size(tile.Photo.Size, FoldersText.Culture));
        if (large) kind = FoldersText.Format("Photos_KindRank_Format", kind, rank, total);
        PhotoDescriptionSection? description = descriptions is null ? null : new PhotoDescriptionSection(descriptions, tile.Photo);
        var details = new PhotoDetails(kind, tile.Name, mark, large, reading, description);
        details.ShowMark(tile.Mark);
        return details;
    }

    public static PhotoDetails ForMany(IReadOnlyList<PhotoTile> tiles, Action<PhotoMark> mark)
    {
        long bytes = tiles.Sum(t => t.Photo.Size);
        var details = new PhotoDetails(
            FoldersText.Format("Photos_ManyKind_Format", EntryFormat.Size(bytes, FoldersText.Culture)),
            FoldersText.Format("Photos_ManyTitle_Format", tiles.Count),
            mark, large: false, reading: null);
        PhotoMark first = tiles[0].Mark;
        details.ShowMark(tiles.All(t => t.Mark == first) ? first : PhotoMark.None);
        return details;
    }

    // The button of the mark the selection holds is pressed; with no mark, or
    // a selection whose marks differ, neither is.
    public void ShowMark(PhotoMark mark)
    {
        _keep.IsChecked = mark == PhotoMark.Keep;
        _delete.IsChecked = mark == PhotoMark.Delete;
    }

    // "Raw" for a camera raw, as 12.4 writes it; else the format's usual name.
    private static string KindOf(PhotoFile photo)
    {
        if (photo.IsRaw) return FoldersText.Get("Photos_Kind_Raw");
        string extension = System.IO.Path.GetExtension(photo.Name).TrimStart('.').ToUpperInvariant();
        return extension switch
        {
            "JPG" or "JPEG" or "JFIF" => "JPEG",
            "TIF" => "TIFF",
            "HIF" => "HEIF",
            _ => extension,
        };
    }

    // "Prise de vue" (12.4). A row the file does not hold is left out; a file
    // that holds none, or cannot be read, says so in one line (Open 33).
    private static void AddCapture(DetailsSheet sheet, CaptureReading reading)
    {
        string title = FoldersText.Get("Photos_Section_Capture");
        CaptureData data = reading.Data;
        if (!reading.Succeeded || data.IsEmpty)
        {
            sheet.Section(title, new TextBlock
            {
                Text = FoldersText.Get(reading.Succeeded ? "Photos_Capture_None" : "Photos_Capture_Unreadable"),
                Style = Resource<Style>("CaptionTextBlockStyle"),
                Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }
        sheet.Section(title,
            (FoldersText.Get("Photos_Capture_Taken"), CaptureDataFormat.Taken(data, FoldersText.Culture)),
            (FoldersText.Get("Photos_Capture_Camera"), CaptureDataFormat.Camera(data)),
            (FoldersText.Get("Photos_Capture_Lens"), CaptureDataFormat.Lens(data, FoldersText.Culture)),
            (FoldersText.Get("Photos_Capture_Exposure"), CaptureDataFormat.Exposure(data, FoldersText.Culture)),
            (FoldersText.Get("Photos_Capture_Place"), CaptureDataFormat.Place(data, FoldersText.Culture)));
    }

    // A pressed toggle takes the accent fill, as 12.4 draws "Keep" (Open
    // 21, drawn: one fill). The click sets the mark; the view then presses
    // the right button through ShowMark, so the toggle never decides alone.
    private static ToggleButton MarkButton(string glyph, string text, Action click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var button = new ToggleButton
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => click();
        return button;
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
