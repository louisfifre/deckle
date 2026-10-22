using Deckle.TestSupport;
using Xunit;

namespace Deckle.Anytype.Tests;

// An IO exception quotes the path it failed on, and the backend lives under the
// profile. The line keeps the file and loses the account.
[Trait("Category", "observability")]
public sealed class AnytypePathRedactionTests
{
    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void ASpawnFailureNamesTheExecutableUnderTheProfileVariable()
    {
        using var listener = new TestEventListener("Deckle-Anytype");

        DeckleAnytypeSource.Log.BackendSpawnFailedDetail(Path.Combine(Profile, "anytype", "anytype.exe"));

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendSpawnFailedDetail);
        Assert.Equal(@"%USERPROFILE%\anytype\anytype.exe", ev.Payload![0]);
    }

    [Fact]
    public void AProviderFileFailureHidesThePathItsExceptionQuotes()
    {
        using var listener = new TestEventListener("Deckle-Anytype");

        DeckleAnytypeSource.Log.BackendProviderFileOperationFailed(
            "read_activation", "IOException", $"The process cannot access '{Profile}\\anytype\\active.json'.");

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendProviderFileOperationFailed);
        Assert.Equal(@"The process cannot access '%USERPROFILE%\anytype\active.json'.", ev.Payload![2]);
    }
}
