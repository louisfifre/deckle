namespace Deckle.Shell.Taskbar;

// Re-emits a record from the DLL through the module's provider, at the level
// the DLL gave it. The DLL's own event name travels as the `name` payload; the
// provider's event ids stay Deckle's.
internal static class NativeLogRelay
{
    public static void Relay(NativeLogRecord record)
    {
        DeckleShellTaskbarSource log = DeckleShellTaskbarSource.Log;
        string fields = record.FieldsText();

        switch (record.Level)
        {
            case NativeLogLevel.Verbose:
                log.NativeEngineDetail(record.Event, fields);
                break;
            case NativeLogLevel.Warning:
                log.NativeEngineWarning(record.Event, record.Message, fields);
                break;
            case NativeLogLevel.Error:
                log.NativeEngineError(record.Event, record.Message, fields);
                break;
            default:
                log.NativeEngineNote(record.Event, fields);
                break;
        }
    }
}
