using Xunit;

namespace Deckle.Core.Tests;

// The order every listing of names agrees on: Explorer's, with numbers read
// as numbers.
[Trait("Category", "unit")]
public sealed class FolderNameOrderTests
{
    [Fact]
    public void NumbersComeInOrderAndCaseIsIgnored()
    {
        string[] names = ["10 Notes", "b", "2 Notes", "A", "a2", "a10"];

        Assert.Equal(["2 Notes", "10 Notes", "A", "a2", "a10", "b"], names.Order(FolderNameOrder.Comparer));
    }
}
