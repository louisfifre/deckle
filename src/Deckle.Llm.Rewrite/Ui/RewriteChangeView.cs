using Deckle.Catalog;
using System.Globalization;

namespace Deckle.Llm.Rewrite;

public sealed record RewriteChangeView(string Original, string Rewritten)
{
    private const string ResourceMap = "Deckle.Llm.Rewrite";

    public string OriginalDisplay => string.IsNullOrEmpty(Original)
        ? Loc.GetFrom(ResourceMap, "RewriteOffer_Added")
        : Original;

    public string RewrittenDisplay => string.IsNullOrEmpty(Rewritten)
        ? Loc.GetFrom(ResourceMap, "RewriteOffer_Removed")
        : Rewritten;

    public string AccessibleName => string.Format(
        CultureInfo.CurrentCulture,
        Loc.GetFrom(ResourceMap, "RewriteOffer_Change_Format"),
        OriginalDisplay,
        RewrittenDisplay);
}
