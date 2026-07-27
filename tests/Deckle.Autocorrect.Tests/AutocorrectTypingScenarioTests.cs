using Deckle.Autocorrect;
using System.Text.Json;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Whole-flow keyboard scenarios built from phrases and fault classes observed in
// Louis's own correction telemetry. They drive physical keys through the real
// decoder/tracker/policy chain and assert the resulting text surface, so a test
// fails on a wrong decision as well as on a malformed injection tail.
[Trait("Category", "integration")]
public sealed class AutocorrectTypingScenarioTests
{
    [Fact]
    public void FastFrenchTypingRestoresResidualAccentsAndOneKeySlip()
    {
        FrequencyLexicon french = Lexicon(
            "il", "y", "a", "plein", "de", "fautes", "non", "corrigées",
            "alors", "avant", "ça", "allait", "un", "peu", "mieux");
        using var h = Harness(french);

        h.Type("Il y a plein de fautes non corrigees alors qu'avant ca allait un pru mieux.");

        Assert.Equal(
            "Il y a plein de fautes non corrigées alors qu'avant ça allait un peu mieux.",
            h.VisibleText);
        Assert.Collection(
            h.Applied,
            correction => Assert.Equal(("corrigees", "corrigées"),
                (correction.Original, correction.Replacement)),
            correction => Assert.Equal(("ca", "ça"),
                (correction.Original, correction.Replacement)),
            correction => Assert.Equal(("pru", "peu"),
                (correction.Original, correction.Replacement)));
    }

    [Fact]
    public void LongFrenchSentenceKeepsItsWordsWhileRestoringDiacritics()
    {
        FrequencyLexicon french = Lexicon(
            "je", "viens", "de", "me", "dire", "que", "ce", "serait",
            "intéressant", "pour", "la", "correction", "en", "fait", "utiliser",
            "les", "capacités", "des", "scanner", "tout", "que", "écris");
        FrequencyLexicon english = Lexicon("llm");
        using var h = Harness(french, english);

        h.Type(
            "Je viens de me dire que ce serait interessant pour la correction en fait, "
            + "utiliser les capacites des LLM pour scanner tout ce que j'ecris.");

        Assert.Equal(
            "Je viens de me dire que ce serait intéressant pour la correction en fait, "
            + "utiliser les capacités des LLM pour scanner tout ce que j'écris.",
            h.VisibleText);
    }

