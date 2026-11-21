using System.Globalization;

namespace Deckle.Core;

// Explorer's order for the names of files and folders: case ignored and runs
// of digits compared as numbers ("2" before "10"). CompareOptions.NumericOrdering
// (.NET 10) does it, like StrCmpLogicalW. One comparer, so a folder listing, a
// map of a folder and a size report put the same names in the same order.
public static class FolderNameOrder
{
    public static readonly IComparer<string> Comparer = Comparer<string>.Create((a, b) =>
        CultureInfo.CurrentCulture.CompareInfo.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering));
}
