namespace Deckle.Autocorrect.Onnx;

internal readonly record struct CandidateCompletionPlan(int Start, int Count)
{
    public int EndExclusive => Start + Count;

    public static CandidateCompletionPlan[] Create(IReadOnlyList<int[]> completions)
    {
        if (completions.Count == 0)
            return Array.Empty<CandidateCompletionPlan>();

        int commonPrefix = CommonPrefixLength(completions);
        int commonSuffix = CommonSuffixLength(completions, commonPrefix);

        var plans = new CandidateCompletionPlan[completions.Count];
        bool hasEmptyDiscriminator = false;
        for (int i = 0; i < completions.Count; i++)
        {
            int count = completions[i].Length - commonPrefix - commonSuffix;
            if (count <= 0)
                hasEmptyDiscriminator = true;

            plans[i] = new CandidateCompletionPlan(commonPrefix, count);
        }

        if (!hasEmptyDiscriminator)
            return plans;

        for (int i = 0; i < completions.Count; i++)
            plans[i] = new CandidateCompletionPlan(0, completions[i].Length);

        return plans;
    }

    private static int CommonPrefixLength(IReadOnlyList<int[]> completions)
    {
        int minLength = completions.Min(static c => c.Length);
        int prefix = 0;
        while (prefix < minLength)
        {
            int token = completions[0][prefix];
            for (int i = 1; i < completions.Count; i++)
                if (completions[i][prefix] != token)
                    return prefix;

            prefix++;
        }

        return prefix;
    }

    private static int CommonSuffixLength(IReadOnlyList<int[]> completions, int commonPrefix)
    {
        int minLength = completions.Min(static c => c.Length);
        int suffix = 0;
        while (suffix < minLength - commonPrefix)
        {
            int token = completions[0][completions[0].Length - 1 - suffix];
            for (int i = 1; i < completions.Count; i++)
            {
                int[] completion = completions[i];
                if (completion[completion.Length - 1 - suffix] != token)
                    return suffix;
            }

            suffix++;
        }

        return suffix;
    }
}
