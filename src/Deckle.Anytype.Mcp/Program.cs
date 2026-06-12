using System.IO;
using System.Text;
using Deckle.Anytype.Api;
using Deckle.Anytype.Gestures;
using Deckle.Anytype.Mcp.JsonRpc;
using Deckle.Anytype.Mcp.Tools;

namespace Deckle.Anytype.Mcp;

// Composition root for the stdio MCP host. Builds the Anytype stack, wires
// the stderr log sink, and runs the dispatcher on raw console streams.
//
// stdout framing is the load-bearing detail: protocol messages are
// newline-delimited UTF-8 without a BOM, so we open the standard streams
// directly with our own UTF8Encoding(false) rather than trusting the
// platform console code page. AutoFlush guarantees each line leaves the
// process before the next read blocks. stderr stays the only log channel.
internal static class Program
{
    private static async Task<int> Main()
    {
        // The listener must subscribe before any provider emits, so attach it
        // first — its EnableEvents also lights up sources created later.
        using var listener = new StderrEventListener(Console.Error);

        AnytypeApiClient api;
        try
        {
            var credentials = AnytypeCredentials.Load();
            api = new AnytypeApiClient(credentials);
        }
        catch (InvalidOperationException ex)
        {
            // Missing or incomplete credentials: the message already carries
            // the remediation. No stack trace on the wire.
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        using (api)
        {
            var resolver = new NameResolver(api);
            var session = new SessionGestures(api, resolver);
            var tasks = new TaskGestures(api, resolver);
            var projects = new ProjectGestures(api, resolver);
            var query = new QueryGestures(api, resolver);

            var tools = ToolCatalog.Build(session, tasks, projects, query);

            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using var stdin = new StreamReader(Console.OpenStandardInput(), utf8NoBom);
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8NoBom)
            {
                AutoFlush = true,
            };
            // Single '\n' line terminator: the protocol forbids embedded
            // newlines and Content-Length framing, so we pin LF rather than
            // inherit the platform CRLF.
            stdout.NewLine = "\n";

            var endpoint = new JsonRpcEndpoint(stdin, stdout);
            var server = new McpServer(tools, endpoint);

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true; // graceful: let RunAsync unwind, do not kill.
                cts.Cancel();
            };

            try
            {
                await server.RunAsync(cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Ctrl+C requested shutdown — a clean exit, not a failure.
            }
        }

        return 0;
    }
}
