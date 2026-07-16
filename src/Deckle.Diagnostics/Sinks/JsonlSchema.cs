namespace Deckle.Diagnostics;

// Envelope shape a JSONL sink writes per line. Orthogonal to the
// gate (which decides *whether* a line is written) — this decides *what
// shape* the written line takes.
public enum JsonlSchema
{
    // { timestamp, kind, session, payload }. The frozen legacy contract,
    // used by the dataset channels (latency, microphone, corpus). Their
    // schema is a stable machine contract consumed by benchmark tooling
    // and pinned by the frozen dataset contract — it never gains envelope fields.
    PayloadOnly,

    // { timestamp, kind, session, provider, event, level, source, message, line, payload }.
    // The self-describing record used by the general app.jsonl journal so
    // the persisted line carries the same identity the live LogWindow
    // renders. A parameter-less event is no longer an empty blob —
    // provider, event and level still identify it, and `message` holds the
    // rendered text (null when the provider declared no Message template).
    // The window↔telemetry symmetry is part of the application JSONL contract.
    SelfDescribing,
}
