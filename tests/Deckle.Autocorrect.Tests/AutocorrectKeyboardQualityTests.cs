using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Versioned gold corpus for the deterministic, instant correction path. Every
// phrase is typed as physical key events through the production policy set.
// Wrong changes are a hard veto; recall and exact-sentence rate expose residue.
[Trait("Category", "integration")]
public sealed class AutocorrectKeyboardQualityTests(ITestOutputHelper output)
{
    private static readonly Scenario[] Corpus =
    [
        Fixes("long_accents",
            "Je viens de me dire que ce serait interessant pour la correction, utiliser les capacites pour scanner ce que j'ecris.",
            "Je viens de me dire que ce serait intéressant pour la correction, utiliser les capacités pour scanner ce que j'écris.",
            ("interessant", "intéressant"), ("capacites", "capacités"), ("ecris", "écris")),
        Fixes("common_residue",
            "Il y a plein de fautes non corrigees alors qu'avant ca allait mieux.",
            "Il y a plein de fautes non corrigées alors qu'avant ça allait mieux.",
            ("corrigees", "corrigées"), ("ca", "ça")),
        Fixes("transpositions",
            "Je dois chrecher les porprietes.",
            "Je dois chercher les propriétés.",
            ("chrecher", "chercher"), ("porprietes", "propriétés")),
        Fixes("observed_slips",
            "bonjuor automatiqurment beosin preaprer.",
            "bonjour automatiquement besoin préparer.",
            ("bonjuor", "bonjour"), ("automatiqurment", "automatiquement"),
            ("beosin", "besoin"), ("preaprer", "préparer")),
        Fixes("accent_agreement",
            "Il faut que je prepare le terrain.",
            "Il faut que je prépare le terrain.",
            ("prepare", "prépare")),
        Fixes("typo_agreement",
            "Ce que tu proposees me convient.",
            "Ce que tu proposes me convient.",
            ("proposees", "proposes")),
        Fixes("valid_form_agreement",
            "Tu mange vite.",
            "Tu manges vite.",
            ("mange", "manges")),
        Fixes("elisions",
            "cest deja clair et jai termine.",
            "c'est déjà clair et j'ai terminé.",
            ("cest", "c'est"), ("deja", "déjà"), ("jai", "j'ai"), ("termine", "terminé")),
        Fixes("missing_letter_and_accent",
            "J'y mettrai surment.",
            "J'y mettrai sûrement.",
            ("surment", "sûrement")),
        Fixes("subjunctive_and_noun_accent",
            "Il faut qu'on refelchisse a la depense.",
            "Il faut qu'on réfléchisse a la dépense.",
            ("refelchisse", "réfléchisse"), ("depense", "dépense")),
        Fixes("plural_accent",
            "Les hebergements sont prets.",
            "Les hébergements sont prêts.",
            ("hebergements", "hébergements"), ("prets", "prêts")),
        Fixes("short_transposition",
            "Investigue et juge al qualite.",
            "Investigue et juge la qualité.",
            ("al", "la"), ("qualite", "qualité")),
        Fixes("mixed_slips",
            "On mettera les docuements.",
            "On mettra les documents.",
            ("mettera", "mettra"), ("docuements", "documents")),
        Fixes("mixed_keyboard_and_accents",
            "Par exemple est dfe reserver les ficheiers.",
            "Par exemple est de réserver les fichiers.",
            ("dfe", "de"), ("reserver", "réserver"), ("ficheiers", "fichiers")),
        Fixes("dense_real_burst",
            "En anglias, les esapces sont facileemnt propsoer.",
            "En anglais, les espaces sont facilement proposer.",
            ("anglias", "anglais"), ("esapces", "espaces"),
            ("facileemnt", "facilement"), ("propsoer", "proposer")),
        Fixes("extra_letter",
            "Enfinn ilo faudra faire attention.",
            "Enfin il faudra faire attention.",
            ("Enfinn", "Enfin"), ("ilo", "il")),
        Fixes("adjacent_substitution",
            "Pas grave maus il faut continuer.",
            "Pas grave mais il faut continuer.",
            ("maus", "mais")),
        Fixes("missing_initial",
            "Ce qu'il y a ura sur le ticket.",
            "Ce qu'il y aura sur le ticket.",
            ("ura", "aura")),
        Fixes("accent_vs_transposition",
            "C'est bine de prendre les tickets.",
            "C'est bien de prendre les tickets.",
            ("bine", "bien")),
        Fixes("singular_model",
            "J'aurai un modele en local.",
            "J'aurai un modèle en local.",
            ("modele", "modèle")),
        Keeps("technical_literals",
            "Les docs du repo passent dans la telemetry pour jauger les logs."),
        Keeps("ordinary_content_words",
            "La date et les ratures restent dans le texte."),
        Keeps("auxiliary_a",
            "Il a dit que le test a réussi."),
    ];

