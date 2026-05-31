namespace Deckle.Diagnostics.Listeners;

// Envelope shape a JsonlEventListener writes per line. Orthogonal to the
// gate (which decides *whether* a line is written) — this decides *what
// shape* the written line takes.
public enum JsonlSchema
{
    // { timestamp, kind, session, payload }. The frozen legacy contract,
    // used by the dataset channels (latency, microphone, corpus). Their
    // schema is a stable machine contract consumed by benchmark tooling
    // and pinned by ADR-0011 — it never gains envelope fields.
    PayloadOnly,

    // { timestamp, kind, session, provider, event, level, message, payload }.
    // The self-describing record used by the general app.jsonl journal so
    // the persisted line carries the same identity the live LogWindow
    // renders. A parameter-less event is no longer an empty blob —
    // provider, event and level still identify it, and `message` holds the
    // rendered text (null when the provider declared no Message template).
    // The window↔telemetry symmetry this enables is recorded in ADR-0017.
    SelfDescribing,
}