    [Fact]
    public void PackagedCompositionKeepsObservedTechnicalTermsLiteral()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        FrequencyLexicon french = FrequencyLexicon.LoadTsvGz(Path.Combine(
            dataDir, AutocorrectLexiconArtifacts.FrenchFileName));
        var english = new GlobalEnglishLexicon(
            AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDir));
        using var h = Harness(french, english);

        const string sentence = "Les docs du repo passent dans la telemetry pour jauger les logs.";
        h.Type(sentence);

        Assert.Equal(sentence, h.VisibleText);
        Assert.Empty(h.Applied);
        Assert.Empty(h.Injector.Calls);
    }

    [Theory]
    [InlineData("chrecher", "chercher")]
    [InlineData("bonjuor", "bonjour")]
    [InlineData("automatiqurment", "automatiquement")]
    [InlineData("beosin", "besoin")]
    [InlineData("preaprer", "préparer")]
    public void PackagedCompositionRepairsObservedSingleKeySlips(
        string typed,
        string expected)
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        FrequencyLexicon french = FrequencyLexicon.LoadTsvGz(Path.Combine(
            dataDir, AutocorrectLexiconArtifacts.FrenchFileName));
        var english = new GlobalEnglishLexicon(
            AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDir));
        using var h = Harness(french, english);

        h.Type(typed + " ");

        Assert.Equal(expected + " ", h.VisibleText);
        Assert.Equal(
            (typed, expected),
            (Assert.Single(h.Applied).Original, Assert.Single(h.Applied).Replacement));
    }

    [Fact]
    public void LegacyAdoptedBareFrenchFormIsPurgedBeforeRealTyping()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        FrequencyLexicon french = FrequencyLexicon.LoadTsvGz(Path.Combine(
            dataDir, AutocorrectLexiconArtifacts.FrenchFileName));
        var english = new GlobalEnglishLexicon(
            AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDir));
        IPairDisambiguator context = BigramPairDisambiguator.LoadTsvGz(Path.Combine(
            dataDir, AutocorrectLexiconArtifacts.PairBigramsFrenchFileName));
        string path = Path.Combine(
            Path.GetTempPath(), $"deckle-typing-dict-{Guid.NewGuid():N}.json");

        var data = new PersonalDictionaryData
        {
            SchemaVersion = PersonalDictionaryData.CurrentSchemaVersion,
        };
        data.Words.Add(Adopted("prepare"));
        data.Words.Add(Adopted("telemetry"));
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));

        try
        {
            var admission = new PersonalWordAdmission(french, AccentIndex.Build(french), english);
            using var dictionary = new PersonalDictionary(path, wordAdmission: admission.Allows);
            VerbMorphology verbs = VerbMorphology.LoadTsvGz(Path.Combine(
                dataDir, AutocorrectLexiconArtifacts.VerbMorphologyFrenchFileName));
            using var h = Harness(french, english, dictionary, context, verbs);

            h.Type("Je prepare le terrain.");

            Assert.Equal(1, dictionary.RemovedOnLoad);
            Assert.False(dictionary.IsAdopted("prepare"));
            Assert.True(dictionary.IsAdopted("telemetry"));
            Assert.Equal("Je prépare le terrain.", h.VisibleText);
            Assert.Contains(h.Applied, correction =>
                correction.Original == "prepare" && correction.Replacement == "prépare");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SubjectAgreementTypoOutranksAnAccentOnlyFalseFriend()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        FrequencyLexicon french = FrequencyLexicon.LoadTsvGz(Path.Combine(
            dataDir, AutocorrectLexiconArtifacts.FrenchFileName));
        var english = new GlobalEnglishLexicon(
            AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDir));
        AccentIndex index = AccentIndex.Build(french);
        var typo = new ConservativeTypoCorrector(
            french,
            english,
            accentIndex: index,
            verbs: VerbMorphology.LoadTsvGz(Path.Combine(
                dataDir, AutocorrectLexiconArtifacts.VerbMorphologyFrenchFileName)));
        var policy = new CompositeCorrectionPolicy(
            new ElisionCorrector(french, english),
            typo,
            new DiacriticsRestorer(french, english, index));
        using var h = new AutocorrectEngineHarness(policy, french: french, english: english);
        h.Settings.Apps["codex"] = true;
        h.Prober.Surface = AutocorrectEngineHarness.Editable("codex");
        Assert.True(h.Start());

        h.Type("ce que tu proposees me convient.");

        Assert.Equal("ce que tu proposes me convient.", h.VisibleText);
        Assert.Contains(h.Applied, correction =>
            correction.Original == "proposees" && correction.Replacement == "proposes");
        Assert.DoesNotContain(h.Applied, correction => correction.Replacement == "proposées");
    }

    private static AutocorrectEngineHarness Harness(
        FrequencyLexicon french,
        IFrequencyLexicon? english = null,
        PersonalDictionary? dictionary = null,
        IPairDisambiguator? context = null,
        VerbMorphology? verbs = null)
    {
        AutocorrectPolicySet policies = AutocorrectPolicySet.Create(
            french,
            english,
            AccentIndex.Build(french),
            context,
            dictionary,
            verbs: verbs);
        var harness = new AutocorrectEngineHarness(
            policies.Policy,
            dictionary: dictionary,
            french: french,
            english: english);
        harness.Settings.Apps["codex"] = true;
        harness.Prober.Surface = AutocorrectEngineHarness.Editable("codex");
        Assert.True(harness.Start());
        return harness;
    }

    private static FrequencyLexicon Lexicon(params string[] words)
    {
        string rows = string.Join(
            '\n',
            words
                .Distinct(StringComparer.Ordinal)
                .Select((word, index) => $"{word}\t{1000 - index}"));
        return FrequencyLexicon.LoadTsv(new StringReader(rows));
    }

    private static WordEntry Adopted(string word)
    {
        var now = new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);
        return new WordEntry
        {
            Word = word,
            Category = PersonalWordCategory.Anglicism,
            CleanOccurrences = 3,
            CleanDays = new Dictionary<string, int>
            {
                ["2026-07-25"] = 1,
                ["2026-07-26"] = 2,
            },
            FirstSeenUtc = now.AddDays(-1),
            LastSeenUtc = now,
        };
    }
}
