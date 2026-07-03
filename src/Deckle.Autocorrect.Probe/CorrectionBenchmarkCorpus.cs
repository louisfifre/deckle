namespace Deckle.Autocorrect.Probe;

internal static class CorrectionBenchmarkCorpus
{
    public static IReadOnlyList<CorrectionBenchmarkCase> All { get; } = new[]
    {
        Case("la_location", "homophone", 0, 1, "je suis la", "je suis là"),
        Case("la_determiner", "literal", 0, 0, "je suis la personne qui arrive", "je suis là personne qui arrive"),
        Case("a_auxiliary", "homophone", 0, 0, "il a dit que c'était prêt", "il à dit que c'était prêt"),
        Case("a_preposition", "homophone", 0, 1, "je vais a Paris demain", "je vais à Paris demain"),
        Case("ou_question", "homophone", 0, 1, "je ne sais pas ou il est", "je ne sais pas où il est"),
        Case("ou_choice", "literal", 0, 0, "tu veux du thé ou du café", "tu veux du thé où du café"),
        Case("sur_certain", "homophone", 0, 1, "je suis sur de moi", "je suis sûr de moi"),
        Case("sur_surface", "literal", 0, 0, "pose le livre sur la table", "pose le livre sûr la table"),
        Case("ca_subject", "homophone", 0, 1, "ca marche très bien", "ça marche très bien"),
        Case("du_participle", "homophone", 0, 1, "j'ai du partir tôt", "j'ai dû partir tôt"),
        Case("du_article", "literal", 0, 0, "je prends du pain", "je prends dû pain"),

        Case("participle_after_avoir", "verb-ending", 0, 1, "j'ai manger trop vite", "j'ai mangé trop vite", "j'ai mangez trop vite"),
        Case("infinitive_after_vais", "verb-ending", 0, 0, "je vais manger ce soir", "je vais mangé ce soir", "je vais mangez ce soir"),
        Case("infinitive_after_pour", "verb-ending", 0, 0, "je passe pour vérifier", "je passe pour vérifié", "je passe pour vérifiez"),
        Case("participle_c_est", "verb-ending", 0, 1, "c'est arriver hier", "c'est arrivé hier", "c'est arrivez hier"),
        Case("infinitive_il_faut", "verb-ending", 0, 0, "il faut corriger ce texte", "il faut corrigé ce texte", "il faut corrigez ce texte"),
        Case("participle_adjective_trap", "verb-ending", 0, 0, "il n'y a rien de cassé", "il n'y a rien de casser", "il n'y a rien de cassez"),
        Case("second_plural_present", "verb-ending", 0, 0, "vous testez demain", "vous tester demain", "vous testé demain"),
        Case("infinitive_after_pouvez", "verb-ending", 0, 1, "vous pouvez testez demain", "vous pouvez tester demain", "vous pouvez testé demain"),

        Case("feminine_singular", "agreement", 0, 1, "la porte est ouvert", "la porte est ouverte", "la porte est ouverts", "la porte est ouvertes"),
        Case("masculine_singular", "agreement", 0, 0, "un fichier ouvert reste visible", "un fichier ouverte reste visible", "un fichier ouverts reste visible"),
        Case("feminine_plural_participle", "agreement", 0, 1, "les données sont stocké localement", "les données sont stockées localement", "les données sont stockée localement", "les données sont stockés localement"),
        Case("masculine_plural_participle", "agreement", 0, 1, "les fichiers sont ouvert", "les fichiers sont ouverts", "les fichiers sont ouverte", "les fichiers sont ouvertes"),
        Case("feminine_plural_subject", "agreement", 0, 1, "elles sont parti tôt", "elles sont parties tôt", "elles sont partie tôt", "elles sont partis tôt"),
        Case("masculine_plural_subject", "agreement", 0, 0, "ils sont partis tôt", "ils sont parties tôt", "ils sont partie tôt"),
        Case("plural_adjective", "agreement", 0, 1, "des erreurs simple restent visibles", "des erreurs simples restent visibles"),
        Case("singular_adjective", "agreement", 0, 0, "une erreur simple reste visible", "une erreur simples reste visible"),

        Case("literal_la_build", "literal", 0, 0, "je lance la build locale", "je lance là build locale"),
        Case("literal_a_variable", "literal", 0, 0, "la variable a une valeur", "la variable à une valeur"),
        Case("literal_ou_api", "literal", 0, 0, "on garde HTTP ou WebSocket", "on garde HTTP où WebSocket"),
    };

    private static CorrectionBenchmarkCase Case(
        string id,
        string category,
        int literalIndex,
        int goldIndex,
        params string[] candidates)
    {
        if ((uint)literalIndex >= candidates.Length)
            throw new ArgumentOutOfRangeException(nameof(literalIndex));
        if ((uint)goldIndex >= candidates.Length)
            throw new ArgumentOutOfRangeException(nameof(goldIndex));
        if (candidates.Distinct(StringComparer.Ordinal).Count() != candidates.Length)
            throw new ArgumentException("Benchmark candidates must be distinct.", nameof(candidates));

        return new CorrectionBenchmarkCase(id, category, literalIndex, goldIndex, candidates);
    }
}
