using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Anytype.Gateway;

// The gateway's own log. It lives beside Deckle's in the diagnostics folder but
// in its own file: two processes writing one JSONL file would interleave lines
// and each keep its own rotation count. It keeps what Deckle's log keeps, every
// Warning and above and every milestone, from every Deckle-* provider the
// gateway loads (the MCP host, the Anytype gestures, Home).
internal static class GatewayDiagnostics
{
    public const string FileName = "gateway.jsonl";

    private static DispatchEventListener? _dispatch;

    public static void Initialize(string diagnosticsDirectory)
    {
        if (_dispatch is not null) return;

        Directory.CreateDirectory(diagnosticsDirectory);
        var dispatch = new DispatchEventListener();
        dispatch.AddSink(new JsonlSink(
            filePath:  Path.Combine(diagnosticsDirectory, FileName),
            kindLabel: "log",
            predicate: static e => e.Kind == ObservationKind.Operational
                && (e.Level <= EventLevel.Warning || e.Milestone),
            schema:    JsonlSchema.SelfDescribing,
            rotation:  new JsonlRotationPolicy(maxLines: 4000, maxArchives: 5)),
            replayBoot: true);
        dispatch.CompleteBoot();
        _dispatch = dispatch;
    }

    // Every exit crosses this barrier: the sink writes through a bounded queue.
    public static void Flush() => _dispatch?.FlushSinks(TimeSpan.FromSeconds(2));
}
