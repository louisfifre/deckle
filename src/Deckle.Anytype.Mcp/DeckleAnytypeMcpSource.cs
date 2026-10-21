using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Anytype.Mcp;

// MCP host provider. Covers the resident streamable-HTTP host that serves the
// Anytype toolsets to external clients: the listener's lifecycle, the requests
// it refuses and the tool calls it serves. Milestones carry their cause and
// read as a narrative of the host's life; the Verbose events carry the
// greppable detail.
//
// No event ever carries a bearer token, a tool argument or a tool result: the
// vault secret and the Home space content stay out of the trace.
[EventSource(Name = "Deckle-AnytypeMcp")]
public sealed class DeckleAnytypeMcpSource : DeckleEventSource
{
    public static readonly DeckleAnytypeMcpSource Log = new();

    private DeckleAnytypeMcpSource() { }

    // Module-local keyword bits (transverse bits 0..9 reserved in Keywords;
    // 0x400+ belongs to the provider). Lifecycle covers the host's start, stop
    // and request admission; Tool covers the per-call lines, so a reader can
    // keep the host's story without the traffic.
    private const EventKeywords Lifecycle = (EventKeywords)0x400;
    private const EventKeywords Tool      = (EventKeywords)0x800;

    // Journal milestones also carry the transverse Lifecycle bit, so a
    // cross-module lifecycle filter finds the host's start, stop and failure.
    private const EventKeywords JournalLifecycle = Lifecycle | (EventKeywords)Keywords.Lifecycle;

    // ── EventIds ─────────────────────────────────────────────────────────
    public const int EvtHostStarted          = 1;
    public const int EvtHostStartedDetail     = 2;
    public const int EvtHostStopped           = 3;
    public const int EvtHostNotProvisioned    = 4;
    // Event ids 5 and 6 are retired and stay reserved so an old trace can never
    // be misread as a new event.
    // 7 retired.
    public const int EvtRequestRejectedDetail = 8;
    public const int EvtRequestRejected       = 9;
    public const int EvtRequestRejectionsEnded = 10;
    public const int EvtHostStartFailed       = 11;
    public const int EvtHostStopFailed        = 12;
    public const int EvtHostProvisioningFailed = 13;
    public const int EvtToolCallCompleted     = 14;
    public const int EvtTokensMinted          = 15;
    public const int EvtTokenVariablesPublished = 16;
    public const int EvtHostInternalLog       = 17;
    public const int EvtHostInternalLogRepeated = 18;

    // ── Host lifecycle ────────────────────────────────────────────────────

    [Event(EvtHostStarted,
           Level = EventLevel.Informational,
           Keywords = JournalLifecycle,
           Tags = ObservationTags.Journal,
           Message = "The MCP host is listening")]
    public void HostStarted()
    {
        if (IsEnabled()) WriteEvent(EvtHostStarted);
    }

    [Event(EvtHostStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "mcp host listening | url={0}")]
    public void HostStartedDetail(string url)
    {
        if (IsEnabled()) WriteEvent(EvtHostStartedDetail, url);
    }

    // Emitted only after a clean drain and dispose; a stop that did not drain
    // or threw says so through HostStopFailed instead.
    [Event(EvtHostStopped,
           Level = EventLevel.Informational,
           Keywords = JournalLifecycle,
           Tags = ObservationTags.Journal,
           Message = "The MCP host stopped")]
    public void HostStopped()
    {
        if (IsEnabled()) WriteEvent(EvtHostStopped);
    }

