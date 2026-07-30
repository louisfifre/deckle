using System.Collections.Frozen;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceDecisionInventory
{
    public const string SourceCorpus = "public_visible_development";

    public static IReadOnlyList<DecisionInventoryEntry> Build()
    {
        var entries = new DecisionInventoryEntry[CorrectionBenchmarkCorpus.All.Count];
        for (int ordinal = 0; ordinal < entries.Length; ordinal++)
        {
            CorrectionBenchmarkCase source = CorrectionBenchmarkCorpus.All[ordinal];
            TokenSpan[] literalTokens = Tokenize(source.Literal);
            var candidates = new List<DecisionCandidate>(source.Candidates.Length - 1);

            for (int candidateIndex = 0;
                candidateIndex < source.Candidates.Length;
                candidateIndex++)
            {
                if (candidateIndex == source.LiteralIndex)
                    continue;

                DecisionCandidate candidate = DeriveCandidate(
                    source.Literal,
                    literalTokens,
                    source.Candidates[candidateIndex]);
                if (!string.Equals(
                    Apply(source.Literal, candidate),
                    source.Candidates[candidateIndex],
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Candidate {candidateIndex} for public case {source.Id} did not reconstruct exactly.");
                }
                candidates.Add(candidate);
            }

            DecisionInput input = new(
                source.Literal,
                literalTokens.Select(static token => token.Value).ToArray(),
                candidates);
            DecisionTruth truth = new(
                [source.Gold],
                source.RequiresCorrection);
            DecisionProvenance provenance = new(
                source.Id,
                source.Category,
                SourceCorpus,
                DecisionCandidateFamilies.ForPublicCase(source.Id),
                ParentGroupId: null,
                SourceSessionGroupId: null,
                PunctuationVariantGroupId: null);
            entries[ordinal] = new DecisionInventoryEntry(
                ordinal,
                input,
                truth,
                provenance);
        }

        return entries;
    }

    public static string Apply(string literal, DecisionCandidate candidate)
    {
        if (candidate.Start < 0
            || candidate.Length < 0
            || candidate.Start > literal.Length - candidate.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(candidate));
        }

        return literal[..candidate.Start]
            + candidate.Replacement
            + literal[(candidate.Start + candidate.Length)..];
    }

    private static DecisionCandidate DeriveCandidate(
        string literal,
        IReadOnlyList<TokenSpan> literalTokens,
        string variant)
    {
        TokenSpan[] variantTokens = Tokenize(variant);
        if (literalTokens.Count != variantTokens.Length)
            throw new InvalidOperationException("A public candidate changed the token count.");

        int changedSlot = -1;
        for (int slot = 0; slot < literalTokens.Count; slot++)
        {
            if (string.Equals(
                literalTokens[slot].Value,
                variantTokens[slot].Value,
                StringComparison.Ordinal))
            {
                continue;
            }

            if (changedSlot >= 0)
                throw new InvalidOperationException("A public candidate changed more than one token.");
            changedSlot = slot;
        }

        if (changedSlot < 0)
            throw new InvalidOperationException("A public nonliteral candidate did not change a token.");

        TokenSpan literalToken = literalTokens[changedSlot];
        TokenSpan variantToken = variantTokens[changedSlot];
        if (!literal.AsSpan(0, literalToken.Start)
                .SequenceEqual(variant.AsSpan(0, variantToken.Start))
            || !literal.AsSpan(literalToken.End)
                .SequenceEqual(variant.AsSpan(variantToken.End)))
        {
            throw new InvalidOperationException(
                "A public candidate changed a separator or text outside its token.");
        }

        string identity = FormattableString.Invariant(
            $"{changedSlot}@{literalToken.Start}:{literalToken.Length}={variantToken.Value}");
        return new DecisionCandidate(
            identity,
            changedSlot,
            literalToken.Start,
            literalToken.Length,
            variantToken.Value);
    }

    private static TokenSpan[] Tokenize(string text)
    {
        var tokens = new List<TokenSpan>();
        int index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
            if (index == text.Length)
                break;

            int start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
                index++;
            tokens.Add(new TokenSpan(start, index - start, text[start..index]));
        }
        return tokens.ToArray();
    }

    private readonly record struct TokenSpan(int Start, int Length, string Value)
    {
        public int End => Start + Length;
    }
}

