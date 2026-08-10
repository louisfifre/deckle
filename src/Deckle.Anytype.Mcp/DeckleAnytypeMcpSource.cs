using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Anytype.Mcp;

// MCP host provider. Covers the resident streamable-HTTP host that serves the
// Anytype toolset to Claude Code and Codex: the listener's lifecycle and the
// requests it fields. Milestones (Info/Warning, no args) read as a
// narrative of the host's life; the Verbose mirrors carry the greppable detail.
//
// No event ever carries a bearer token; the vault secret stays out of the trace.
[EventSource(Name = "Deckle-AnytypeMcp")]
public sealed class DeckleAnytypeMcpSource : DeckleEventSource
{
    public static readonly DeckleAnytypeMcpSource Log = new();

    private DeckleAnytypeMcpSource() { }

    // Module-local keyword bit (transverse bits 0..9 reserved in Keywords;
    // 0x400+ belongs to the provider). The host's start/stop and request
    // activity share the one Lifecycle family.
    private const EventKeywords Lifecycle = (EventKeywords)0x400;

    // ── EventIds ─────────────────────────────────────────────────────────
    public const int EvtHostStarted          = 1;
    public const int EvtHostStartedDetail     = 2;
    public const int EvtHostStopped           = 3;
    public const int EvtHostNotProvisioned    = 4;
    // Event ids 5 and 6 belonged to the removed stateful session transport and
    // stay reserved so an old trace can never be misread as a new event.
    public const int EvtRequestRejected       = 7;
    public const int EvtRequestRejectedDetail = 8;

    // ── Host lifecycle ────────────────────────────────────────────────────

    [Event(EvtHostStarted,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
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

    [Event(EvtHostStopped,
           Level = EventLevel.Informational,
           Keywords = Lifecycle,
           Message = "The MCP host stopped")]
    public void HostStopped()
    {
        if (IsEnabled()) WriteEvent(EvtHostStopped);
    }

    // Warning: the host cannot listen because the vault holds no credentials —
    // a state the user must act on before any client can connect.
    [Event(EvtHostNotProvisioned,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Message = "The MCP host is not provisioned")]
    public void HostNotProvisioned()
    {
        if (IsEnabled()) WriteEvent(EvtHostNotProvisioned);
    }

    // ── Requests ──────────────────────────────────────────────────────────

    // Warning: a request the host refused before dispatch (bad auth, Origin or
    // method) — a degradation a human would want to notice.
    [Event(EvtRequestRejected,
           Level = EventLevel.Warning,
           Keywords = Lifecycle,
           Message = "An MCP request was rejected")]
    public void RequestRejected()
    {
        if (IsEnabled()) WriteEvent(EvtRequestRejected);
    }

    [Event(EvtRequestRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = Lifecycle,
           Message = "mcp request rejected | reason={0} | status={1}")]
    public void RequestRejectedDetail(string reason, int status_code)
    {
        if (IsEnabled()) WriteEvent(EvtRequestRejectedDetail, reason, status_code);
    }
}
