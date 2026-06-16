using System.IO;
using System.Text;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;

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
    private static async Task<int> Main(string[] args)
    {
        ToolProfile profile = ToolProfileParser.Parse(args);
        bool managementEnabled = ManagementFlag.IsEnabled(args);

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
            var dialogues = new DialogueGestures(api, resolver);
            var management = new ManagementGestures(api, resolver);

            var tools = profile switch
            {
                ToolProfile.ProjectManagement => ToolCatalog.Build(session, tasks, projects, query),
                ToolProfile.Dialogues => DialogueToolCatalog.Build(dialogues),
                ToolProfile.All => ToolCatalog.Build(session, tasks, projects, query)
                    .Concat(DialogueToolCatalog.Build(dialogues))
                    .ToArray(),
                _ => throw new InvalidOperationException($"Profil MCP inconnu : {profile}."),
            };

            var descriptor = profile switch
            {
                ToolProfile.ProjectManagement => McpServer.ProjectManagementDescriptor,
                ToolProfile.Dialogues => McpServer.DialoguesDescriptor,
                ToolProfile.All => McpServer.AllDescriptor,
                _ => McpServer.ProjectManagementDescriptor,
            };

            // Mount the supervised management catalog on demand, additive to the
            // object-management surface. The Dialogues-only profile has no object to
            // delete, so the flag is a no-op there. Default (flag off) serves none of
            // these destructive tools.
            if (managementEnabled && profile != ToolProfile.Dialogues)
            {
                tools = tools.Concat(ManagementToolCatalog.Build(management)).ToArray();
                descriptor = descriptor with
                {
                    Instructions = descriptor.Instructions + "\n\n" + ManagementToolCatalog.Instructions,
                };
            }

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
            var server = new McpServer(tools, endpoint, descriptor);

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

internal enum ToolProfile
{
    ProjectManagement,
    Dialogues,
    All,
}

// The management catalog is mounted on demand, never by default: a consumer
// opts in through its mcp.json, either with the --management launch arg or by
// setting DECKLE_ANYTYPE_MANAGEMENT to a truthy value. An unsupervised consumer
// is served no destructive tool.
internal static class ManagementFlag
{
    const string EnvVar = "DECKLE_ANYTYPE_MANAGEMENT";

    public static bool IsEnabled(string[] args)
    {
        foreach (string arg in args)
            if (string.Equals(arg, "--management", StringComparison.OrdinalIgnoreCase))
                return true;

        return IsTruthy(Environment.GetEnvironmentVariable(EnvVar));
    }

    static bool IsTruthy(string? value) =>
        value is not null &&
        (value == "1"
         || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
         || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
         || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase));
}

internal static class ToolProfileParser
{
    public static ToolProfile Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--profile", StringComparison.OrdinalIgnoreCase))
                continue;

            string? value = i + 1 < args.Length ? args[i + 1] : null;
            return value?.ToLowerInvariant() switch
            {
                "pm" or "project-management" => ToolProfile.ProjectManagement,
                "dialogues" => ToolProfile.Dialogues,
                "all" => ToolProfile.All,
                _ => throw new ArgumentException(
                    "Profil MCP invalide. Profils attendus : pm, dialogues, all."),
            };
        }

        return ToolProfile.ProjectManagement;
    }
}