internal static class DecisionCandidateFamilies
{
    public const string DiacriticOnly = "diacritic_only";
    public const string TerminalInflection = "terminal_inflection";

    public static FrozenSet<string> Frozen { get; } =
        new[] { DiacriticOnly, TerminalInflection }
            .ToFrozenSet(StringComparer.Ordinal);

    public static FrozenDictionary<string, string> FrozenAssignments { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["la_location"] = DiacriticOnly,
            ["la_determiner"] = DiacriticOnly,
            ["a_auxiliary"] = DiacriticOnly,
            ["a_preposition"] = DiacriticOnly,
            ["ou_question"] = DiacriticOnly,
            ["ou_choice"] = DiacriticOnly,
            ["sur_certain"] = DiacriticOnly,
            ["sur_surface"] = DiacriticOnly,
            ["ca_subject"] = DiacriticOnly,
            ["du_participle"] = DiacriticOnly,
            ["du_article"] = DiacriticOnly,
            ["participle_after_avoir"] = TerminalInflection,
            ["infinitive_after_vais"] = TerminalInflection,
            ["infinitive_after_pour"] = TerminalInflection,
            ["participle_c_est"] = TerminalInflection,
            ["infinitive_il_faut"] = TerminalInflection,
            ["participle_adjective_trap"] = TerminalInflection,
            ["second_plural_present"] = TerminalInflection,
            ["infinitive_after_pouvez"] = TerminalInflection,
            ["feminine_singular"] = TerminalInflection,
            ["masculine_singular"] = TerminalInflection,
            ["feminine_plural_participle"] = TerminalInflection,
            ["masculine_plural_participle"] = TerminalInflection,
            ["feminine_plural_subject"] = TerminalInflection,
            ["masculine_plural_subject"] = TerminalInflection,
            ["plural_adjective"] = TerminalInflection,
            ["singular_adjective"] = TerminalInflection,
            ["literal_la_build"] = DiacriticOnly,
            ["literal_a_variable"] = DiacriticOnly,
            ["literal_ou_api"] = DiacriticOnly,
            ["literal_ratures"] = DiacriticOnly,
            ["literal_date"] = DiacriticOnly,
            ["duplicate_letter"] = TerminalInflection,
            ["qu_a_auxiliary"] = DiacriticOnly,
            ["qu_a_preposition"] = DiacriticOnly,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string ForPublicCase(string publicCaseId) =>
        FrozenAssignments.TryGetValue(publicCaseId, out string? family)
            ? family
            : throw new InvalidOperationException(
                $"Public case {publicCaseId} has no frozen candidate-family assignment.");
}

internal sealed record DecisionInput(
    string Literal,
    IReadOnlyList<string> Tokens,
    IReadOnlyList<DecisionCandidate> Candidates);

internal readonly record struct DecisionCandidate(
    string Identity,
    int SlotIndex,
    int Start,
    int Length,
    string Replacement)
{
    public SentenceEditCandidate ToSentenceEditCandidate() =>
        new(SlotIndex, Start, Length, Replacement);
}

internal sealed record DecisionTruth(
    IReadOnlyList<string> AcceptableFinals,
    bool RequiresEdit);

internal sealed record DecisionProvenance(
    string PublicCaseId,
    string PublicCategory,
    string SourceCorpus,
    string CandidateFamilyGroup,
    string? ParentGroupId,
    string? SourceSessionGroupId,
    string? PunctuationVariantGroupId);

internal sealed record DecisionInventoryEntry(
    int PublicOrdinal,
    DecisionInput Input,
    DecisionTruth Truth,
    DecisionProvenance Provenance);