    // Usually the port is taken (another Deckle, another tool): no client can
    // connect until the next start.
    [Event(EvtHostStartFailed,
           Level = EventLevel.Error,
           Keywords = JournalLifecycle,
           Tags = ObservationTags.Journal,
           Message = "The MCP host could not listen | port={0} | {1}: {2}")]
    public void HostStartFailed(int port, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtHostStartFailed, port, ex_type, ex_message);
    }

    // reason: drain_timeout (a tool call outlived the shutdown budget; the API
    // client is kept alive under it), stop_failed or dispose_failed.
    [Event(EvtHostStopFailed,
           Level = EventLevel.Warning,
           Keywords = JournalLifecycle,
           Tags = ObservationTags.Journal,
           Message = "The MCP host did not stop cleanly | reason={0} | {1}: {2}")]
    public void HostStopFailed(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtHostStopFailed, reason, ex_type, ex_message);
    }

    // Kept until the call sites outside this module move to HostProvisioningFailed.
    [Event(EvtHostNotProvisioned,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Message = "The MCP host is not provisioned")]
    public void HostNotProvisioned()
    {
        if (IsEnabled()) WriteEvent(EvtHostNotProvisioned);
    }

    // The host cannot listen because the Anytype credentials or the token vault
    // are unavailable — a state the user must act on before any client can
    // connect. reason: credentials_missing, vault_error, bad_endpoint or
    // token_mint_failed.
    [Event(EvtHostProvisioningFailed,
           Level = EventLevel.Warning,
           Keywords = JournalLifecycle,
           Tags = ObservationTags.Journal,
           Message = "The MCP host is not provisioned | reason={0} | {1}: {2}")]
    public void HostProvisioningFailed(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtHostProvisioningFailed, reason, ex_type, ex_message);
    }

    // Kestrel and the MCP SDK log through Microsoft.Extensions.Logging, which
    // the host clears; Warning and above come back here. The message is left
    // out on purpose: framework text can quote request content.
    [Event(EvtHostInternalLog,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Message = "The MCP host reported a problem | event_name={1} | category={0} | level={2} | ex_type={3}")]
    public void HostInternalLog(string category, string event_name, string log_level, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtHostInternalLog, category, event_name, log_level, ex_type);
    }

    // The same framework event again: one incident per event name, not one
    // Warning per request.
    [Event(EvtHostInternalLogRepeated,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "mcp host problem repeated | category={0} | event_name={1} | occurrences={2} | ex_type={3}")]
    public void HostInternalLogRepeated(string category, string event_name, int occurrences, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtHostInternalLogRepeated, category, event_name, occurrences, ex_type);
    }

    // ── Client tokens ─────────────────────────────────────────────────────

    // Counts only: a token never leaves the vault through the trace.
    [Event(EvtTokensMinted,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "mcp client tokens checked | clients={0} | minted={1}")]
    public void TokensMinted(int clients, int minted)
    {
        if (IsEnabled()) WriteEvent(EvtTokensMinted, clients, minted);
    }

    // missing counts clients whose vault token is absent: their environment
    // variable stays stale and that client will be refused with unknown-bearer.
    [Event(EvtTokenVariablesPublished,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "mcp client token variables published | clients={0} | published={1} | unchanged={2} | missing={3}")]
    public void TokenVariablesPublished(int clients, int published, int unchanged, int missing)
    {
        if (IsEnabled()) WriteEvent(EvtTokenVariablesPublished, clients, published, unchanged, missing);
    }

    // ── Requests ──────────────────────────────────────────────────────────

    // A request the host refused before dispatch (bad auth or Origin, oversized
    // body, wrong method, rate limit). Opens an incident: later refusals stay
    // Verbose until an admitted request closes it, so a misconfigured client or
    // a hostile page cannot flood the journal. reason: rate-limit,
    // body-too-large, foreign-origin, missing-bearer, unknown-bearer,
    // method-not-allowed.
    [Event(EvtRequestRejected,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Message = "An MCP request was rejected | reason={0} | status={1}")]
    public void RequestRejected(string reason, int status_code)
    {
        if (IsEnabled()) WriteEvent(EvtRequestRejected, reason, status_code);
    }

    [Event(EvtRequestRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "mcp request rejected | reason={0} | status={1}")]
    public void RequestRejectedDetail(string reason, int status_code)
    {
        if (IsEnabled()) WriteEvent(EvtRequestRejectedDetail, reason, status_code);
    }

    [Event(EvtRequestRejectionsEnded,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
           Message = "MCP requests are admitted again | rejected={0}")]
    public void RequestRejectionsEnded(int rejected)
    {
        if (IsEnabled()) WriteEvent(EvtRequestRejectionsEnded, rejected);
    }

    // One line per tool call: which tool, how it ended and how long it took.
    // Never the arguments, never the result, never an exception message (it can
    // quote Anytype content); ex_type names the failure. outcome: completed,
    // input_rejected, output_rejected, failed, cancelled, unknown_tool,
    // missing_tool_name. The rate limiter bounds the volume.
    [Event(EvtToolCallCompleted,
           Level = EventLevel.Informational,
           Keywords = Tool,
           Message = "MCP tool call finished | outcome={2} | surface={0} | tool={1} | ex_type={3} | elapsed_ms={4:F1}")]
    public void ToolCallCompleted(string surface, string tool, string outcome, string ex_type, double elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtToolCallCompleted, surface, tool, outcome, ex_type, elapsed_ms);
    }
}
