using System.Text;
using Deckle.Input.Autocorrect;

namespace Deckle.Input.Autocorrect;

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
// The headline is PRECISION, not accuracy. The product objective is asymmetric:
// a correction that lands on an already-correct word is the cardinal sin, while
// a miss is tolerable (the user fixes it by hand — the status quo without the
// tool). So the questions are "when the engine acts, is it right?" (precision,
// the gate, target ~100%) and "of the accents that were needed, how many did it
// restore?" (recall, the coverage objective, NOT a ~100% target). Word accuracy
// blends the two and hides the cardinal sin under recall gains — kept only as a
// demoted diagnostic, never the verdict.
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

    // Per-stage breakdown of every emitted correction, keyed by the reason the
    // policy gave. This is how the eval sees ITSELF: without it, Restored and
    // WrongForm conflate the deterministic gate, frequency dominance and the
    // context model, so we cannot tell whether a number reflects the cheap path
    // or the pair model — nor whether the context model is a net gain. Populated
    // by the evaluator; one entry per reason that actually fired.
    public IReadOnlyDictionary<CorrectionReason, StageTally> ByStage { get; set; } =
        new Dictionary<CorrectionReason, StageTally>();

    // Top offenders by class — diagnosis fuel, not a metric. Populated to 25.
    public IReadOnlyList<(string Word, long Count)> TopMissed { get; set; } =
        Array.Empty<(string, long)>();
    public IReadOnlyList<(string Word, long Count)> TopWrongForm { get; set; } =
        Array.Empty<(string, long)>();
    public IReadOnlyList<(string Word, long Count)> TopFalseCorrections { get; set; } =
        Array.Empty<(string, long)>();

    // Every token the policy acted on (output != typed): the wins plus both
    // wrong classes. The denominator of precision.
    public long EmittedCorrections => Restored + WrongForm + FalseCorrections;

    // Corruptions: emitted corrections that were wrong, whichever world they
    // landed in. The numerator of "harm done by acting".
    public long Corruptions => WrongForm + FalseCorrections;

    // THE headline. Of every correction the engine chose to emit, the share that
    // was right. ~100% is the meaningful target — it means "the engine is never
    // wrong when it acts". NaN when the engine emitted nothing (not 0%: there is
    // nothing to be right or wrong about).
    public double Precision =>
        EmittedCorrections == 0 ? double.NaN : (double)Restored / EmittedCorrections;

    // Of the accents that were actually needed, the share put back correctly.
    // The coverage objective — maximized UNDER the precision/FC ceiling, never
    // chased to 100% on its own.
    public double RestorationRecall =>
        AccentedRef == 0 ? double.NaN : (double)Restored / AccentedRef;

    // Of the correctly-typed bare words, the share the policy wrongly altered —
    // the cardinal-sin rate, the one to drive toward zero. NaN when there were
    // no bare words to wreck (an empty denominator is "not measured", not 0%).
    public double FalseCorrectionRate =>
        BareRef == 0 ? double.NaN : (double)FalseCorrections / BareRef;

    // Demoted diagnostic only. Blends recall and the cardinal sin in one sum, so
    // a recall gain can mask a false-correction explosion — never the verdict.
    public double WordAccuracy =>
        TotalTokens == 0 ? double.NaN : (double)(Restored + Untouched) / TotalTokens;

    // A sober fixed-width table. Precision leads; recall and the false-correction
    // rate follow as their own blocks; the per-stage breakdown exposes which path
    // earned the numbers; word accuracy trails as a flagged diagnostic.
    public string FormatConsole()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Restoration evaluation");
        sb.AppendLine("──────────────────────────────────────────────");
        sb.AppendLine($"  total tokens          {TotalTokens,12:N0}");
        sb.AppendLine();
        sb.AppendLine($"  PRECISION             {Pct(Precision),12}   when it acts, share correct — target ~100%");
        sb.AppendLine($"    emitted corrections {EmittedCorrections,12:N0}");
        sb.AppendLine($"      restored          {Restored,12:N0}");
        sb.AppendLine($"      wrong form        {WrongForm,12:N0}");
        sb.AppendLine($"      false correction  {FalseCorrections,12:N0}");
        sb.AppendLine();
        sb.AppendLine($"  RECALL                {Pct(RestorationRecall),12}   of accents needed, share restored");
        sb.AppendLine($"    accented reference  {AccentedRef,12:N0}");
        sb.AppendLine($"      restored          {Restored,12:N0}");
        sb.AppendLine($"      missed (bare)      {Missed,12:N0}");
        sb.AppendLine($"      wrong form        {WrongForm,12:N0}");
        sb.AppendLine();
        sb.AppendLine($"  FALSE-CORR. RATE      {Pct(FalseCorrectionRate),12}   correct words wrecked — drive to 0");
        sb.AppendLine($"    bare reference      {BareRef,12:N0}");
        sb.AppendLine($"      untouched         {Untouched,12:N0}");
        sb.AppendLine($"      false corrections {FalseCorrections,12:N0}");
        sb.AppendLine("──────────────────────────────────────────────");

        AppendStages(sb);

        sb.AppendLine();
        sb.AppendLine($"  (diagnostic) word accuracy {Pct(WordAccuracy),12}   blends recall + harm — not a verdict");

        AppendTop(sb, "Top missed", TopMissed);
        AppendTop(sb, "Top wrong form", TopWrongForm);
        AppendTop(sb, "Top false corrections", TopFalseCorrections);
        return sb.ToString();
    }

    // The per-stage table: for each reason that fired, how many corrections it
    // emitted, how many were right, how many were corruptions. A stage whose
    // wrong count rivals its correct count is buying recall with the cardinal sin.
    private void AppendStages(StringBuilder sb)
    {
        if (ByStage.Count == 0)
            return;
        sb.AppendLine();
        sb.AppendLine("  by correction stage         acted     correct       wrong");
        foreach (var (reason, t) in ByStage)
            sb.AppendLine($"    {reason,-22}{t.Acted,8:N0}{t.Correct,12:N0}{t.Wrong,12:N0}");
    }

    private static string Pct(double v) =>
        double.IsNaN(v) ? "N/A" : v.ToString("P2");

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

// One stage's emitted-correction tally: total acted, of which correct (Restored)
// and wrong (WrongForm + FalseCorrection). Mutable — the evaluator accumulates
// into it during the pass, then hands the dictionary to the report.
public sealed class StageTally
{
    public long Acted { get; set; }
    public long Correct { get; set; }
    public long Wrong { get; set; }
}
