using Deckle.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Deckle.Catalog;

// The icon picker Deckle's apps share (specs/atelier.md, Window: the
// Atelier gives it to an added folder, Deckle Mail will give it to a
// mailbox). It edits one IconChoice and knows nothing of what carries it: the
// host opens it under the icon it changes, as a flyout (CreateFlyout), and
// saves each choice it reports.
//
// Two labels, "Icon" and "Color", and no title naming the item. A choice
// applies at once: there is no confirm button (12.3b), and ChoiceChanged
// fires on every change, the arrow keys included, as Windows Settings applies
// an accent swatch the moment it is selected.
//
// The wording belongs to the host, in its own catalog and language, not to
// the picker. The picker asks it for these keys:
// IconPicker_Icon, IconPicker_Color, IconPicker_Background (the row of the
// background option), IconPicker_Background_Off, IconPicker_Background_On,
// IconPicker_Symbol_<IconSymbol> and IconPicker_Tint_<IconTint> for every
// member.
//
// Three rows of tiles, each a GridView in single selection: the symbols, in
// four rows of six, each drawn in the current colour; the five colours,
// Windows' accent first; then the background option as two tiles, the symbol
// alone and the symbol on its square. A tile row is one Tab stop and the
// arrow keys move inside it, and every tile says its name to a screen reader.
public sealed partial class IconPicker : UserControl
{
    private const double TileSize = 40;
    private const double TileGap = 4;
    private const int SymbolsPerRow = 6;

    private static readonly IconSymbol[] Symbols = Enum.GetValues<IconSymbol>();
    private static readonly IconTint[] Tints = Enum.GetValues<IconTint>();

    private readonly GridView _symbols;
    private readonly GridView _tints;
    private readonly GridView _backgrounds;
    private readonly List<IconChoiceView> _symbolViews = [];
    private readonly List<IconChoiceView> _backgroundViews = [];
    private IconChoice _choice;
    private bool _showing;

    public IconPicker(IconChoice choice, Func<string, string> text)
    {
        _choice = choice;

        TextBlock iconLabel = Label(text("IconPicker_Icon"));
        TextBlock colorLabel = Label(text("IconPicker_Color"));

        _symbols = Tiles(iconLabel, SymbolsPerRow);
        foreach (IconSymbol symbol in Symbols)
        {
            var view = new IconChoiceView(choice with { Symbol = symbol }, 24);
            _symbolViews.Add(view);
            _symbols.Items.Add(Tile(view, text($"IconPicker_Symbol_{symbol}")));
        }

        _tints = Tiles(colorLabel, Tints.Length);
        foreach (IconTint tint in Tints)
            _tints.Items.Add(Tile(new IconTintSwatch(tint), text($"IconPicker_Tint_{tint}")));

        _backgrounds = Tiles(colorLabel, 2);
        AutomationProperties.SetName(_backgrounds, text("IconPicker_Background"));
        foreach (bool background in (bool[])[false, true])
        {
            var view = new IconChoiceView(choice with { HasBackground = background }, 24);
            _backgroundViews.Add(view);
            _backgrounds.Items.Add(Tile(view, text(background ? "IconPicker_Background_On" : "IconPicker_Background_Off")));
        }

        ShowChoice();
        _symbols.SelectionChanged += (_, _) => Pick(_choice with { Symbol = Symbols[Math.Max(0, _symbols.SelectedIndex)] });
        _tints.SelectionChanged += (_, _) => Pick(_choice with { Tint = Tints[Math.Max(0, _tints.SelectedIndex)] });
        _backgrounds.SelectionChanged += (_, _) => Pick(_choice with { HasBackground = _backgrounds.SelectedIndex == 1 });

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(iconLabel);
        panel.Children.Add(_symbols);
        colorLabel.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(colorLabel);
        panel.Children.Add(_tints);
        panel.Children.Add(_backgrounds);
        Content = panel;
    }

    public IconChoice Choice => _choice;

    public event Action<IconChoice>? ChoiceChanged;

    // The picker in a flyout under the icon it changes (12.3): the host
    // anchors it with ShowAt. Light dismiss and Escape close it; nothing is
    // left to confirm.
    public static Flyout CreateFlyout(IconChoice choice, Func<string, string> text, Action<IconChoice> changed)
    {
        var picker = new IconPicker(choice, text);
        picker.ChoiceChanged += changed;
        return new Flyout
        {
            Content = picker,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
        };
    }

    private void Pick(IconChoice choice)
    {
        if (_showing || choice == _choice) return;
        _choice = choice;
        ShowChoice();
        ChoiceChanged?.Invoke(choice);
    }

    // Selects the tiles of the current choice and redraws the previews in
    // it, without reporting the selection as a new choice.
    private void ShowChoice()
    {
        _showing = true;
        try
        {
            _symbols.SelectedIndex = Array.IndexOf(Symbols, _choice.Symbol);
            _tints.SelectedIndex = Array.IndexOf(Tints, _choice.Tint);
            _backgrounds.SelectedIndex = _choice.HasBackground ? 1 : 0;
            for (int i = 0; i < _symbolViews.Count; i++) _symbolViews[i].Choice = _choice with { Symbol = Symbols[i] };
            _backgroundViews[0].Choice = _choice with { HasBackground = false };
            _backgroundViews[1].Choice = _choice with { HasBackground = true };
        }
        finally
        {
            _showing = false;
        }
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
    };

    // A row of tiles as wide as `perRow` of them, so GridView's wrapping
    // panel breaks the rows there.
    private static GridView Tiles(TextBlock label, int perRow)
    {
        var grid = new GridView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = false,
            Width = perRow * (TileSize + TileGap),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollMode(grid, ScrollMode.Disabled);
        AutomationProperties.SetLabeledBy(grid, label);
        return grid;
    }

    private static GridViewItem Tile(UIElement content, string name)
    {
        var item = new GridViewItem
        {
            Content = content,
            Width = TileSize,
            Height = TileSize,
            MinWidth = TileSize,
            MinHeight = TileSize,
            Margin = new Thickness(0, 0, TileGap, TileGap),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(item, name);
        ToolTipService.SetToolTip(item, name);
        return item;
    }
}
