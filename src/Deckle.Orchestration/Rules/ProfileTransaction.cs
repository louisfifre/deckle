namespace Deckle.Orchestration;

// Applies an entry's rules all together or not at all. Each effect changes the
// system and hands back its undo; if one effect fails, the ones already
// applied are undone at once and the session runs without rules. Never
// throws: a broken rule must not stop detection.
public static class ProfileTransaction
{
    public static AppliedProfile Apply(AppEntry entry, IReadOnlyList<IRuleEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(effects);

        var applied = new List<string>();
        var restores = new List<RuleRestore>();

        foreach (IRuleEffect effect in effects)
        {
            try
            {
                if (!effect.AppliesTo(entry)) continue;
                applied.Add(effect.Name);
                if (effect.Apply(entry) is { } restore)
                    restores.Add(new RuleRestore(effect.Name, restore));
            }
            catch (Exception ex)
            {
                AppliedProfile.RunInReverse(entry.Id, restores);
                DeckleOrchestrationSource.Log.RuleApplyFailed();
                DeckleOrchestrationSource.Log.RuleApplyFailedDetail(
                    entry.Id, effect.Name, ex.GetType().Name, ex.Message);
                return AppliedProfile.Empty(entry.Id);
            }
        }

        // An entry with no rule at all is the common case: nothing to say.
        if (applied.Count == 0) return AppliedProfile.Empty(entry.Id);

        DeckleOrchestrationSource.Log.ProfileApplied();
        DeckleOrchestrationSource.Log.ProfileAppliedDetail(entry.Id, string.Join(",", applied), restores.Count);
        return new AppliedProfile(entry.Id, applied, restores);
    }
}
