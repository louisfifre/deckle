namespace Deckle.Diagnostics;

// What a run identity line may say about its command line. The line travels
// with the log to another machine, and a command line can carry a path
// under the user's profile or a value typed for a switch, so only the switch
// names are kept: what the process was asked to do, never what it was given.
public static class CommandLineSwitches
{
    // The names of the switches in the arguments, in order, comma-joined, with
    // any "=value" cut off; "none" when there is none. An argument that does not
    // start with "--" is a value and is dropped.
    public static string Names(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var names = new List<string>();
        foreach (string arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;
            int equals = arg.IndexOf('=');
            names.Add(equals < 0 ? arg : arg[..equals]);
        }
        return names.Count == 0 ? "none" : string.Join(',', names);
    }
}
