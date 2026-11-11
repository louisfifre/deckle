namespace Deckle.Llm.Rewrite;

// What the Rewriting page's "Reset all" writes: the shipped defaults, with the
// module switch and "Rewrite dictations" carried over from the settings in
// force. Whether rewriting is on is the person's choice, not a value to tune,
// so a reset never flips it (2026-11-07). Apart from the page so a test can
// pin it.
public static class LlmResetDefaults
{
    public static LlmSettings From(LlmSettings current)
    {
        // Fresh defaults carry no profile ids nor steps: reconcile them
        // before the instance goes live.
        var fresh = new LlmSettings { Enabled = current.Enabled };
        LlmSettingsNormalizer.Apply(fresh);
        fresh.Dictation.Enabled = current.Dictation?.Enabled ?? false;
        return fresh;
    }
}
