using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// A space plan is written in English and built in the language of a terms file.
// The shipped plans (project management, Home) and their terms files are held
// together here: the English file names exactly what the plan names, every
// other language yields a plan the engine accepts, and no translation points
// at an English text the plan no longer holds.
[Trait("Category", "unit")]
public sealed class PlanLocalizerTests
{
    public static TheoryData<string, string> Experiences => new()
    {
    };

    [Theory]
    [MemberData(nameof(Experiences))]
    public void TheEnglishTermsNameWhatTheBasePlanNames(string plan, string terms)
    {
        JsonObject basePlan = Read(plan);
        JsonObject localized = PlanLocalizer.Localize(basePlan, Read(terms + ".en.json"));
        Assert.Equal(basePlan.ToJsonString(), localized.ToJsonString());
    }

    [Theory]
    [MemberData(nameof(Experiences))]
    public void EveryLanguageYieldsAPlanTheEngineAccepts(string plan, string terms)
    {
        foreach (string file in Languages(terms))
        {
            SpacePlan parsed = SpacePlan.Parse(PlanLocalizer.Localize(Read(plan), Read(file)));
            Assert.Empty(SpacePlanValidator.Validate(parsed));
        }
    }

    [Theory]
    [MemberData(nameof(Experiences))]
    public void EveryTranslatedTextIsStillInThePlan(string plan, string terms)
    {
        // An English text edited in the plan leaves its translation behind, and
        // the word silently falls back to English: the orphan shows it here.
        var strings = new HashSet<string>(StringComparer.Ordinal);
        Collect(Read(plan), strings);
        foreach (string file in Languages(terms))
            if (Read(file)["texts"] is JsonObject texts)
                foreach ((string key, _) in texts)
                {
                    string english = key.Split('|')[0];
                    Assert.True(strings.Contains(english), $"{Path.GetFileName(file)}: « {english} » is not in the plan any more.");
                }
    }

    [Theory]
    [MemberData(nameof(Experiences))]
    public void EveryOptionHasAKeyNoOtherOptionHolds(string plan, string terms)
    {
        // The server keeps option keys unique across a whole space (see
        // PmSchemaManifestTests), and the key is how the terms name an option.
        _ = terms;
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonObject property in Read(plan)["properties"]!.AsArray().OfType<JsonObject>())
            foreach (JsonNode? tag in property["tags"] as JsonArray ?? [])
            {
                string owner = property["key"]!.GetValue<string>();
                string? key = (tag as JsonObject)?["key"]?.GetValue<string>();
                Assert.True(key is not null, $"{owner}: an option has no key.");
                Assert.True(seen.TryAdd(key!, owner), $"option key « {key} » under {owner} and {seen.GetValueOrDefault(key!)}.");
            }
    }

    [Fact]
    public void SchemaWordsComeByKeyAndFreeTextByItsEnglishText()
    {
        JsonObject localized = PlanLocalizer.Localize(SamplePlan(), FrenchTerms());

        Assert.Equal("État", localized["properties"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("Ouvert", localized["properties"]![0]!["tags"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("Idée", localized["types"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("Idées", localized["types"]![0]!["plural_name"]!.GetValue<string>());
        Assert.Equal("Projet", localized["types"]![1]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void AContextChoosesTheWordForWhatItDescribes()
    {
        JsonObject localized = PlanLocalizer.Localize(SamplePlan(), FrenchTerms());

        // French agrees « All » with the type the view lists.
        Assert.Equal("Toutes", localized["types"]![0]!["views"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("Tous", localized["types"]![1]!["views"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void AReferenceByNameFollowsWhatItPointsAt()
    {
        JsonObject localized = PlanLocalizer.Localize(SamplePlan(), FrenchTerms());

        // A widget names a view of its type, with that type's context.
        Assert.Equal("Toutes", localized["widgets"]![0]!["view"]!.GetValue<string>());
        // A filter names an option by its name.
        Assert.Equal("Ouvert", localized["types"]![0]!["views"]![0]!["filters"]![0]!["value"]![0]!.GetValue<string>());
        // A type names its default template, a template names the type it serves.
        Assert.Equal("Idée par défaut", localized["types"]![0]!["default_template"]!.GetValue<string>());
        Assert.Equal("Idée par défaut", localized["templates"]![0]!["name"]!.GetValue<string>());
        // A widget names a collection, and the collection carries the same name.
        Assert.Equal("object:Acteurs", localized["widgets"]![1]!["target"]!.GetValue<string>());
        Assert.Equal("Acteurs", localized["collections"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void AWordTheTermsDoNotNameStaysInEnglish()
    {
        JsonObject localized = PlanLocalizer.Localize(SamplePlan(), new JsonObject());
        Assert.Equal(SamplePlan().ToJsonString(), localized.ToJsonString());
    }

    private static JsonObject SamplePlan() => (JsonObject)JsonNode.Parse("""
    {
      "plan_version": 1,
      "properties": [
        { "key": "state", "name": "State", "format": "select", "tags": [ { "key": "open", "name": "Open", "color": "lime" } ] }
      ],
      "types": [
        { "key": "idea", "name": "Idea", "plural_name": "Ideas", "layout": "basic", "properties": [ "state" ],
          "default_template": "Default idea",
          "views": [ { "name": "All", "type": "list", "filters": [ { "property": "state", "condition": "in", "value": [ "Open" ] } ] } ] },
        { "key": "project", "name": "Project", "plural_name": "Projects", "layout": "basic", "properties": [ "state" ],
          "views": [ { "name": "All", "type": "list" } ] }
      ],
      "templates": [ { "name": "Default idea", "for": "idea", "blocks": [] } ],
      "collections": [ { "name": "Contacts", "items": [ "type:idea" ] } ],
      "widgets": [
        { "target": "type:idea", "layout": "view", "view": "All" },
        { "target": "object:Contacts", "layout": "compact_list" }
      ]
    }
    """)!;

    private static JsonObject FrenchTerms() => (JsonObject)JsonNode.Parse("""
    {
      "language": "fr",
      "types": { "idea": { "name": "Idée", "plural": "Idées" }, "project": { "name": "Projet", "plural": "Projets" } },
      "properties": { "state": "État" },
      "options": { "state": { "open": "Ouvert" } },
      "texts": { "All": "Tous", "All|type:idea": "Toutes", "Default idea": "Idée par défaut", "Contacts": "Acteurs" }
    }
    """)!;

    private static IEnumerable<string> Languages(string terms)
    {
        string directory = Path.Combine(RepoRoot(), Path.GetDirectoryName(terms)!);
        return Directory.GetFiles(directory, Path.GetFileName(terms) + ".*.json").Order(StringComparer.Ordinal);
    }

    private static void Collect(JsonNode? node, HashSet<string> strings)
    {
        switch (node)
        {
            case JsonObject obj: foreach ((_, JsonNode? v) in obj) Collect(v, strings); break;
            case JsonArray array: foreach (JsonNode? v in array) Collect(v, strings); break;
            case JsonValue value when value.TryGetValue(out string? s):
                strings.Add(s);
                if (s.StartsWith("object:", StringComparison.Ordinal)) strings.Add(s["object:".Length..]);
                break;
        }
    }

    private static JsonObject Read(string path) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.IsPathRooted(path) ? path : Path.Combine(RepoRoot(), path)))!;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
