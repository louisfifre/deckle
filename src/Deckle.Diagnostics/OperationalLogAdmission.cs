namespace Deckle.Diagnostics;

// Closed activity vocabulary for producer-side operational-log admission.
// Provider boundaries do not appear here: one user workflow can span several
// emitting modules.
public enum OperationalLogActivity
{
    Ambient,
    Transcription,
    Autocorrect,
    Windowing,
}

// Dependency-neutral access point used by producers before log-only work.
// Logging owns the persisted policy; the composition root injects its reader
// after settings migration. Governed activities fail closed until then.
public static class OperationalLogAdmission
{
    private static Func<OperationalLogActivity, bool>? _reader;
    private static readonly bool[] _active =
        new bool[Enum.GetValues<OperationalLogActivity>().Length];

    public static void Configure(Func<OperationalLogActivity, bool> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Volatile.Write(ref _reader, reader);
    }

    public static bool IsEnabled(OperationalLogActivity activity)
    {
        var reader = Volatile.Read(ref _reader);
        if (reader is null) return false;
        try { return reader(activity); }
        catch { return false; }
    }

    // Supporting providers use this form when their detail belongs to an
    // activity only while its workflow is running. Outside that scope their
    // own diagnostics remain unaffected.
    public static bool AllowsScopedDetail(OperationalLogActivity activity)
        => !IsActive(activity) || IsEnabled(activity);

    public static bool IsActive(OperationalLogActivity activity)
        => Volatile.Read(ref _active[(int)activity]);

    public static void SetActive(OperationalLogActivity activity, bool active)
        => Volatile.Write(ref _active[(int)activity], active);
}