    [Fact]
    public void ProductionKeyboardCorpusMeetsPrecisionFirstQualityGate()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        FrequencyLexicon french = FrequencyLexicon.LoadTsvGz(Path.Combine(
            dataDir, AutocorrectLexiconArtifacts.FrenchFileName));
        var english = new GlobalEnglishLexicon(
            AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDir));
        AutocorrectPolicySet policies = AutocorrectPolicySet.Create(
            french,
            english,
            AccentIndex.Build(french),
            BigramPairDisambiguator.LoadTsvGz(Path.Combine(
                dataDir, AutocorrectLexiconArtifacts.PairBigramsFrenchFileName)),
            verbs: VerbMorphology.LoadTsvGz(Path.Combine(
                dataDir, AutocorrectLexiconArtifacts.VerbMorphologyFrenchFileName)));

        int trueChanges = 0;
        int wrongChanges = 0;
        int goldChanges = 0;
        int exactScenarios = 0;
        var failures = new List<string>();

        foreach (Scenario scenario in Corpus)
        {
            using var harness = new AutocorrectEngineHarness(
                policies.Policy, french: french, english: english);
            harness.Settings.Apps["codex"] = true;
            harness.Prober.Surface = AutocorrectEngineHarness.Editable("codex");
            Assert.True(harness.Start());

            harness.Type(scenario.Typed, interKeyMs: 35);

            goldChanges += scenario.Corrections.Length;
            var unmatched = scenario.Corrections.ToList();
            foreach (CorrectionDecision applied in harness.Applied)
            {
                int match = unmatched.FindIndex(expected =>
                    expected.Original == applied.Original
                    && expected.Replacement == applied.Replacement);
                if (match >= 0)
                {
                    trueChanges++;
                    unmatched.RemoveAt(match);
                }
                else
                {
                    wrongChanges++;
                }
            }

            if (harness.VisibleText == scenario.Expected)
                exactScenarios++;
            else
                failures.Add($"{scenario.Name}: '{harness.VisibleText}' != '{scenario.Expected}'");
        }

        double precision = trueChanges + wrongChanges == 0
            ? 1.0
            : (double)trueChanges / (trueChanges + wrongChanges);
        double recall = goldChanges == 0 ? 1.0 : (double)trueChanges / goldChanges;
        double exactRate = (double)exactScenarios / Corpus.Length;
        string score =
            $"quality: precision={precision:P1} ({trueChanges}/{trueChanges + wrongChanges}), "
            + $"recall={recall:P1} ({trueChanges}/{goldChanges}), "
            + $"exact={exactRate:P1} ({exactScenarios}/{Corpus.Length}), wrong={wrongChanges}";
        output.WriteLine(score);
        foreach (string failure in failures)
            output.WriteLine(failure);

        Assert.True(wrongChanges == 0, score + Environment.NewLine + string.Join(Environment.NewLine, failures));
        Assert.True(recall >= 0.90, score + Environment.NewLine + string.Join(Environment.NewLine, failures));
        Assert.True(exactRate >= 0.85, score + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static Scenario Fixes(
        string name,
        string typed,
        string expected,
        params (string Original, string Replacement)[] corrections) =>
        new(name, typed, expected,
            corrections.Select(pair => new CorrectionPair(pair.Original, pair.Replacement)).ToArray());

    private static Scenario Keeps(string name, string text) => new(name, text, text, []);

    private sealed record Scenario(
        string Name,
        string Typed,
        string Expected,
        CorrectionPair[] Corrections);

    private readonly record struct CorrectionPair(string Original, string Replacement);
}
