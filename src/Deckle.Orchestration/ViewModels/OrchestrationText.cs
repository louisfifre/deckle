using System.Globalization;
using Deckle.Localization;

namespace Deckle.Orchestration;

// Format strings of this module's own .resw, read from its PRI subtree: the
// accessible names of the rows' controls, which carry the row's own data.
internal static class OrchestrationText
{
    internal static string Format(string key, string argument) =>
        string.Format(CultureInfo.CurrentCulture, Loc.GetFrom("Deckle.Orchestration", key), argument);
}