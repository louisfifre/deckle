using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "unit")]
public class AnytypeSpaceAliasesTests
{
    [Fact]
    public void LoadAlwaysMapsDevToTheCredentialSpace()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

        var aliases = AnytypeSpaceAliases.Load("dev-space", path);

        Assert.Equal("dev-space", aliases.Resolve("dev"));
    }

    [Fact]
    public void LoadMergesConfiguredAliases()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"aliases":{"home":"home-space"}}""");

            var aliases = AnytypeSpaceAliases.Load("dev-space", path);

            Assert.Equal("home-space", aliases.Resolve("home"));
            Assert.Equal("dev-space", aliases.Resolve("dev"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadRejectsConfiguredDevAliasPointingElsewhere()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"aliases":{"dev":"other-space"}}""");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => AnytypeSpaceAliases.Load("dev-space", path));

            Assert.Contains("dev", ex.Message);
            Assert.Contains("réservé", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveRejectsUnknownAliasWithKnownAliases()
    {
        var aliases = new AnytypeSpaceAliases(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dev"] = "dev-space",
            });

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => aliases.Resolve("raw-space-id"));

        Assert.Contains("Alias d'espace inconnu", ex.Message);
        Assert.Contains("dev", ex.Message);
    }
}
