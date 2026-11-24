using Xunit;

namespace Deckle.Projects.Tests;

// Whether Home sets the projects in no category apart in a closed fold.
[Trait("Category", "regression")]
public sealed class HomeLayoutTests
{
    // A store with one project and no category drew Home as a single closed
    // "Without a category" fold: the person's only project was hidden.
    [Fact]
    public void WithNoCategoryTheProjectsInNoCategoryShowOnHomeWithoutAFold()
    {
        Assert.False(HomeLayout.FoldsLooseProjects(categoryCount: 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void WithACategoryTheProjectsInNoCategoryGoInTheFold(int categoryCount)
    {
        Assert.True(HomeLayout.FoldsLooseProjects(categoryCount));
    }
}
