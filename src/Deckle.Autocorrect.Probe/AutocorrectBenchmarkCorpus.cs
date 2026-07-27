namespace Deckle.Autocorrect.Probe;

// Versioned gold corpus shared by the quality gate and the offline benchmark.
// Each scenario is still driven as physical key events through AutocorrectEngine;
// moving the data here prevents the benchmark and the invariant test from
// quietly measuring different products.
internal static class AutocorrectBenchmarkCorpus
{
    public static IReadOnlyList<KeyboardScenario> All { get; } =
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

    private static KeyboardScenario Fixes(
        string name,
        string typed,
        string expected,
        params (string Original, string Replacement)[] corrections) =>
        new(name, typed, expected,
            corrections.Select(pair =>
                new ExpectedCorrection(pair.Original, pair.Replacement)).ToArray());

    private static KeyboardScenario Keeps(string name, string text) =>
        new(name, text, text, []);
}

internal sealed record KeyboardScenario(
    string Name,
    string Typed,
    string Expected,
    IReadOnlyList<ExpectedCorrection> Corrections);

internal readonly record struct ExpectedCorrection(string Original, string Replacement);
