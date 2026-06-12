using System.Text;

namespace Deckle.Input.Autocorrect.Evaluation;

// ── RestorationReport ───────────────────────────────────────────────────────
//
// The outcome of one offline evaluation pass: how the policy fared at putting
// accents back on text a QWERTY-US typist would have flattened. Tokens fall
// into two worlds and five classes:
//
//   accented reference (token != typed):
//     Restored   — output == reference  (the win)
//     Missed     — output == typed       (left bare; a miss, not a wound)
//     WrongForm  — output is neither     (corrected to the wrong accented form)
//   bare reference (token == typed):
//     Untouched        — output == reference (correctly left alone)
//     FalseCorrections — output != reference (THE killer: mangled a valid word)
//
// The killer metric is FalseCorrections: a wrongly altered valid word is worse
// than any number of misses, because the user typed it correctly.
public sealed class RestorationReport
{
    public long TotalTokens { get; set; }

    // Accented-reference world.
    public long AccentedRef { get; set; }
    public long Restored { get; set; }
    public long Missed { get; set; }
    public long WrongForm { get; set; }

    // Bare-reference world.
    public long BareRef { get; set; }
    public long Untouched { get; set; }
    public long FalseCorrections { get; set; }

    // Top offenders by class — diagnosis fuel, not a metric. Populated to 25.
    public IReadOnlyList<(string Word, long Count)> TopMissed { get; set; } =
        Array.Empty<(string, long)>();
    public IReadOnlyList<(string Word, long Count)> TopWrongForm { get; set; } =
        Array.Empty<(string, long)>();
    public IReadOnlyList<(string Word, long Count)> TopFalseCorrections { get; set; } =
        Array.Empty<(string, long)>();

    // Fraction of accented tokens whose accents were put back correctly.
    public double RestorationRecall =>
        AccentedRef == 0 ? 0.0 : (double)Restored / AccentedRef;

    // Fraction of correctly-typed bare words the policy wrongly altered — the
    // killer rate, the one to drive toward zero.
    public double FalseCorrectionRate =>
        BareRef == 0 ? 0.0 : (double)FalseCorrections / BareRef;

    // Fraction of all tokens whose final output matched the reference.
    public double WordAccuracy =>
        TotalTokens == 0 ? 0.0 : (double)(Restored + Untouched) / TotalTokens;

    // A sober fixed-width table plus the three top lists. No color, no flourish.
    public string FormatConsole()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Restoration evaluation");
        sb.AppendLine("──────────────────────────────────────────────");
        sb.AppendLine($"  total tokens          {TotalTokens,12:N0}");
        sb.AppendLine();
        sb.AppendLine($"  accented reference    {AccentedRef,12:N0}");
        sb.AppendLine($"    restored            {Restored,12:N0}");
        sb.AppendLine($"    missed (left bare)  {Missed,12:N0}");
        sb.AppendLine($"    wrong form          {WrongForm,12:N0}");
        sb.AppendLine();
        sb.AppendLine($"  bare reference        {BareRef,12:N0}");
        sb.AppendLine($"    untouched           {Untouched,12:N0}");
        sb.AppendLine($"    false corrections   {FalseCorrections,12:N0}");
        sb.AppendLine("──────────────────────────────────────────────");
        sb.AppendLine($"  restoration recall    {RestorationRecall,12:P2}");
        sb.AppendLine($"  false-correction rate {FalseCorrectionRate,12:P2}");
        sb.AppendLine($"  word accuracy         {WordAccuracy,12:P2}");

        AppendTop(sb, "Top missed", TopMissed);
        AppendTop(sb, "Top wrong form", TopWrongForm);
        AppendTop(sb, "Top false corrections", TopFalseCorrections);
        return sb.ToString();
    }

    private static void AppendTop(StringBuilder sb, string title, IReadOnlyList<(string Word, long Count)> list)
    {
        if (list.Count == 0)
            return;
        sb.AppendLine();
        sb.AppendLine($"{title}:");
        foreach (var (word, count) in list)
            sb.AppendLine($"  {count,6:N0}  {word}");
    }
}
