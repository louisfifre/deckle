using System.Text.Json;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The space plan builder against a real Anytype space, in three explicit tests.
// They are maintenance tests: nothing here runs in a normal suite, and the
// endpoint, the key file and the space come from variables, never from the
// machine's own credentials (except the read, which only reads).
//
//   ReadSpaceToPlanFile      reads a configured space (alias) into a plan file.
//   BuildPlanOnTrialSpace    previews, applies and previews again on a THROWAWAY space.
//   CheckPlanOnTrialSpace    a preview alone: the 10-minute re-check after a build.
//
// Run with /p:VSTestCLIRunSettings=xUnit.Explicit=only. The runbook's section
// « Throwaway bot space for tests » brings the space and its key file up.
[Trait("Category", "maintenance")]
public sealed class SpacePlanTrialTests
{
    public const string ApiUrlVariable = "DECKLE_PLAN_TRIAL_API_URL";
    public const string KeyFileVariable = "DECKLE_PLAN_TRIAL_KEY_FILE";
    public const string SpaceIdVariable = "DECKLE_PLAN_TRIAL_SPACE_ID";
    public const string PlanFileVariable = "DECKLE_PLAN_FILE";
    public const string StagesVariable = "DECKLE_PLAN_STAGES";
    public const string ReadAliasVariable = "DECKLE_PLAN_READ_ALIAS";
    public const string ReadOutVariable = "DECKLE_PLAN_READ_OUT";
    public const string ReadPagesVariable = "DECKLE_PLAN_READ_PAGES";

    const string TrialAlias = "plan-trial";

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static string? Variable(string name) =>
        Environment.GetEnvironmentVariable(name) is { } value && value.Trim().Length > 0 ? value.Trim() : null;

    static void Write(string text) => TestContext.Current.TestOutputHelper?.WriteLine(text);

    static SpacePlanStages Stages() =>
        Variable(StagesVariable) is { } text
            ? text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Aggregate(SpacePlanStages.None, (all, word) => all | Enum.Parse<SpacePlanStages>(word, ignoreCase: true))
            : SpacePlanStages.Default;

    static (AnytypeApiClient Api, SpacePlanGestures Gestures)? TrialGestures()
    {
        string? url = Variable(ApiUrlVariable);
        string? keyFile = Variable(KeyFileVariable);
        string? spaceId = Variable(SpaceIdVariable);
        if (url is null || keyFile is null || spaceId is null || !File.Exists(keyFile)) return null;

        var credentials = new AnytypeCredentials(url, "2025-11-08", File.ReadAllText(keyFile).Trim(), spaceId);
        var aliases = new AnytypeSpaceAliases(new Dictionary<string, string> { [TrialAlias] = spaceId });
        var api = new AnytypeApiClient(credentials);
        return (api, new SpacePlanGestures(api, aliases));
    }

    static JsonObject LoadPlan()
    {
        string path = Variable(PlanFileVariable) ?? throw new InvalidOperationException($"{PlanFileVariable} is not set.");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    [Fact(Explicit = true)]
    public async Task ReadSpaceToPlanFile()
    {
        string? alias = Variable(ReadAliasVariable);
        string? output = Variable(ReadOutVariable);
        Assert.SkipWhen(alias is null || output is null,
            $"Not run: set {ReadAliasVariable} (a spaces.json alias) and {ReadOutVariable} (the plan file to write).");

        AnytypeCredentials machine = AnytypeCredentials.Load();
        AnytypeSpaceAliases aliases = AnytypeSpaceAliases.Load(machine.SpaceId);
        using var api = new AnytypeApiClient(machine);
        var gestures = new SpacePlanGestures(api, aliases);

        string[] pages = Variable(ReadPagesVariable)?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        JsonObject plan = await gestures.ReadAsync(alias!, pages, Ct);
        File.WriteAllText(output!, plan.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Write($"Plan written: {plan["properties"]?.AsArray().Count} properties, {plan["types"]?.AsArray().Count} types, "
            + $"{plan["templates"]?.AsArray().Count} templates, {plan["collections"]?.AsArray().Count} collections, "
            + $"{plan["queries"]?.AsArray().Count} queries.");
    }

    // Reads the throwaway space back into a plan file, to compare it with the plan
    // it was built from field by field.
    [Fact(Explicit = true)]
    public async Task ReadTrialSpaceToPlanFile()
    {
        var trial = TrialGestures();
        string? output = Variable(ReadOutVariable);
        Assert.SkipWhen(trial is null || output is null,
            $"Not run: set {ApiUrlVariable}, {KeyFileVariable}, {SpaceIdVariable} and {ReadOutVariable}.");
        using AnytypeApiClient api = trial!.Value.Api;
        string[] pages = Variable(ReadPagesVariable)?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        JsonObject plan = await trial.Value.Gestures.ReadAsync(TrialAlias, pages, Ct);
        File.WriteAllText(output!, plan.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Write($"Trial space read: {plan["types"]?.AsArray().Count} types, {plan["templates"]?.AsArray().Count} templates.");
    }

    [Fact(Explicit = true)]
    public async Task BuildPlanOnTrialSpace()
    {
        var trial = TrialGestures();
        Assert.SkipWhen(trial is null,
            $"Not run: set {ApiUrlVariable}, {KeyFileVariable} (a file holding the throwaway bot's key), {SpaceIdVariable} and {PlanFileVariable}.");
        using AnytypeApiClient api = trial!.Value.Api;
        SpacePlanGestures gestures = trial.Value.Gestures;
        JsonObject plan = LoadPlan();
        SpacePlanStages stages = Stages();

        SpacePlanPreview preview = await gestures.PreviewAsync(TrialAlias, plan, stages, Ct);
        Write("PREVIEW\n" + preview.Rendered);
        Assert.Empty(preview.Problems);
        Assert.Empty(preview.Conflicts);

        string outcome = await gestures.ApplyAsync(TrialAlias, preview.PreviewId, plan, stages, confirm: true, Ct);
        Write("APPLY\n" + outcome);

        SpacePlanPreview again = await gestures.CheckAsync(TrialAlias, plan, stages, Ct);
        Write("SECOND PREVIEW\n" + again.Rendered);
        Assert.Empty(again.Actions);
    }

    [Fact(Explicit = true)]
    public async Task CheckPlanOnTrialSpace()
    {
        var trial = TrialGestures();
        Assert.SkipWhen(trial is null,
            $"Not run: set {ApiUrlVariable}, {KeyFileVariable}, {SpaceIdVariable} and {PlanFileVariable}.");
        using AnytypeApiClient api = trial!.Value.Api;
        SpacePlanPreview check = await trial.Value.Gestures.CheckAsync(TrialAlias, LoadPlan(), Stages(), Ct);
        Write("CHECK\n" + check.Rendered);
        Assert.Empty(check.Actions);
    }
}
