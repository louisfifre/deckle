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
    public const string ExportSourceVariable = "DECKLE_HOME_EXPORT_SOURCE";
    public const string ReplaySheetVariable = "DECKLE_HOME_REPLAY_SHEET";
    public const string ReplayApiUrlVariable = "DECKLE_HOME_REPLAY_API_URL";
    public const string ReplayApiKeyVariable = "DECKLE_HOME_REPLAY_API_KEY";
    public const string ReplayApiVersionVariable = "DECKLE_HOME_REPLAY_API_VERSION";
    public const string ReplaySpaceIdVariable = "DECKLE_HOME_REPLAY_SPACE_ID";
    const string ExplicitTargetAlias = "home-replay-target";

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

    // The two halves of a run split across networks. ExportSheet writes the
    // sheet of DECKLE_HOME_EXPORT_SOURCE (an alias) and prints its path;
    // ReplaySheetOnTestSpace replays DECKLE_HOME_REPLAY_SHEET (that path) on
    // DECKLE_HOME_REHEARSAL_TARGET (an EMPTY test space), then compares.
    [Fact(Explicit = true)]
    public async Task ExportSheet()
    {
        string? source = Variable(ExportSourceVariable);
        Assert.SkipWhen(source is null,
            $"Export non lancé : définis {ExportSourceVariable} (alias de l'espace source, par exemple home).");

        AnytypeCredentials credentials = AnytypeCredentials.Load();
        AnytypeSpaceAliases aliases = AnytypeSpaceAliases.Load(credentials.SpaceId);
        using var api = new AnytypeApiClient(credentials);
        string sheetPath = HomeReplaySheet.ResolvePath(source!, DateTimeOffset.Now);

        HomeReplaySheetReport report = await new HomeRecreationRehearsal(api, aliases)
            .ExportAsync(source!, sheetPath, Ct);

        Write($"Feuille écrite : {sheetPath}");
        Write($"Objets exportés : {report.Exported}.");
        Assert.True(File.Exists(sheetPath));
    }

    [Fact(Explicit = true)]
    public async Task ReplaySheetOnTestSpace()
    {
        string? sheetPath = Variable(ReplaySheetVariable);
        string? target = Variable(TargetVariable);
        Assert.SkipWhen(sheetPath is null || target is null,
            $"Rejeu non lancé : définis {ReplaySheetVariable} (chemin d'une feuille exportée) "
            + $"et {TargetVariable} (alias d'un espace de TEST vide, déclaré dans spaces.json).");

        AnytypeCredentials credentials = AnytypeCredentials.Load();
        AnytypeSpaceAliases aliases = AnytypeSpaceAliases.Load(credentials.SpaceId);
        using var api = new AnytypeApiClient(credentials);

        HomeRehearsalResult result = await new HomeRecreationRehearsal(api, aliases)
            .ReplayFromSheetAsync(sheetPath!, target!, Ct);

        Write(result.Summary());
    }

    // The replay on a bot the machine's credentials do not name: another
    // Anytype network, reached through a local tunnel, with a disposable test
    // account. The endpoint, its key and the target space id come from
    // variables; nothing is saved, and the machine's own credentials are not
    // read. The target is a throwaway alias over that id.
    [Fact(Explicit = true)]
    public async Task ReplaySheetOnExplicitEndpoint()
    {
        string? sheetPath = Variable(ReplaySheetVariable);
        string? url = Variable(ReplayApiUrlVariable);
        string? key = Variable(ReplayApiKeyVariable);
        string? spaceId = Variable(ReplaySpaceIdVariable);
        Assert.SkipWhen(sheetPath is null || url is null || key is null || spaceId is null,
            $"Rejeu non lancé : définis {ReplaySheetVariable}, {ReplayApiUrlVariable} (par exemple http://127.0.0.1:41012), "
            + $"{ReplayApiKeyVariable} (clé du bot de test) et {ReplaySpaceIdVariable} (id d'un espace de TEST vide).");

        var credentials = new AnytypeCredentials(url!, Variable(ReplayApiVersionVariable) ?? "2025-11-08", key!, spaceId!);
        var aliases = new AnytypeSpaceAliases(new Dictionary<string, string> { [ExplicitTargetAlias] = spaceId! });
        using var api = new AnytypeApiClient(credentials);

        HomeRehearsalResult result = await new HomeRecreationRehearsal(api, aliases)
            .ReplayFromSheetAsync(sheetPath!, ExplicitTargetAlias, Ct);

        Write(result.Summary());
    }

    // The comparison alone, on a target already replayed: it reads and
    // writes nothing but the compare file beside the sheet.
    [Fact(Explicit = true)]
    public async Task CompareSheetOnExplicitEndpoint()
    {
        string? sheetPath = Variable(ReplaySheetVariable);
        string? url = Variable(ReplayApiUrlVariable);
        string? key = Variable(ReplayApiKeyVariable);
        string? spaceId = Variable(ReplaySpaceIdVariable);
        Assert.SkipWhen(sheetPath is null || url is null || key is null || spaceId is null,
            $"Comparaison non lancée : définis {ReplaySheetVariable}, {ReplayApiUrlVariable}, "
            + $"{ReplayApiKeyVariable} et {ReplaySpaceIdVariable}.");

        var credentials = new AnytypeCredentials(url!, Variable(ReplayApiVersionVariable) ?? "2025-11-08", key!, spaceId!);
        using var api = new AnytypeApiClient(credentials);

        HomeComparison comparison = await new HomeRecreationComparer(api).CompareAsync(
            HomeReplaySheet.Load(sheetPath!), spaceId!, HomeReplaySheet.Beside(sheetPath!, "compare"), Ct);

        Write($"Valeurs : {comparison.ValuesCompared} comparée(s), {comparison.ValuesDifferent} différente(s) ; "
            + $"objets égaux {comparison.Equal}, différents {comparison.Different}, manquants {comparison.Missing}, "
            + $"en trop {comparison.Extra}.");
        foreach ((string field, int count) in comparison.DifferentByField)
            Write($"  {field} : {count}");
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
        Write("Il n'est visible que du bot : personne ne peut t'y faire entrer. Pour un espace que tu dois voir, "
            + "crée-le dans l'app et donne au bot un lien d'invitation.");
        Write("Ligne à ajouter dans l'objet « aliases » de spaces.json :");
        Write($"\"home-test\": \"{created.Id}\"");
    }
}
