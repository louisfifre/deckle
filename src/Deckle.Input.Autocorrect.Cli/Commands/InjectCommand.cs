using Deckle.Input.Autocorrect;

namespace Deckle.Input.Autocorrect.Cli;

// Étape-2 micro-deliverable: type a literal string into whatever window holds
// focus when the countdown ends. It is the bare TextInjector path — a human
// switches to a target app during the countdown and verifies the Unicode
// keystroke synthesis lands correctly (accents, ligatures, everything).
internal static class InjectCommand
{
    public static int Run(CliArgs args)
    {
        if (args.Positional.Count == 0)
        {
            Console.Error.WriteLine("Usage: inject <text> [--delay-ms 3000]");
            return 1;
        }

        string text = string.Join(' ', args.Positional);
        int delayMs = args.IntOr("--delay-ms", 3000);

        Console.WriteLine($"Injecting in {delayMs} ms — focus the target window now.");
        int remaining = delayMs;
        while (remaining > 0)
        {
            Console.WriteLine($"  {(remaining + 999) / 1000}..."); // seconds, rounded up
            int step = Math.Min(1000, remaining);
            Thread.Sleep(step);
            remaining -= step;
        }

        var injector = new TextInjector();
        bool ok = injector.TypeText(text);
        Console.WriteLine(ok ? "Injected." : "Injection failed (partial SendInput).");
        return ok ? 0 : 1;
    }
}
