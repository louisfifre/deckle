namespace Deckle.Orchestration;

// What one session changed: the effects applied for an entry and the actions
// that undo them, in the order they were applied. Restore runs them in
// reverse, so the last change is undone first, the way a stack unwinds.
public sealed class AppliedProfile
{
    private readonly IReadOnlyList<RuleRestore> _restores;
    private int _restored;

    internal AppliedProfile(string entryId, IReadOnlyList<string> effects, IReadOnlyList<RuleRestore> restores)
    {
        EntryId = entryId;
        Effects = effects;
        _restores = restores;
    }

    /// <summary>A profile that changed nothing: the entry had no rule, or applying them failed and was rolled back.</summary>
    public static AppliedProfile Empty(string entryId) => new(entryId, [], []);

    public string EntryId { get; }

    /// <summary>Names of the effects that applied, in order.</summary>
    public IReadOnlyList<string> Effects { get; }

    /// <summary>How many changes Restore will undo.</summary>
    public int Changes => _restores.Count;

    /// <summary>
    /// Undo every change, last first. A restore that throws is logged and the
    /// others still run. Only the first call does anything: a session end and
    /// a shutdown may both ask.
    /// </summary>
    public void Restore()
    {
        if (Interlocked.Exchange(ref _restored, 1) == 1) return;
        if (_restores.Count == 0) return;

        int restored = RunInReverse(EntryId, _restores);
        DeckleOrchestrationSource.Log.ProfileRestored();
        DeckleOrchestrationSource.Log.ProfileRestoredDetail(EntryId, restored);
    }

    // Shared with ProfileTransaction's rollback. Returns how many succeeded.
    internal static int RunInReverse(string entryId, IReadOnlyList<RuleRestore> restores)
    {
        int succeeded = 0;
        for (int i = restores.Count - 1; i >= 0; i--)
        {
            RuleRestore restore = restores[i];
            try
            {
                restore.Action();
                succeeded++;
            }
            catch (Exception ex)
            {
                DeckleOrchestrationSource.Log.RuleRestoreFailed();
                DeckleOrchestrationSource.Log.RuleRestoreFailedDetail(
                    entryId, restore.Effect, ex.GetType().Name, ex.Message);
            }
        }
        return succeeded;
    }
}

// One undo action and the effect it belongs to, for the logs.
internal sealed record RuleRestore(string Effect, Action Action);
