using Xunit;
using Deckle.FolderMap;

namespace Deckle.Projects.Tests;

// The estimate is the map's own startup total: the always-read files plus the
// names and descriptions of the skills.
[Collection("projects provider singleton")]
[Trait("Category", "integration")]
public sealed class AgentReadingTests
{
    [Fact]
    public async Task AProjectWithContextFilesAndASkillGivesTheInventorysStartupTotal()
    {
        using var scratch = new ScratchFolder();
        string root = scratch.Folder("p");
        scratch.File("# Rules\n\n" + new string('a', 800), "p", "AGENTS.md");
        scratch.File("---\nname: ship\ndescription: Publishes a release.\n---\nBody.\n", "p", ".claude", "skills", "ship", "SKILL.md");

        AgentEstimate? estimate = await AgentReading.EstimateAsync(root, CancellationToken.None);

        int expected = MapInventory.Build(root, TestContext.Current.CancellationToken).EstimatedStartupTokens;
        Assert.NotNull(estimate);
        Assert.Equal(expected, estimate.Tokens);
        Assert.True(estimate.Tokens > 0);
        Assert.False(estimate.IsTruncated);
    }

    [Fact]
    public async Task AFolderWithNothingAnAgentReadsGivesZeroNotNull()
    {
        using var scratch = new ScratchFolder();
        string root = scratch.Folder("p");
        scratch.File("x", "p", "notes.txt");

        AgentEstimate? estimate = await AgentReading.EstimateAsync(root, CancellationToken.None);

        Assert.NotNull(estimate);
        Assert.Equal(0, estimate.Tokens);
    }

    [Fact]
    public async Task AFolderThatIsGoneGivesNull()
    {
        using var scratch = new ScratchFolder();

        AgentEstimate? estimate = await AgentReading.EstimateAsync(Path.Combine(scratch.Path, "gone"), CancellationToken.None);

        Assert.Null(estimate);
    }

    [Fact]
    public async Task LeavingThePageCancelsTheRead()
    {
        using var scratch = new ScratchFolder();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AgentReading.EstimateAsync(scratch.Folder("p"), cancelled.Token));
    }
}
