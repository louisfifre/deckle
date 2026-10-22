namespace Deckle.Diagnostics;

// Envelope shape a JSONL sink writes per line. Orthogonal to the
// gate (which decides *whether* a line is written) — this decides *what
// shape* the written line takes.
public enum JsonlSchema
{
    // { timestamp, kind, session, payload }. Used by the dataset channels
    // (latency, microphone, corpus): a stable machine contract consumed by
    // benchmark tooling, so it never gains envelope fields.
    PayloadOnly,

    // { timestamp, kind, session, provider, event, level, source, message, line, payload }.
    // Used by the operational log files so the persisted line carries the
    // same identity the live LogWindow renders. `message` is null when the
    // provider declared no Message template.
    SelfDescribing,
}
