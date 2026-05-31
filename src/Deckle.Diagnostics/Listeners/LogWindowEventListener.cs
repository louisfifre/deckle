using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics.Listeners;

// Listens to every Deckle.* EventSource and forwards each event to the
// registered ILogWindowSink(s). The listener is constructed once at App
// boot and starts buffering immediately; sinks are attached lazily as
// the LogWindow surface comes online (typically on first user open).
//
// Lifetime. Constructed once at App boot, kept alive for the life of
// the process. EventListener auto-discovers EventSources created
// after the listener (the OnEventSourceCreated callback fires every
// time a new provider is instantiated), so providers declared in
// modules loaded lazily still light up.
//
// Threading. EventListener.OnEventWritten fires on the emitting
// thread. The ILogWindowSink implementation is responsible for
// marshalling to the UI thread if it needs to (e.g. via DispatcherQueue).
//
// Buffer pour lazy LogWindow. Le LogWindow est créé à la première
// ouverture utilisateur (lazy) ; les events émis pendant le boot
// doivent être visibles dès cette ouverture. Le listener tient un
// ring de capacité fixe (5000) et le rejoue intégralement au moment
// où un sink s'attache via `AttachSink`. Remplace l'ancien `Telemetry-
// Service._history` du legacy avec la même garantie d'historique.
public sealed class LogWindowEventListener : EventListener
{
    private const int BufferCapacity = 5000;

    private readonly List<ILogWindowSink> _sinks = new();
    private readonly List<EventEntry> _buffer = new(capacity: BufferCapacity);
    private readonly object _lock = new();

    // Optional drop filter. Quand non-null et retourne true, l'entry
    // est ignorée AVANT insertion dans le buffer ring et AVANT broadcast
    // aux sinks. Conséquence directe : un entry filtré ne sera pas non
    // plus rejoué par AttachSink, puisqu'il n'a jamais atterri dans le
    // buffer. Posture délibérée — le filter exprime un signal "cet
    // event n'a pas vocation à exister dans la fenêtre de log live",
    // pas un masquage temporaire de l'affichage.
    //
    // Câblé par le host via ConfigureDropFilter. Cas d'usage actuel :
    // silencer les Verbose ambient pendant la capture loop quand le
    // toggle LogAmbientCaptureActivity est off (consommé via
    // Deckle.Diagnostics.Logging.AmbientCaptureGate).
    private Func<EventEntry, bool>? _dropFilter;
    private Func<string, EventLevel, bool>? _providerLevelDropFilter;

    // We collect EventSources observed before the derived constructor
    // is ready, then enable them in the constructor body. EventListener's
    // base constructor invokes OnEventSourceCreated for every already-
    // existing provider; that callback can fire before the derived
    // constructor's field initialisers run, so the listener may not be
    // ready yet on the first calls.
    private readonly List<EventSource> _earlySources = new();
    private bool _ready;

    public LogWindowEventListener()
    {
        // Now that fields are wired, light up everything we saw during
        // base-class init.
        lock (_earlySources)
        {
            _ready = true;
            foreach (var src in _earlySources)
                EnableEvents(src, EventLevel.LogAlways, EventKeywords.All);
            _earlySources.Clear();
        }
    }

    // Attache un sink et lui rejoue l'historique bufferisé depuis le
    // boot. Le rejeu se fait sous le lock du buffer pour qu'aucun event
    // ne s'intercale entre la copie du snapshot et l'inscription du
    // sink — un event arrivé pendant le rejeu sera capturé dans le live
    // path, jamais perdu ni dupliqué.
    public void AttachSink(ILogWindowSink sink)
    {
        EventEntry[] replay;
        lock (_lock)
        {
            replay = _buffer.ToArray();
            _sinks.Add(sink);
        }
        foreach (var entry in replay)
        {
            try { sink.Write(entry); }
            catch { /* A sink must never crash the listener. */ }
        }
    }

    public void DetachSink(ILogWindowSink sink)
    {
        lock (_lock) _sinks.Remove(sink);
    }

