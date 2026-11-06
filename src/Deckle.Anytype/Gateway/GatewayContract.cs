namespace Deckle.Anytype;

// The names both sides of the Anytype gateway agree on: the resident app, which
// stages and supervises the gateway process, and the gateway itself, which serves
// MCP on the loopback port (ADR-0008). Each is a wire contract between two
// executables that can run different builds, so none is renamed lightly.
public static class GatewayContract
{
    // Loopback, outside every neighbour's range: 3100x-3101x is Anytype, 11434 is
    // Ollama, and the Windows dynamic range opens at 49152. External clients keep
    // this URL across gateway and Deckle restarts.
    public const int Port = 33255;

    // The gateway's image name. The supervisor recognizes a gateway by this
    // name under its versions folder, never by the name alone.
    public const string ExecutableName = "Deckle.Anytype.Gateway.exe";

    // The folder beside Deckle.exe that holds the gateway's build output. The
    // gateway never runs from there: the supervisor copies it into a versioned
    // folder first, so a rebuild can overwrite the payload while it runs.
    public const string PayloadFolderName = "Gateway";

    // One gateway per user session; a second start exits at once.
    public const string InstanceMutexName = @"Local\Deckle.Anytype.Gateway";

    // The resident app answers the delete's second validation on this pipe
    // (current user only), since only the app can show a toast.
    public const string DeletionApprovalPipeName = "Deckle.Anytype.DeletionApproval";

    // Set by the supervisor to ask one gateway process to drain and exit. Named
    // per process id so a replacement never stops the wrong instance.
    public static string StopEventName(int processId) => $@"Local\Deckle.Anytype.Gateway.Stop.{processId}";

    // The gateway's exit codes the supervisor acts on.
    public static class ExitCodes
    {
        public const int Stopped = 0;
        // Another gateway already holds the instance mutex.
        public const int AlreadyRunning = 2;
        // The Anytype credentials or the token vault are missing: restarting
        // cannot help until the person provisions them.
        public const int NotProvisioned = 3;
        // The HTTP host could not bind its port.
        public const int ListenFailed = 4;
        // An unexpected exception ended the process.
        public const int Faulted = 5;
    }
}
