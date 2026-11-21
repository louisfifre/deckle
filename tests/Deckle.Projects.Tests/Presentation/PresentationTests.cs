using Xunit;
using Deckle.Core;

namespace Deckle.Projects.Tests;

// The arithmetic of the grid, in the spec's own numbers.
[Trait("Category", "unit")]
public sealed class CardGridCapacityTests
{
    // 1100 DIP holds four cards of 240 with three gaps of 12; 4 * 252 - 12 = 996.
    private const double FourAcross = 1000;
    private const double ThreeAcross = 760;

    [Theory]
    [InlineData(996, 4)]
    [InlineData(995, 3)]
    [InlineData(744, 3)]
    [InlineData(492, 2)]
    [InlineData(240, 1)]
    [InlineData(100, 1)]
    [InlineData(0, 1)]
    [InlineData(-50, 1)]
    public void TheColumnsAreHowManyCardsOf240WithAGapOf12FitAcross(double width, int columns)
    {
        Assert.Equal(columns, CardGridCapacity.Columns(width));
    }

    [Fact]
    public void NotANumberIsOneColumn()
    {
        Assert.Equal(1, CardGridCapacity.Columns(double.NaN));
    }

    [Fact]
    public void FourAcrossAndFifteenProjectsGiveElevenCardsAndFourMore()
    {
        CardGridPlan plan = CardGridCapacity.Plan(FourAcross, 15);

        Assert.Equal(4, plan.Columns);
        Assert.Equal(12, plan.Places);
        Assert.Equal(11, plan.CardsShown);
        Assert.Equal(4, plan.MoreCount);
        Assert.True(plan.ShowsMoreTile);
        Assert.Equal("+ 4 more", plan.MoreText);
    }

    [Fact]
    public void ThreeAcrossAndFifteenProjectsGiveEightCardsAndSevenMore()
    {
        CardGridPlan plan = CardGridCapacity.Plan(ThreeAcross, 15);

        Assert.Equal(3, plan.Columns);
        Assert.Equal(8, plan.CardsShown);
        Assert.Equal(7, plan.MoreCount);
        Assert.Equal("+ 7 more", plan.MoreText);
    }

    [Fact]
    public void ExactlyTwelveWithFourAcrossShowsTwelveAndNoTile()
    {
        CardGridPlan plan = CardGridCapacity.Plan(FourAcross, 12);

        Assert.Equal(12, plan.CardsShown);
        Assert.False(plan.ShowsMoreTile);
        Assert.Equal(0, plan.MoreCount);
    }

    [Fact]
    public void ThirteenShowsElevenAndTwoMore()
    {
        CardGridPlan plan = CardGridCapacity.Plan(FourAcross, 13);

        Assert.Equal(11, plan.CardsShown);
        Assert.Equal(2, plan.MoreCount);
    }

    [Fact]
    public void AnEmptyCategoryShowsNothingAndNoTile()
    {
        CardGridPlan plan = CardGridCapacity.Plan(FourAcross, 0);

        Assert.Equal(0, plan.CardsShown);
        Assert.False(plan.ShowsMoreTile);
    }

    [Fact]
    public void ANarrowWindowIsOneColumnOfThreePlaces()
    {
        CardGridPlan plan = CardGridCapacity.Plan(180, 5);

        Assert.Equal(1, plan.Columns);
        Assert.Equal(3, plan.Places);
        Assert.Equal(2, plan.CardsShown);
        Assert.Equal(3, plan.MoreCount);
    }

    [Fact]
    public void ThePlacesAndTheMoreCountAlwaysAddUpToTheItems()
    {
        for (double width = 100; width <= 2400; width += 37)
        {
            for (int items = 0; items <= 40; items++)
            {
                CardGridPlan plan = CardGridCapacity.Plan(width, items);
                Assert.Equal(items, plan.CardsShown + plan.MoreCount);
                Assert.True(plan.CardsShown + (plan.ShowsMoreTile ? 1 : 0) <= plan.Places);
            }
        }
    }
}

// The words of a card's meta line, which the category page's rows share.
[Trait("Category", "unit")]
public sealed class CardMetaTests
{
    private static ProjectReading Reading(string? tag = null, IReadOnlyList<VersionSource>? sources = null, int? uncommitted = 0,
        RepositoryState git = RepositoryState.Repository) =>
        new(FolderState.Present, git, new VersionReading(tag, sources ?? (tag is null ? [] : [new(VersionSource.GitTagLabel, tag[1..])])),
            uncommitted, null, false, DateTimeOffset.UnixEpoch);

    [Fact]
    public void AReadingNotThereYetGivesAnEmptyLine()
    {
        Assert.Equal("", CardMeta.Text(null));
    }

    [Fact]
    public void AVersionAndUncommittedFilesReadLikeTheMockup()
    {
        Assert.Equal("v0.131.0 · 3 uncommitted", CardMeta.Text(Reading("v0.131.0", uncommitted: 3)));
    }

    [Fact]
    public void ACleanProjectShowsTheVersionAlone()
    {
        Assert.Equal("v0.6.0", CardMeta.Text(Reading("v0.6.0", uncommitted: 0)));
    }

    [Fact]
    public void NoTagReadsNoVersionYet()
    {
        Assert.Equal("No version yet", CardMeta.Text(Reading(tag: null, uncommitted: 0)));
    }

