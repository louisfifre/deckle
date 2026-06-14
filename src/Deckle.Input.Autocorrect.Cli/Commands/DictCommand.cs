using System.IO;
using Deckle.Core;
using Deckle.Input.Autocorrect;

namespace Deckle.Input.Autocorrect.Cli;

// Inspects and maintains the personal dictionary — the only persisted text in
// the module, and by doctrine user-removable. Operates on the same
// personal-dictionary.json the live engine reads, so changes here take effect
// on the next run.
internal static class DictCommand
{
    public static int Run(CliArgs args)
    {
        if (args.Positional.Count == 0)
        {
            Usage();
            return 1;
        }

        string dictPath = Path.Combine(
            AppPaths.GetModuleDirectory("autocorrect"), "personal-dictionary.json");
        using var dictionary = new PersonalDictionary(dictPath);

        string action = args.Positional[0].ToLowerInvariant();
        switch (action)
        {
            case "path":
                Console.WriteLine(dictPath);
                return 0;

            case "list":
                PrintWords(dictionary);
                PrintSuppressions(dictionary);
                return 0;

            case "remove":
                if (args.Positional.Count < 2) { Usage(); return 1; }
                {
                    bool removed = dictionary.RemoveWord(args.Positional[1]);
                    Console.WriteLine(removed
                        ? $"Removed word: {args.Positional[1]}"
                        : $"No such word: {args.Positional[1]}");
                    return 0;
                }

            case "remove-suppression":
                if (args.Positional.Count < 3) { Usage(); return 1; }
                {
                    bool removed = dictionary.RemoveSuppression(args.Positional[1], args.Positional[2]);
                    Console.WriteLine(removed
                        ? $"Removed suppression: {args.Positional[1]} -> {args.Positional[2]}"
                        : $"No such suppression: {args.Positional[1]} -> {args.Positional[2]}");
                    return 0;
                }

            case "purge":
                return Purge(dictionary);

            default:
                Usage();
                return 1;
        }
    }

    private static void PrintWords(PersonalDictionary dictionary)
    {
        var words = dictionary.SnapshotWords();
        Console.WriteLine($"Words ({words.Count}):");
        if (words.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        Console.WriteLine($"  {"word",-24}{"weight",10}  adopted");
        foreach (var (word, weight, adopted) in words)
            Console.WriteLine($"  {word,-24}{weight,10:0.0}  {adopted}");
    }

    private static void PrintSuppressions(PersonalDictionary dictionary)
    {
        var suppressions = dictionary.SnapshotSuppressions();
        Console.WriteLine();
        Console.WriteLine($"Suppressions ({suppressions.Count}):");
        if (suppressions.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }
        foreach (var (original, replacement) in suppressions)
            Console.WriteLine($"  {original} -> {replacement}");
    }

    // Destructive — confirm on stdin. Default is No: anything but an explicit
    // "y"/"yes" leaves the dictionary untouched.
    private static int Purge(PersonalDictionary dictionary)
    {
        Console.Write("Purge all words and suppressions? [y/N] ");
        string? answer = Console.ReadLine();
        if (answer is not null &&
            (answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
             answer.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)))
        {
            dictionary.Purge();
            Console.WriteLine("Purged.");
        }
        else
        {
            Console.WriteLine("Cancelled.");
        }
        return 0;
    }

    private static void Usage() => Console.Error.WriteLine(
        "Usage: dict list | remove <word> | remove-suppression <orig> <repl> | purge | path");
}
