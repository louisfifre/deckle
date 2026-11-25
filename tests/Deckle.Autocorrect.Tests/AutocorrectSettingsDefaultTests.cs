using Xunit;

namespace Deckle.Autocorrect.Tests;

// The first-run defaults depend on the Windows languages, passed in so the
// tests never read the machine's.
[Trait("Category", "unit")]
public sealed class AutocorrectSettingsDefaultTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "deckle-autocorrect-default-" + Guid.NewGuid().ToString("N"));

    public AutocorrectSettingsDefaultTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static IReadOnlySet<string> Languages(params string[] tags) =>
        new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void EnglishList_StartsDisabledWithNoAppEnrolled()
    {
        var s = AutocorrectSettings.CreateDefault(Languages("en"));

        Assert.False(s.Enabled);
        Assert.Empty(s.Apps);
    }

    [Fact]
    public void FrenchList_StartsEnabledWithNoAppEnrolled()
    {
        var s = AutocorrectSettings.CreateDefault(Languages("fr", "en"));

        Assert.True(s.Enabled);
        Assert.Empty(s.Apps);
    }

    [Fact]
    public void Seed_WritesTheLanguageDefaultWhenNoFileExists()
    {
        string path = Path.Combine(_dir, "settings.json");

        Assert.True(AutocorrectSettingsService.SeedIfMissing(path, Languages("en")));

        string json = File.ReadAllText(path);
        Assert.Contains("\"enabled\": false", json);
    }

    [Fact]
    public void Seed_NeverOverwritesASavedSetting()
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"enabled":false,"apps":{"claude":true}}""");

        Assert.False(AutocorrectSettingsService.SeedIfMissing(path, Languages("fr")));

        Assert.Contains("\"enabled\":false", File.ReadAllText(path));
    }
}
