using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The activity rollup is a summary the always-on log keeps: log.jsonl admits a
// Verbose event only through its tag, so a rollup that loses the tag drops out
// of the file without any other test noticing.
[Trait("Category", "observability")]
public sealed class SummaryTagTests
{
    [Fact]
    public void TheActivityRollupCarriesTheSummaryTag()
    {
        MethodInfo method = typeof(DeckleAutocorrectSource).GetMethod(nameof(DeckleAutocorrectSource.ActivityRollup))!;
        EventAttribute attribute = method.GetCustomAttribute<EventAttribute>()!;

        Assert.True(ObservationTags.IsSummary(attribute.Tags), "ActivityRollup lost its Summary tag");
    }
}
