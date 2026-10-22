using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "unit")]
public sealed class WorkIdTests
{
    [Fact]
    public void Every_identifier_is_distinct_even_when_taken_from_many_threads()
    {
        const int PerThread = 2_000;
        var all = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < PerThread; i++) all.Add(WorkId.Next());
        });

        Assert.Equal(8 * PerThread, all.Distinct().Count());
    }

    [Fact]
    public void An_identifier_is_short_and_made_of_a_prefix_and_lowercase_hex()
    {
        string id = WorkId.Next();

        Assert.Matches("^w[0-9a-f]+$", id);
        Assert.True(id.Length <= 12);
    }
}
