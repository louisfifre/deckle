namespace Deckle.Autocorrect;

// Mechanical permission gate for a future silent whole-sentence proposal. The
// judge answers "which sentence reads better"; this gate answers the separate
// safety question "is this still a bounded correction of the exact text on
// screen?". Both must pass. It deliberately rejects reflow, token insertion or
// deletion, digit changes and protected-literal rewrites before any injection.
public sealed class SentenceProposalGate
{
    private const int MaxTextLength = 512;
    private const int MaxBackspaces = 160;
    private const int MaxInsertedChars = 192;
    private const int AbsoluteEditCap = 24;
    private const double RelativeEditCap = 0.15;

    private readonly IFrequencyLexicon? _protectedLiterals;

    public SentenceProposalGate(IFrequencyLexicon? protectedLiterals = null) =>
        _protectedLiterals = protectedLiterals;

    public SentenceProposalGateVerdict Evaluate(
        string original,
        string proposed,
        string? typed = null)
    {
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(proposed))
            return Reject(SentenceProposalGateVerdict.Reasons.EmptyText);
        if (original.Length > MaxTextLength || proposed.Length > MaxTextLength)
            return Reject(SentenceProposalGateVerdict.Reasons.TextTooLong);
        if (!HasSafeWhitespace(original, requireSingleSpaces: false)
            || !HasSafeWhitespace(proposed, requireSingleSpaces: true))
            return Reject(SentenceProposalGateVerdict.Reasons.UnsafeWhitespace);
        if (string.Equals(original, proposed, StringComparison.Ordinal))
            return Reject(SentenceProposalGateVerdict.Reasons.Identity);

        string[] originalTokens = LexicalTokens(original);
        string[] proposedTokens = LexicalTokens(proposed);
        if (originalTokens.Length != proposedTokens.Length)
            return Reject(SentenceProposalGateVerdict.Reasons.TokenCountChanged);
        // The raw typed side is the authority when available. It lets a proposal
        // safely restore a protected literal that the commit stage already
        // damaged (typed "docs", on-screen "dos", proposed "docs") instead of
        // treating the damaged final as ground truth.
        string protectedBaseline = string.IsNullOrWhiteSpace(typed) ? original : typed;
        string[] baselineTokens = LexicalTokens(protectedBaseline);
        if (!DigitRuns(baselineTokens).SequenceEqual(DigitRuns(proposedTokens)))
            return Reject(SentenceProposalGateVerdict.Reasons.DigitsChanged);
        if (!ProtectedTokens(baselineTokens).SequenceEqual(ProtectedTokens(proposedTokens)))
            return Reject(SentenceProposalGateVerdict.Reasons.ProtectedLiteralChanged);
        if (!StructuralCharacters(original).SequenceEqual(StructuralCharacters(proposed)))
            return Reject(SentenceProposalGateVerdict.Reasons.StructuralCharactersChanged);

        int maxEdits = Math.Min(
            AbsoluteEditCap,
            Math.Max(2, (int)Math.Ceiling(original.Length * RelativeEditCap)));
        if (EditDistance(original, proposed, maxEdits) > maxEdits)
            return Reject(SentenceProposalGateVerdict.Reasons.EditBudgetExceeded);

        InjectionPlan plan = InjectionPlan.Compute(original, proposed);
        if (plan.IsNoOp)
            return Reject(SentenceProposalGateVerdict.Reasons.Identity);
        if (plan.Backspaces > MaxBackspaces || plan.Text.Length > MaxInsertedChars)
            return Reject(SentenceProposalGateVerdict.Reasons.InjectionBudgetExceeded);

