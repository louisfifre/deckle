using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Modules;
using Deckle.Shell.TrayMenu;
using Deckle.Transcription;

namespace Deckle.App;

// Where each user incident shows in the tray: the module line it folds into and
// the Settings page a click on that line opens. The modules raise incidents by
// id (UserIncidents) and know nothing of the tray; this table is the App's
// answer to "under which name, and where is the fix".
//
// One line per module, not per id: Transcription has two sources (the Whisper
// model and the speech-trim model) and Ambient Light two (the event stream and
// the push), and the person fixes either from the same place. The order of
// Sources is the order of the lines, and a module's line opens the page of its
// first open source.
internal static class TrayIncidentLines
{
    internal sealed record Source(string IncidentId, string Module, string PageTag);

    internal sealed record Line(string Module, string PageTag);

    // Modules without a ModuleIds entry: the microphone belongs to Recording,
    // the elevated toasts to Notifications.
    internal const string RecordingModule     = "recording";
    internal const string NotificationsModule = "notifications";

    private const string RecordingPageTag       = "Deckle.Audio.RecordingPage, Deckle.Audio";
    private const string PrecisionScrollPageTag = "Deckle.Input.PrecisionScroll.PrecisionScrollPage, Deckle.Input.PrecisionScroll";
    private const string AmbientPageTag         = "Deckle.Lighting.Ambient.AmbientPage, Deckle.Lighting.Ambient";
    // Anytype has no page of its own yet, and the elevated toasts' explanation
    // sits on General (its InfoBar).
    private const string GeneralPageTag         = "Deckle.Settings.GeneralPage";

    internal static IReadOnlyList<Source> Sources { get; } =
    [
        new(UserIncidents.PrecisionScroll, ModuleIds.PrecisionScroll,   PrecisionScrollPageTag),
        new(UserIncidents.Microphone,      RecordingModule,             RecordingPageTag),
        new(UserIncidents.WhisperModel,    ModuleIds.Transcription,     WhisperSettingsModule.PageTag),
        new(UserIncidents.VadModel,        ModuleIds.Transcription,     WhisperSettingsModule.StreamingPageTag),
        new(UserIncidents.AnytypeBackend,  ModuleIds.Anytype,           GeneralPageTag),
        new(UserIncidents.HueEvents,       ModuleIds.Ambient,           AmbientPageTag),
        new(UserIncidents.HuePush,         ModuleIds.Ambient,           AmbientPageTag),
        new(UserIncidents.ToastsElevated,  NotificationsModule,         GeneralPageTag),
    ];

    // The module's name in the menu line, the same as its Settings page where
    // it has one, so the line and the page it opens read alike. Anytype is a
    // product name and stays out of the resources, as Loc requires.
    internal static IReadOnlyDictionary<string, string> LabelKeys { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ModuleIds.PrecisionScroll] = "TrayMenu_PrecisionScroll",
            [RecordingModule]           = "TrayIncident_Module_Recording",
            [ModuleIds.Transcription]   = "TrayIncident_Module_Transcription",
            [ModuleIds.Ambient]         = "TrayMenu_AmbientLight",
            [NotificationsModule]       = "TrayIncident_Module_Notifications",
        };

    internal const string AnytypeLabel = "Anytype";

    internal static bool HasLabel(string module)
        => module == ModuleIds.Anytype || LabelKeys.ContainsKey(module);

    // Reads the resources: called while the menu builds its items, never in a test.
    internal static string LabelOf(string module)
        => module == ModuleIds.Anytype ? AnytypeLabel : Loc.Get(LabelKeys[module]);

    internal static void DeclareAll(UserIncidentRegistry registry)
    {
        foreach (Source source in Sources)
            registry.Declare(source.IncidentId, source.Module, source.PageTag);
    }

    // The registry hands its open incidents in declaration order, so the first
    // one met for a module is its highest-ranked open source. An incident no
    // one declared has no module to show under and no page to open: it has no
    // line, and a test keeps every id declared.
    internal static IReadOnlyList<Line> Fold(IReadOnlyList<UserIncident> open)
    {
        var lines = new List<Line>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (UserIncident incident in open)
        {
            if (incident.Module is null || incident.PageTag is null) continue;
            if (seen.Add(incident.Module))
                lines.Add(new Line(incident.Module, incident.PageTag));
        }
        return lines;
    }

    // The lines go first, above the switches, behind a separator of their own;
    // with no line the menu is exactly what it was.
    internal static IReadOnlyList<TrayMenuEntry> Compose(
        IReadOnlyList<TrayMenuEntry> lines, IReadOnlyList<TrayMenuEntry> rest)
        => lines.Count == 0 ? rest : [.. lines, new TrayMenuSeparator(), .. rest];
}
