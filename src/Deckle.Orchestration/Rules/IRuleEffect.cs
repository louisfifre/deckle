namespace Deckle.Orchestration;

// One kind of change Deckle makes while a listed app runs (HDR per monitor
// today; autocorrect or Ambient later). ProfileTransaction applies every
// effect at session start and keeps the restore actions they return.
//
// An effect owns its own state only: it never writes another module's
// settings, it changes the live system and gives back the way to undo it.
public interface IRuleEffect
{
    /// <summary>Stable lowercase name, used in logs ("hdr").</summary>
    string Name { get; }

    /// <summary>True when the entry's rules ask this effect for anything.</summary>
    bool AppliesTo(AppEntry entry);

    /// <summary>
    /// Capture the prior state, change it, and return the action that puts it
    /// back; null when nothing had to change. Throws on failure, after undoing
    /// whatever part of its own change already happened.
    /// </summary>
    Action? Apply(AppEntry entry);
}
