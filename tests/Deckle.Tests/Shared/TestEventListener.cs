using System.Diagnostics.Tracing;

namespace Deckle.Tests.Shared;

// ── TestEventListener ─────────────────────────────────────────────────────────
//
// EventListener instrumenté pour les tests d'observabilité. Collecte les
// EventWrittenEventArgs émis par le provider ciblé, sans filtre côté
// listener — le test asserte ensuite sur la séquence collectée.
//
// Limitation native EventListener à connaître : OnEventSourceCreated est
// appelé pour les sources préexistantes pendant le constructeur de la
// classe de base, AVANT que les champs de la classe dérivée soient
// assignés. Le re-scan explicite via EventSource.GetSources() après
// assignment du nom couvre ce cas. OnEventSourceCreated reste utile pour
// les sources créées APRÈS instanciation du listener.
//
// Utilisation typique dans un test :
//   using var listener = new TestEventListener("Deckle.Chrono");
//   DeckleChronoSource.Log.PilotEmitted("test");
//   Assert.Single(listener.Events);
//
// Le `using` est important — Dispose désinscrit le listener, sinon il
// continue de capter les émissions des tests suivants.
internal sealed class TestEventListener : EventListener
{
    private readonly string _providerName;
    private readonly List<EventWrittenEventArgs> _events = new();
    private readonly object _gate = new();

    public TestEventListener(string providerName)
    {
        _providerName = providerName;

        // Rattrapage des sources créées AVANT l'instanciation du listener.
        // OnEventSourceCreated a couru sur ces sources pendant base() avec
        // _providerName encore null — on les inscrit explicitement ici.
        foreach (var source in EventSource.GetSources())
        {
            if (source.Name == providerName)
            {
                EnableEvents(source, EventLevel.LogAlways, EventKeywords.All);
            }
        }
    }

    public IReadOnlyList<EventWrittenEventArgs> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // Sources créées APRÈS l'instanciation du listener — _providerName
        // est désormais assigné. Le null-check protège du cas préexistant
        // (les sources préexistantes sont traitées dans le constructeur).
        if (_providerName is not null && eventSource.Name == _providerName)
        {
            EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        // Garde de sécurité : EventListener BCL peut router des events
        // système non-Deckle (RuntimeEventSource, etc.) selon les
        // EnableEvents passifs. On filtre par nom au cas où.
        if (eventData.EventSource.Name != _providerName) return;

        lock (_gate)
        {
            _events.Add(eventData);
        }
    }
}
