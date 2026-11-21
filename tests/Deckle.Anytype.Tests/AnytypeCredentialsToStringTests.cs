using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// A record prints all its members, so a credentials object written to a log, an
// exception message or a debugger would show the API key. The key is made up here.
[Trait("Category", "unit")]
public class AnytypeCredentialsToStringTests
{
    const string Key = "made-up-key-0123456789-never-real";

    private static AnytypeCredentials Credentials() =>
        new("http://127.0.0.1:31012", "2025-11-08", Key, "space-1");

    [Fact]
    public void ToStringNeverContainsTheKey()
    {
        AnytypeCredentials credentials = Credentials();

        Assert.DoesNotContain(Key, credentials.ToString());
        Assert.DoesNotContain(Key, $"{credentials}");
        Assert.DoesNotContain(Key, string.Format("{0}", credentials));
    }

    [Fact]
    public void ToStringStillNamesTheOtherMembers()
    {
        string text = Credentials().ToString();

        Assert.Contains("http://127.0.0.1:31012", text);
        Assert.Contains("2025-11-08", text);
        Assert.Contains("space-1", text);
        Assert.Contains("ApiKey", text);
    }

    [Fact]
    public void TheClientStillCarriesTheRealKey() =>
        Assert.Equal(Key, Credentials().ApiKey);

    [Fact]
    public void TheLoadedDtoDoesNotPrintTheKeyEither()
    {
        var dto = new AnytypeCredentials.Dto("http://127.0.0.1:31012", "2025-11-08", Key, "space-1");

        Assert.DoesNotContain(Key, dto.ToString());
    }
}
