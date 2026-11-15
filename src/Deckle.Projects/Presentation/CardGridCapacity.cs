namespace Deckle.Projects;

// What a category's grid shows at a width (spec, Home): as many cards across as
// the width allows, each at least 240 DIP wide with 12 DIP between, filling at
// most three rows. With more projects than places, the last place is a tile
// "+ N more" that opens the category's page.
//
//   4 across, 15 projects:  12 places, 11 cards, "+ 4 more"
//   3 across, 15 projects:   9 places,  8 cards, "+ 7 more"
//   exactly 12 with 4 across: 12 cards, no tile
//
// The layout control (an ItemsRepeater with a UniformGridLayout) puts the cards
// in columns by itself; this arithmetic only decides how many items it is
// given, and it must agree with the layout's own column count, so the minimum
// width and the gap are the layout's settings, shared here.
public sealed record CardGridPlan(int Columns, int Places, int CardsShown, int MoreCount)
{
    public bool ShowsMoreTile => MoreCount > 0;

    public string MoreText => $"+ {MoreCount} more";
}

public static class CardGridCapacity
{
    public const double MinCardWidth = 240;
    public const double Gap = 12;
    public const int Rows = 3;

    // One more gap than the cards have gaps between them: the available width
    // plus a gap holds n cards when n * (card + gap) fits.
    public static int Columns(double width)
    {
        if (double.IsNaN(width) || width <= 0) return 1;
        return Math.Max(1, (int)Math.Floor((width + Gap) / (MinCardWidth + Gap)));
    }

    public static CardGridPlan Plan(double width, int itemCount)
    {
        int columns = Columns(width);
        int places = Rows * columns;
        int count = Math.Max(0, itemCount);

        if (count <= places) return new CardGridPlan(columns, places, count, 0);

        // The last place is the tile, so one card fewer than the places.
        int shown = places - 1;
        return new CardGridPlan(columns, places, shown, count - shown);
    }
}
