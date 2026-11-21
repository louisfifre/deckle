using System.Runtime.InteropServices;
using System.Text.Json;

namespace Deckle.Hosting.SelfCheck;

// How a self-check run ends when it is refused, shared by every executable of
// the suite.
public static class SelfCheckProcess
{
    // Exit codes the script reads next to the report.
    public const int Passed = 0;
    public const int Failed = 1;
    public const int Refused = 2;

    // A refusal writes its reason beside where the report would have been, so the
    // caller reads it. The process then ends with no handler
    // (TerminateWithoutExitHandlers): ProcessExit would touch AppPaths, which is
    // the root the gate has just protected.
    public static void WriteRefusal(string? outputFolder, string reason)
    {
        if (string.IsNullOrWhiteSpace(outputFolder)) return;
        try
        {
            Directory.CreateDirectory(outputFolder);
            File.WriteAllText(Path.Combine(outputFolder, SelfCheckReport.FileName),
                JsonSerializer.Serialize(
                    new { outcome = "refused", reason },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // The exit code still says refused.
        }
    }

    public static void TerminateWithoutExitHandlers(int code)
        => TerminateProcess(GetCurrentProcess(), (uint)code);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
}
