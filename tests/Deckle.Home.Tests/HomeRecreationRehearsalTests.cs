using Deckle.Anytype;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// The recreation rehearsal as maintainer operations, hosted by the test
// runner: they reach the real Anytype backend with the machine's credentials,
// so they are explicit and skip unless their variables are set. They print
// counts only; everything that names the person's objects stays in the files
// beside the sheet (see OPERATIONS.md, Recreation rehearsal).
//
// RecreateOnTestSpace: export DECKLE_HOME_REHEARSAL_SOURCE (an alias, e.g.
// home) into a sheet, apply the manifest on DECKLE_HOME_REHEARSAL_TARGET
// (the alias of an EMPTY test space), replay, compare.
// CreateTestSpace: create the space named DECKLE_HOME_CREATE_TEST_SPACE and
// print the alias line to add to spaces.json.
[Trait("Category", "maintenance")]
public sealed class HomeRecreationRehearsalTests
{
    public const string SourceVariable = "DECKLE_HOME_REHEARSAL_SOURCE";
    public const string TargetVariable = "DECKLE_HOME_REHEARSAL_TARGET";
    public const string CreateSpaceVariable = "DECKLE_HOME_CREATE_TEST_SPACE";

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static string? Variable(string name) =>
        Environment.GetEnvironmentVariable(name) is { } value && value.Trim().Length > 0 ? value.Trim() : null;

    static void Write(string text) => TestContext.Current.TestOutputHelper?.WriteLine(text);

    [Fact(Explicit = true)]
    public async Task RecreateOnTestSpace()
    {
        string? source = Variable(SourceVariable);
        string? target = Variable(TargetVariable);
        Assert.SkipWhen(source is null || target is null,
            $"Répétition non lancée : définis {SourceVariable} (alias de l'espace source, par exemple home) "
            + $"et {TargetVariable} (alias d'un espace de TEST vide, déclaré dans spaces.json).");

        AnytypeCredentials credentials = AnytypeCredentials.Load();
        AnytypeSpaceAliases aliases = AnytypeSpaceAliases.Load(credentials.SpaceId);
        using var api = new AnytypeApiClient(credentials);
        string sheetPath = HomeReplaySheet.ResolvePath(source!, DateTimeOffset.Now);

        HomeRehearsalResult result = await new HomeRecreationRehearsal(api, aliases)
            .RunAsync(source!, target!, sheetPath, Ct);

        Write(result.Summary());
        Assert.True(File.Exists(sheetPath));
    }

    [Fact(Explicit = true)]
    public async Task CreateTestSpace()
    {
        string? name = Variable(CreateSpaceVariable);
        Assert.SkipWhen(name is null,
            $"Création non lancée : définis {CreateSpaceVariable} avec le nom exact de l'espace de test à créer.");

        AnytypeCredentials credentials = AnytypeCredentials.Load();
        using var api = new AnytypeApiClient(credentials);

        // A second run must not make a second space under the same name.
        IReadOnlyList<AnytypeSpaceSummary> spaces = await api.ListSpacesAsync(Ct);
        AnytypeSpaceSummary? existing = spaces.FirstOrDefault(space =>
            string.Equals(space.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        Assert.True(existing is null,
            $"Un espace porte déjà le nom « {name} » (id {existing?.Id}) : rien n'a été créé. "
            + "Choisis un autre nom, ou déclare cet espace dans spaces.json.");

        AnytypeSpaceSummary created = await api.CreateSpaceAsync(
            name!, "Espace de test de la répétition de recréation Home : il ne reçoit que des copies.", Ct);

        Write($"Espace créé : « {created.Name} », id {created.Id}.");
        Write("Il n'est visible que du bot tant que tu ne l'as pas rejoint depuis l'app.");
        Write("Ligne à ajouter dans l'objet « aliases » de spaces.json :");
        Write($"\"home-test\": \"{created.Id}\"");
    }
}
