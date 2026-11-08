namespace Deckle.Shell.Taskbar.Harness;

internal sealed class HarnessOptions
{
    public string? DllPath { get; private init; }
    public string? Root { get; private init; }
    public string? SendFile { get; private init; }
    public string? TreeFile { get; private init; }
    public double TreeAfter { get; private init; } = 6;
    public double? StopAfter { get; private init; }
    public string? ControlFile { get; private init; }
    public bool Visual { get; private init; }
    public string? TargetProcess { get; private init; }
    public bool Packaged { get; private init; }

    public static HarnessOptions Parse(string[] args)
    {
        string? dll = null, root = null, send = null, tree = null, control = null;
        double treeAfter = 6;
        double? stopAfter = null;
        bool visual = false, packaged = false;
        string? target = null;

        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--dll": dll = Next(); break;
                case "--root": root = Next(); break;
                case "--send": send = Next(); break;
                case "--tree": tree = Next(); break;
                case "--tree-after": treeAfter = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--stop-after": stopAfter = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--control": control = Next(); break;
                case "--visual": visual = true; break;
                case "--process": target = Next(); break;
                case "--packaged": packaged = true; break;
                default: throw new ArgumentException($"unknown option {args[i]}");
            }
        }

        return new HarnessOptions
        {
            DllPath = dll, Root = root, SendFile = send, TreeFile = tree, TreeAfter = treeAfter,
            StopAfter = stopAfter, ControlFile = control, Visual = visual, TargetProcess = target, Packaged = packaged,
        };
    }
}

