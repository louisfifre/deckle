using System.Globalization;
using System.Text;

namespace Deckle.Installer;

// The stub's only trace. It is a NativeAOT WinExe with no console and takes no
// Deckle.Diagnostics reference, so there is no EventSource and nobody listening
// when it runs; the message box shows the user one sentence and is gone. A plain
// append-only file in %TEMP% is what survives the run, both for the install path
// (launched from Downloads) and the uninstall path (whose own folder is deleted at
// the end, so the log cannot live beside the exe).
//
// Writing must never fail the install or uninstall it observes: every write is
// wrapped, and a file that cannot be opened is simply skipped.
internal static class InstallLog
{
    // Kept small on purpose: the stub runs a handful of times per machine, and a
    // cap keeps a file nobody deletes from growing without bound.
    private const long MaxBytes = 512 * 1024;

    private static readonly object s_sync = new();

    public static string FilePath { get; } = Path.Combine(Path.GetTempPath(), "Deckle-Installer.log");

    // The exit code cannot tell a user's cancel from a failure (an install cancel
    // returns 1, a declined uninstall returns 0), so the flows mark the cancel and
    // the entry point reads it when it writes the run's outcome.
    public static bool Cancelled { get; private set; }

    public static void MarkCancelled() => Cancelled = true;

    public static void Info(string message) => Write("Information", message);

    public static void Warning(string message) => Write("Warning", message);

    public static void Error(string message) => Write("Error", message);

    public static void Verbose(string message) => Write("Verbose", message);

    // ex.Message can span lines; the file is one record per line so a reader can
    // grep it, hence the flattening.
    public static string Cause(Exception ex) =>
        $"ex_type={ex.GetType().Name} | ex_message={Flatten(ex.Message)}";

    public static string Flatten(string text) =>
        text.Replace('\r', ' ').Replace('\n', ' ');

    private static void Write(string level, string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} | {level} | pid={Environment.ProcessId} | {message}{Environment.NewLine}");
        lock (s_sync)
        {
            try
            {
                var info = new FileInfo(FilePath);
                FileMode mode = info.Exists && info.Length > MaxBytes ? FileMode.Create : FileMode.Append;
                using var stream = new FileStream(FilePath, mode, FileAccess.Write, FileShare.ReadWrite);
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                stream.Write(bytes, 0, bytes.Length);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