    [Fact]
    public void ADisagreeingSetOfVersionsIsSaidInWords()
    {
        var sources = new VersionSource[] { new("Git tag", "0.32.0"), new("a/Cargo.toml", "0.1.0"), new("b/gradle.properties", "0.1.0") };

        Assert.Equal("v0.32.0 · 3 versions found", CardMeta.Text(Reading("v0.32.0", sources, uncommitted: 0)));
    }

    [Fact]
    public void TheCountOfVersionsComesBeforeTheUncommittedFiles()
    {
        var sources = new VersionSource[] { new("Git tag", "0.32.0"), new("a/Cargo.toml", "0.1.0") };

        Assert.Equal("v0.32.0 · 2 versions found · 2 uncommitted", CardMeta.Text(Reading("v0.32.0", sources, uncommitted: 2)));
    }

    [Fact]
    public void AgreeingVersionsAreNotCounted()
    {
        var sources = new VersionSource[] { new("Git tag", "1.0.0"), new("Cargo.toml", "1.0.0") };

        Assert.Equal("v1.0.0", CardMeta.Text(Reading("v1.0.0", sources, uncommitted: 0)));
    }

    [Fact]
    public void AFolderThatIsNotARepositoryHasNoUncommittedPart()
    {
        Assert.Equal("No version yet", CardMeta.Text(Reading(tag: null, uncommitted: null, git: RepositoryState.NotARepository)));
    }

    [Fact]
    public void AMissingFolderSaysSo()
    {
        Assert.Equal("This folder was not found", CardMeta.Text(ProjectReading.Missing(DateTimeOffset.UnixEpoch)));
    }
}

// Back and Forward over the window's places, with the removal of a project or a
// category forgetting the places that name it.
[Trait("Category", "unit")]
public sealed class ProjectsTargetTests
{
    [Fact]
    public void PlacesCompareByValue()
    {
        Assert.Equal<ProjectsTarget>(new ProjectsTarget.Home(), new ProjectsTarget.Home());
        Assert.Equal<ProjectsTarget>(new ProjectsTarget.Category("c"), new ProjectsTarget.Category("c"));
        Assert.NotEqual<ProjectsTarget>(new ProjectsTarget.Category("c"), new ProjectsTarget.Category("d"));
        Assert.NotEqual<ProjectsTarget>(new ProjectsTarget.Project("p"), new ProjectsTarget.Project("p", BeginRename: true));
        Assert.NotEqual<ProjectsTarget>(new ProjectsTarget.Category("p"), new ProjectsTarget.Project("p"));
    }

    [Fact]
    public void ARenameArrivalIsNotPartOfThePlaceTheHistoryRecords()
    {
        var arrival = new ProjectsTarget.Project("p1", BeginRename: true);
        var history = new NavigationHistory<ProjectsTarget>();
        history.Navigate(new ProjectsTarget.Project("p1"));

        Assert.True(arrival.BeginsRename);
        Assert.Equal<ProjectsTarget>(new ProjectsTarget.Project("p1"), arrival.Place);
        Assert.False(history.Navigate(arrival.Place));
        Assert.False(new ProjectsTarget.Project("p1").BeginsRename);
        Assert.Equal<ProjectsTarget>(new ProjectsTarget.Home(), new ProjectsTarget.Home().Place);
    }

    [Fact]
    public void BackLeadsToTheScreenTheCameFromAndNavigatingToTheCurrentPlaceChangesNothing()
    {
        var history = new NavigationHistory<ProjectsTarget>();
        history.Navigate(new ProjectsTarget.Home());
        history.Navigate(new ProjectsTarget.Category("dev"));
        history.Navigate(new ProjectsTarget.Project("p1"));

        Assert.False(history.Navigate(new ProjectsTarget.Project("p1")));
        Assert.Equal(new ProjectsTarget.Category("dev"), history.GoBack());
        Assert.Equal(new ProjectsTarget.Home(), history.GoBack());
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void RemovingAProjectForgetsEveryPlaceThatNamesIt()
    {
        var history = new NavigationHistory<ProjectsTarget>();
        history.Navigate(new ProjectsTarget.Home());
        history.Navigate(new ProjectsTarget.Project("p1"));
        history.Navigate(new ProjectsTarget.Category("dev"));
        history.Navigate(new ProjectsTarget.Project("p1", BeginRename: true));

        bool currentGone = history.Forget(ProjectsTarget.NamesProject("p1"));

        Assert.True(currentGone);
        history.Navigate(new ProjectsTarget.Home());
        Assert.Equal(new ProjectsTarget.Category("dev"), history.GoBack());
        Assert.Equal(new ProjectsTarget.Home(), history.GoBack());
    }

    [Fact]
    public void ADeletedCategoryIsForgottenToo()
    {
        var history = new NavigationHistory<ProjectsTarget>();
        history.Navigate(new ProjectsTarget.Home());
        history.Navigate(new ProjectsTarget.Category("dev"));
        history.Navigate(new ProjectsTarget.Project("p1"));

        history.Forget(ProjectsTarget.NamesCategory("dev"));

        Assert.Equal(new ProjectsTarget.Home(), history.GoBack());
    }
}