        return new SentenceProposalGateVerdict(true, SentenceProposalGateVerdict.Reasons.Accepted, plan);
    }

    private IEnumerable<ProtectedToken> ProtectedTokens(IReadOnlyList<string> tokens)
    {
        if (_protectedLiterals is null)
            yield break;
        for (int index = 0; index < tokens.Count; index++)
        {
            string token = tokens[index];
            if (_protectedLiterals.Contains(token.ToLowerInvariant()))
                yield return new ProtectedToken(index, token);
        }
    }

    private static bool HasSafeWhitespace(string text, bool requireSingleSpaces)
    {
        if (text.Length == 0 || text[0] == ' ' || text[^1] == ' ')
            return false;
        bool previousSpace = false;
        foreach (char c in text)
        {
            if (char.IsControl(c))
                return false;
            if (char.IsWhiteSpace(c) && c != ' ')
                return false;
            if (requireSingleSpaces && c == ' ' && previousSpace)
                return false;
            previousSpace = c == ' ';
        }
        return true;
    }

    private static string[] LexicalTokens(string text)
    {
        var tokens = new List<string>();
        var token = new System.Text.StringBuilder();
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c) || c is '\'' or '\u2019' or '-' or '_')
            {
                token.Append(c);
            }
            else if (token.Length > 0)
            {
                tokens.Add(token.ToString());
                token.Clear();
            }
        }
        if (token.Length > 0)
            tokens.Add(token.ToString());
        return tokens.ToArray();
    }

    private static IEnumerable<DigitRun> DigitRuns(IReadOnlyList<string> tokens)
    {
        for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            string token = tokens[tokenIndex];
            var run = new System.Text.StringBuilder();
            int runIndex = 0;
            foreach (char c in token)
            {
                if (char.IsDigit(c))
                {
                    run.Append(c);
                }
                else if (run.Length > 0)
                {
                    yield return new DigitRun(tokenIndex, runIndex++, run.ToString());
                    run.Clear();
                }
            }
            if (run.Length > 0)
                yield return new DigitRun(tokenIndex, runIndex, run.ToString());
        }
    }

    // French prose punctuation may be corrected. Everything else is structural
    // syntax (code, paths, mentions, markdown) and must survive byte-for-byte in
    // order; the model does not get silent authority over it.
    private static IEnumerable<char> StructuralCharacters(string text)
    {
        foreach (char c in text)
            if (!char.IsLetterOrDigit(c)
                && c != ' '
                && c is not '.' and not ',' and not ';' and not ':'
                    and not '!' and not '?' and not '…'
                    and not '\'' and not '\u2019' and not '-'
                    and not '(' and not ')' and not '«' and not '»' and not '"')
            {
                yield return c;
            }
    }

    // Bounded Levenshtein: rows whose minimum already exceeds the permission
    // budget stop immediately, avoiding quadratic work on hostile proposals.
    private static int EditDistance(string left, string right, int limit)
    {
        if (Math.Abs(left.Length - right.Length) > limit)
            return limit + 1;

        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        int[] current = new int[right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            int rowMin = current[0];
            for (int j = 1; j <= right.Length; j++)
            {
                int substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
                rowMin = Math.Min(rowMin, current[j]);
            }
            if (rowMin > limit)
                return limit + 1;
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    private static SentenceProposalGateVerdict Reject(string reason) =>
        new(false, reason, default);

    private readonly record struct ProtectedToken(int Index, string Text);
    private readonly record struct DigitRun(int TokenIndex, int RunIndex, string Text);
}

public readonly record struct SentenceProposalGateVerdict(
    bool Accepted,
    string Reason,
    InjectionPlan Plan)
{
    public static class Reasons
    {
        public const string Accepted = "accepted";
        public const string EmptyText = "empty_text";
        public const string Identity = "identity";
        public const string TextTooLong = "text_too_long";
        public const string UnsafeWhitespace = "unsafe_whitespace";
        public const string TokenCountChanged = "token_count_changed";
        public const string DigitsChanged = "digits_changed";
        public const string ProtectedLiteralChanged = "protected_literal_changed";
        public const string StructuralCharactersChanged = "structural_characters_changed";
        public const string EditBudgetExceeded = "edit_budget_exceeded";
        public const string InjectionBudgetExceeded = "injection_budget_exceeded";
    }
}
