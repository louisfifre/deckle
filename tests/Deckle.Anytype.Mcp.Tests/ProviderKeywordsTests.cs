using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// A person filtering the log on the cross-module Lifecycle keyword must find the
// MCP host's lines whichever of them it is, not only the milestones that travel
// in the journal.
[Trait("Category", "observability")]
public sealed class ProviderKeywordsTests
{
    // The provider's own Lifecycle bit; transverse bits 0..9 are reserved in Keywords.
    private const long ModuleLifecycle = 0x400;

    [Fact]
    [Trait("Category", "regression")]
    public void EveryHostLifecycleEventCarriesTheTransverseLifecycleKeyword()
    {
        var missing = new List<string>();
        foreach (MethodInfo method in typeof(DeckleAnytypeMcpSource).GetMethods(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            EventAttribute? attribute = method.GetCustomAttribute<EventAttribute>();
            if (attribute is null) continue;
            long bits = (long)attribute.Keywords;
            if ((bits & ModuleLifecycle) != 0 && (bits & (long)Keywords.Lifecycle) == 0)
                missing.Add(method.Name);
        }

        Assert.Empty(missing);
    }
}