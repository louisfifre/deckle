using System.Runtime.InteropServices;

namespace Deckle.Installer;

// ── ConsoleUi ─────────────────────────────────────────────────────────────────
//
// The whole visual surface of the installer: the DECKLE banner, the consent
// recap, phase headers, status ticks, single-key choices, and the download
// progress bar. The grammar it serves is recap → one keystroke → unattended run:
// everything the user must know sits in one block up front, and after consent the
// run prints compact ✓ lines as real work completes — never a "raw console",
// never phantom progress.
//
// Colour is ANSI (virtual terminal sequences). Windows Terminal renders them out
// of the box; the legacy conhost a double-click may spawn needs VT processing
// enabled explicitly, which EnableVirtualTerminal() does once at startup. If that
// fails (redirected output, an exotic host), s_colour flips to false and every
// helper degrades to plain text rather than printing escape garbage.
internal static partial class ConsoleUi
{
    private static bool s_colour = true;

    // ── ANSI palette — semantic, not literal, mirroring the PS scripts ───────────
    private const string Reset = "\e[0m";
    private const string Bold = "\e[1m";
    private const string Blue = "\e[34m";   // banner — the menu chrome's identity colour
    private const string Cyan = "\e[36m";   // phase headers        ([publish] Step idiom)
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

    // ── Banner ───────────────────────────────────────────────────────────────────

    // The DECKLE figlet from scripts/lib/menu/chrome.ps1 (Get-MenuBanner) — one
    // console identity shared by the dev menu and the installer. Duplicated because
    // PowerShell and C# cannot share a source; keep the two in sync.
    private static readonly string[] BannerArt =
    [
        @"  ____   _____   ____  _  __  _      _____",
        @" |  _ \ | ____| / ___|| |/ / | |    | ____|",
        @" | | | ||  _|  | |    |   /  | |    |  _|",
        @" | |_| || |___ | |___ |   \  | |___ | |___",
        @" |____/ |_____| \____||_|\_\ |_____||_____|",
    ];

    // The cover page: figlet, a letter-spaced tagline ("Installer" →
    // I N S T A L L E R, the menu's "S C R I P T S" idiom), a closing rule.
    public static void Banner(string tagline)
    {
        Console.WriteLine();
        foreach (string line in BannerArt) Console.WriteLine(Paint(Blue, " " + line));
        string spaced = string.Join(' ', tagline.ToUpperInvariant().ToCharArray());
        Console.WriteLine(Paint(Blue, "   " + spaced));
        Console.WriteLine(Paint(Grey, "  " + new string('─', 43)));
        Console.WriteLine();
    }

    // ── Recap block ──────────────────────────────────────────────────────────────

    // The one-line summary of what is about to happen —
    // "Deckle v0.7.0 — ready to install (28 MB download, no admin)".
    public static void Headline(string text) => Console.WriteLine(Paint(Bold, "  " + text));

    // An aligned label/value row of the recap ("App    C:\…").
    public static void Row(string label, string value) =>
        Console.WriteLine(Paint(Grey, "    " + label.PadRight(4) + "   ") + value);

    // A continuation note aligned under the previous row's value.
    public static void RowNote(string note) =>
        Console.WriteLine(Paint(Grey, new string(' ', 11) + note));

    // The single line inviting the keystroke ("Enter installs · C changes…").
    public static void Hint(string text) => Console.WriteLine(Paint(Grey, "  " + text));

    // ── Phases and status ────────────────────────────────────────────────────────

    // A phase header ("Installing", "Removing") — opens the unattended run.
    public static void Phase(string label)
    {
        Console.WriteLine();
        Console.WriteLine(Paint(Cyan + Bold, "  " + label));
    }

    public static void Ok(string message) => Console.WriteLine(Paint(Green, "    ✓ ") + message);
    public static void Warn(string message) => Console.WriteLine(Paint(Yellow, "    ! ") + message);
    public static void Error(string message) => Console.WriteLine(Paint(Red, "    ✗ ") + message);
    public static void Info(string message) => Console.WriteLine(Paint(Grey, "    · ") + message);

    // The final state — the run's receipt line.
    public static void Success(string message) =>
        Console.WriteLine(Paint(Bold + Green, "  ✓ " + message));

    // ── Interaction ──────────────────────────────────────────────────────────────

    // Blocks until one of the accepted keys is pressed; every other key is ignored.
    // Redirected input cannot press keys — the first accepted key is the default
    // action and stands in, mirroring --yes.
    public static ConsoleKey WaitKey(params ConsoleKey[] accepted)
    {
        if (Console.IsInputRedirected) return accepted[0];
        while (true)
        {
            ConsoleKey key = ReadKeyRaw().Key;
            if (Array.IndexOf(accepted, key) >= 0) return key;
        }
    }

    // Single-key yes/no. Enter takes the default; the chosen letter is echoed so
    // the transcript keeps the decision.
    public static bool Confirm(string label, bool defaultYes)
    {
        string hint = defaultYes ? "[Y/n]" : "[y/N]";
        Console.Write(Paint(Bold, "  " + label + " ") + Paint(Grey, hint + " "));
        if (Console.IsInputRedirected) { Console.WriteLine(); return defaultYes; }
        while (true)
        {
            bool? choice = ReadKeyRaw().Key switch
            {
                ConsoleKey.Enter => defaultYes,
                ConsoleKey.Y => true,
                ConsoleKey.N => false,
                _ => null,
            };
            if (choice is { } picked)
            {
                Console.WriteLine(picked ? "y" : "n");
                return picked;
            }
        }
    }

    // Folder editor for the "change the folders" branch. The current value gets its
    // own line — a long path never wraps a bracketed default — and Enter keeps it.
    public static string PromptPath(string label, string current)
    {
        Console.WriteLine(Paint(Bold, "    " + label));
        Console.WriteLine(Paint(Grey, "      current  ") + current);
        Console.Write(Paint(Bold, "      new      "));
        string? entered = Console.ReadLine();
        return string.IsNullOrWhiteSpace(entered) ? current : entered.Trim();
    }

    // Keeps a double-clicked console alive until the user lets go of it.
    public static void HoldOpen()
    {
        if (Console.IsInputRedirected) return;
        Console.WriteLine();
        Info("Press Enter to close…");
        Console.ReadLine();
    }

    // Reads one key without echo. Ctrl+C is taken as input — a pending ReadKey
    // would otherwise swallow the break signal — and mapped onto the same
    // cancellation path as everywhere else.
    private static ConsoleKeyInfo ReadKeyRaw()
    {
        bool previous = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;
        try
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0)
                throw new OperationCanceledException();
            return key;
        }
        finally { Console.TreatControlCAsInput = previous; }
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
            Console.Write("\r    " + Paint(Green, bar) + $"  {ratio,4:P0}  " + Paint(Grey, sizePart));
        }
        else
        {
            Console.Write("\r" + Paint(Grey, "    downloading  ") + Paint(Grey, sizePart));
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
