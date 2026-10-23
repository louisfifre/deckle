using System.Diagnostics.Tracing;
using System.Reflection;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Anytype.Tests;

// A person filtering the log on the cross-module Lifecycle keyword must find the
// backend's supervision lines whichever of them it is, not only the milestones
// that travel in the log.
[Trait("Category", "observability")]
public sealed class ProviderKeywordsTests
{
    // The provider's own Lifecycle bit; transverse bits 0..9 are reserved in Keywords.
    private const long ModuleLifecycle = 0x800;

    [Fact]
    [Trait("Category", "regression")]
    public void EveryBackendLifecycleEventCarriesTheTransverseLifecycleKeyword()
    {
        var missing = new List<string>();
        foreach (MethodInfo method in typeof(DeckleAnytypeSource).GetMethods(
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