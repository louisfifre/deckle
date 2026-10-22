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
    // 4 retired.
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
    public const int EvtToolCallStarted       = 19;
    public const int EvtToolCallFailed        = 20;
    public const int EvtHostStartRefused      = 21;
    public const int EvtHostInternalLogCounted = 22;
    public const int EvtTokenVariablesMissing = 23;

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

    // Start was asked of a host that cannot start: nothing was bound and no client
    // can connect. reason: disposed (the host was already disposed; a new host
    // has to be built).
    [Event(EvtHostStartRefused,
           Level = EventLevel.Warning,
           Keywords = JournalLifecycle,
           Tags = ObservationTags.Journal,
           Message = "The MCP host did not start | reason={0}")]
    public void HostStartRefused(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtHostStartRefused, reason);
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

    // The running count of one framework problem, in the journal: written when the
    // count reaches 10, 100, 1000... and once more when the host's logging closes
    // if the count moved since, so the journal shows how often a problem the
    // Warning announced once kept happening.
    [Event(EvtHostInternalLogCounted,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The MCP host problem kept happening | category={0} | event_name={1} | occurrences={2} | ex_type={3}")]
    public void HostInternalLogCounted(string category, string event_name, int occurrences, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtHostInternalLogCounted, category, event_name, occurrences, ex_type);
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

    // A client whose vault token is absent keeps a stale environment variable and
    // is refused with unknown-bearer until the token exists again. Written next to
    // TokenVariablesPublished, which stays a Verbose count, when missing > 0.
    [Event(EvtTokenVariablesMissing,
           Level = EventLevel.Warning,
           Keywords = JournalLifecycle,
           Tags = ObservationTags.Journal,
           Message = "MCP client tokens are missing from the vault | clients={0} | missing={1}")]
    public void TokenVariablesMissing(int clients, int missing)
    {
        if (IsEnabled()) WriteEvent(EvtTokenVariablesMissing, clients, missing);
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
           Tags = ObservationTags.Journal,
           Message = "MCP requests are admitted again | rejected={0}")]
    public void RequestRejectionsEnded(int rejected)
    {
        if (IsEnabled()) WriteEvent(EvtRequestRejectionsEnded, rejected);
    }

    // A tool call is short and the rate limiter bounds how many a client makes, so
    // its start stays Verbose and the journal keeps its failures; the work_id of
    // the start comes back on the call's end, whichever event writes it. An unknown
    // tool name is caller text and reads "unknown", a missing one reads "".
    [Event(EvtToolCallStarted,
           Level = EventLevel.Verbose,
           Keywords = Tool,
           Message = "mcp tool call started | surface={0} | tool={1} | work_id={2}")]
    public void ToolCallStarted(string surface, string tool, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtToolCallStarted, surface, tool, work_id);
    }

    // The end of a call that gave the client what it asked for, or that the client
    // withdrew. Never the arguments, never the result, never an exception message
    // (it can quote Anytype content). outcome: completed or cancelled; a call that
    // failed ends in ToolCallFailed.
    [Event(EvtToolCallCompleted,
           Level = EventLevel.Informational,
           Keywords = Tool,
           Message = "MCP tool call finished | outcome={2} | surface={0} | tool={1} | ex_type={3} | elapsed_ms={4:F1} | work_id={5}")]
    public void ToolCallCompleted(string surface, string tool, string outcome, string ex_type, double elapsed_ms, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtToolCallCompleted, surface, tool, outcome, ex_type, elapsed_ms, work_id);
    }

    // The end of a call the client got an error for. The cause is a type and, for an
    // Anytype API refusal, its status and machine code: never the exception message,
    // which the client receives in the tool result and which can quote Anytype
    // content. outcome: input_rejected, output_rejected, failed, unknown_tool,
    // missing_tool_name. status_code is 0 and error_code "none" when the cause
    // carries none.
    [Event(EvtToolCallFailed,
           Level = EventLevel.Warning,
           Keywords = Tool,
           Message = "MCP tool call failed | outcome={2} | surface={0} | tool={1} | ex_type={3} | status_code={4} | error_code={5} | elapsed_ms={6:F1} | work_id={7}")]
    public void ToolCallFailed(string surface, string tool, string outcome, string ex_type, int status_code, string error_code, double elapsed_ms, string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtToolCallFailed, surface, tool, outcome, ex_type, status_code, error_code, elapsed_ms, work_id);
    }
}