    // Installe un filter de drop unique. Un seul filter actif à la
    // fois — un nouvel appel remplace le précédent. Null désinstalle.
    // Le filter est consulté dans OnEventWritten avant insertion dans
    // le buffer et avant broadcast aux sinks ; un entry filtré n'est
    // donc jamais vu par les sinks (ni en live, ni au replay
    // d'AttachSink).
    public void ConfigureDropFilter(Func<EventEntry, bool> filter)
    {
        _dropFilter = filter;
    }

    // Filtre de drop précoce, consulté avant BuildEntry. À utiliser
    // pour les familles bruyantes dont provider + level suffisent à
    // décider, afin d'éviter les allocations payload / format string.
    public void ConfigureProviderLevelDropFilter(Func<string, EventLevel, bool> filter)
    {
        _providerLevelDropFilter = filter;
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name is null) return;
        if (!eventSource.Name.StartsWith("Deckle.", System.StringComparison.Ordinal)) return;

        lock (_earlySources)
        {
            if (!_ready)
            {
                _earlySources.Add(eventSource);
                return;
            }
        }
        EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        string? provider = eventData.EventSource.Name;
        if (provider is null) return;

        var providerLevelDropFilter = _providerLevelDropFilter;
        if (providerLevelDropFilter is not null)
        {
            try { if (providerLevelDropFilter(provider, eventData.Level)) return; }
            catch { /* A filter must never crash the listener. */ }
        }

        var entry = BuildEntry(eventData);

        // Drop filter consulté avant le buffer pour qu'un entry filtré
        // ne soit ni rejoué ni broadcasté. Lecture non locked du field :
        // une race au moment d'un ConfigureDropFilter passe au pire un
        // event de trop ou un event de moins, jamais une corruption.
        var dropFilter = _dropFilter;
        if (dropFilter is not null)
        {
            try { if (dropFilter(entry)) return; }
            catch { /* A filter must never crash the listener. */ }
        }

        ILogWindowSink[] snapshot;
        lock (_lock)
        {
            // Ring : on bornè le buffer pour ne pas croître indéfini-
            // ment sur les longues sessions. Quand on dépasse la cap,
            // on jette le plus ancien — même posture que `LogWindow`
            // côté UI (cap 5000 dans `_entries`). La capacité matche
            // pour que le replay d'ouverture remplisse exactement la
            // fenêtre que l'utilisateur va voir.
            _buffer.Add(entry);
            if (_buffer.Count > BufferCapacity) _buffer.RemoveAt(0);
            snapshot = _sinks.ToArray();
        }

        foreach (var sink in snapshot)
        {
            try { sink.Write(entry); }
            catch { /* A sink must never crash the emitter. */ }
        }
    }

    internal static EventEntry BuildEntry(EventWrittenEventArgs e)
    {
        var dict = new Dictionary<string, object?>(System.StringComparer.Ordinal);
        var names = e.PayloadNames;
        var values = e.Payload;
        int count = names is null ? 0 : names.Count;
        for (int i = 0; i < count; i++)
        {
            string key = names![i];
            object? value = (values is not null && i < values.Count) ? values[i] : null;
            dict[key] = value;
        }

        // EventWrittenEventArgs.Message is the template declared via
        // the [Event(Message = "…")] attribute. String.Format with
        // the payload yields the human-readable line. Null when the
        // provider didn't supply a template; sinks fall back to a
        // generic "Provider.EventName" rendering.
        string? formatted = null;
        if (!string.IsNullOrEmpty(e.Message) && values is not null)
        {
            try
            {
                var arr = new object?[values.Count];
                for (int i = 0; i < values.Count; i++) arr[i] = values[i];
                formatted = string.Format(System.Globalization.CultureInfo.InvariantCulture, e.Message, arr);
            }
            catch
            {
                // A malformed template should not break the pipeline.
                // Leave formatted = null and let the sink render the
                // raw payload instead.
            }
        }

        return new EventEntry(
            timestamp: System.DateTimeOffset.Now,
            provider: e.EventSource.Name!,
            eventName: e.EventName ?? "(unnamed)",
            level: e.Level,
            keywords: e.Keywords,
            formattedMessage: formatted,
            payload: dict);
    }
}
