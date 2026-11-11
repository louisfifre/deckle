namespace Deckle.Atelier.Views;

// The photo tool's counted sentences. French puts 0 and 1 in the singular
// ("0 photo va", "1 photo va", "2 photos vont"), so a counted string has two
// keys in the catalog, <key>_One and <key>_Other, each with {0} for the count.
internal static class PhotoText
{
    private const int NamesShown = 5;

    public static string Count(string key, int count) =>
        AtelierText.Format(count <= 1 ? key + "_One" : key + "_Other", count);

    // A list of file names, the first few then how many more, so a bar of
    // two hundred failures stays one line.
    public static string Names(IReadOnlyList<string> names)
    {
        string shown = string.Join(", ", names.Take(NamesShown));
        int more = names.Count - NamesShown;
        return more <= 0 ? shown : AtelierText.Format("Photos_NamesMore_Format", shown, more);
    }
}
