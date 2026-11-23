using Deckle.Anytype;

namespace Deckle.Home;

// Every Anytype-visible label (type names, property names, closed-vocabulary
// options) lives in one embedded terms file per language,
// Terms/terms.<lang>.json, read by the same SpaceTerms the project-management
// space uses. The schema names the structure with stable English keys; this
// file names the words.
internal static class HomeTerms
{
    private const string DisplayLanguage = "fr";

    public static readonly SpaceTerms Current =
        SpaceTerms.Load(typeof(HomeTerms).Assembly, "Deckle.Home.Terms.terms", DisplayLanguage);
}
