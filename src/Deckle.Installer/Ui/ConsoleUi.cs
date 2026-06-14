using System.Runtime.InteropServices;

namespace Deckle.Installer;

// ── ConsoleUi ─────────────────────────────────────────────────────────────────
//
// The whole visual surface of the installer: coloured sections, step headers,
// status lines, prompts, and the download progress bar. The intent is the feel
// of Louis's hand-written PowerShell scripts (Step/Ok/Warn idiom, cyan headers,
// green confirmations) but compiled into the stub — never a "raw console".
//
// Colour is ANSI (virtual terminal sequences). Windows Terminal renders them out
// of the box; the legacy conhost a double-click may spawn needs VT processing
// enabled explicitly, which EnableVirtualTerminal() does once at startup. If that
// fails (redirected output, an exotic host), Enabled flips to false and every
// helper degrades to plain text rather than printing escape garbage.
internal static partial class ConsoleUi
{
    private static bool s_colour = true;

    // ── ANSI palette — semantic, not literal, mirroring the PS scripts ───────────
    private const string Reset = "\e[0m";
    private const string Bold = "\e[1m";
    private const string Dim = "\e[2m";
    private const string Cyan = "\e[36m";   // section headers      ([publish] Step idiom)
    private const string Green = "\e[32m";  // success / Ok
    private const string Yellow = "\e[33m"; // warning / Warn
    private const string Red = "\e[31m";    // error
    private const string Grey = "\e[90m";   // secondary detail

    private static string Paint(string code, string text) => s_colour ? code + text + Reset : text;

    // ── Startup ──────────────────────────────────────────────────────────────────

    public static void EnableVirtualTerminal()
    {
        try
        {
            nint stdout = GetStdHandle(STD_OUTPUT_HANDLE);
            if (stdout == nint.Zero || stdout == new nint(-1)) { s_colour = false; return; }
            if (!GetConsoleMode(stdout, out uint mode)) { s_colour = false; return; }
            if (!SetConsoleMode(stdout, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING)) s_colour = false;
        }
        catch { s_colour = false; }
    }

    // ── Sections and status ──────────────────────────────────────────────────────

    // The product banner shown once at the top.
    public static void Banner(string title, string subtitle)
    {
        Console.WriteLine();
        Console.WriteLine(Paint(Bold + Cyan, "  " + title));
        Console.WriteLine(Paint(Grey, "  " + subtitle));
        Console.WriteLine();
    }

    // A numbered step header — "[3/7] Downloading…". The count makes the flow's
    // length legible up front; no phantom bar that "loads into the void".
    public static void Step(int index, int total, string label)
    {
        Console.WriteLine();
        Console.WriteLine(Paint(Cyan, $"  [{index}/{total}] ") + Paint(Bold, label));
    }

    public static void Ok(string message) => Console.WriteLine(Paint(Green, "      ✓ ") + message);
    public static void Warn(string message) => Console.WriteLine(Paint(Yellow, "      ! ") + message);
    public static void Error(string message) => Console.WriteLine(Paint(Red, "      ✗ ") + message);
    public static void Info(string message) => Console.WriteLine(Paint(Grey, "      · ") + message);

    // ── Prompts ──────────────────────────────────────────────────────────────────

    // Asks for a value, showing the default in brackets — Enter accepts it. The
    // default-accepting prompt is the console analogue of a pre-filled field; the
    // data-folder prompt is the one screen that actually matters (models off C:).
    public static string Prompt(string label, string defaultValue)
    {
        Console.Write(Paint(Bold, "      " + label + " "));
        Console.Write(Paint(Grey, $"[{defaultValue}]: "));
        string? entered = Console.ReadLine();
        return string.IsNullOrWhiteSpace(entered) ? defaultValue : entered.Trim();
    }

    // Yes/no with a default. Empty input takes the default.
    public static bool Confirm(string label, bool defaultYes)
    {
        string hint = defaultYes ? "[Y/n]" : "[y/N]";
        Console.Write(Paint(Bold, "      " + label + " "));
        Console.Write(Paint(Grey, hint + ": "));
        string? entered = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(entered)) return defaultYes;
        return entered.StartsWith('y') || entered.StartsWith('Y');
    }

    // ── Download progress ────────────────────────────────────────────────────────

    // A single in-place bar (carriage return, no scroll spam). Driven by real
    // byte counts, never a timer — the progression is derived from actual advance,
    // per the no-phantom-bar rule. Call Done() once after the loop to drop to a
    // fresh line.
    public static void ProgressBar(long downloaded, long? total)
    {
        const int width = 28;
        string sizePart = total is > 0
            ? $"{Mb(downloaded),7:0.0} / {Mb(total.Value),-7:0.0} MB"
            : $"{Mb(downloaded),7:0.0} MB";

        if (total is > 0)
        {
            double ratio = Math.Clamp((double)downloaded / total.Value, 0, 1);
            int filled = (int)(ratio * width);
            string bar = new string('█', filled) + new string('░', width - filled);
            Console.Write("\r" + Paint(Grey, "      ") + Paint(Green, bar) + $"  {ratio,4:P0}  " + Paint(Grey, sizePart));
        }
        else
        {
            Console.Write("\r" + Paint(Grey, "      downloading  ") + Paint(Grey, sizePart));
        }
    }

    public static void ProgressDone() => Console.WriteLine();

    private static double Mb(long bytes) => bytes / (1024.0 * 1024.0);

    // ── Win32 console mode ───────────────────────────────────────────────────────

    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
