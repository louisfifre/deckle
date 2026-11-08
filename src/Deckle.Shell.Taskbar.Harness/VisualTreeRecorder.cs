using System.Text;

namespace Deckle.Shell.Taskbar;

// Rebuilds the XAML visual trees from the DLL's visual_add and visual_remove
// records and prints them as indented text, one element per line:
//
//   Taskbar.TaskbarFrame#TaskbarFrame  [Width=.. ; ~actual=1920x48]
//
// A record carries its own handle (h), its parent's (p) and its child index
// (i); an element whose parent was never reported is a root.
internal sealed class VisualTreeRecorder
{
    private sealed class Node
    {
        public required string Handle { get; init; }
        public required string Parent { get; init; }
        public required int Index { get; init; }
        public required string Type { get; init; }
        public required string Name { get; init; }
        public required string Props { get; init; }
        public List<Node> Children { get; } = [];
    }

    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _lastChangeTicks = Environment.TickCount64;

    public int Count { get { lock (_gate) return _nodes.Count; } }

    public long MillisecondsSinceLastChange => Environment.TickCount64 - Interlocked.Read(ref _lastChangeTicks);

    public void Record(NativeLogRecord record)
    {
        if (record.Event is not ("visual_add" or "visual_remove")) return;
        lock (_gate)
        {
            string handle = record.Fields.GetValueOrDefault("h", "");
            if (record.Event == "visual_remove")
            {
                _nodes.Remove(handle);
            }
            else
            {
                _nodes[handle] = new Node
                {
                    Handle = handle,
                    Parent = record.Fields.GetValueOrDefault("p", ""),
                    Index = int.TryParse(record.Fields.GetValueOrDefault("i", ""), out int index) ? index : 0,
                    Type = record.Fields.GetValueOrDefault("type", ""),
                    Name = record.Fields.GetValueOrDefault("name", ""),
                    Props = record.Fields.GetValueOrDefault("props", ""),
                };
            }
            Interlocked.Exchange(ref _lastChangeTicks, Environment.TickCount64);
        }
    }

    public string Dump()
    {
        lock (_gate)
        {
            foreach (Node node in _nodes.Values) node.Children.Clear();
            var roots = new List<Node>();
            foreach (Node node in _nodes.Values)
            {
                if (_nodes.TryGetValue(node.Parent, out Node? parent) && !ReferenceEquals(parent, node))
                    parent.Children.Add(node);
                else
                    roots.Add(node);
            }

            var text = new StringBuilder();
            foreach (Node root in roots.OrderBy(node => node.Handle, StringComparer.Ordinal))
                Write(text, root, 0);
            return text.ToString();
        }
    }

    private static void Write(StringBuilder text, Node node, int depth)
    {
        text.Append(' ', depth * 2).Append(node.Type);
        if (node.Name.Length > 0) text.Append('#').Append(node.Name);
        if (node.Props.Length > 0) text.Append("  [").Append(node.Props).Append(']');
        text.AppendLine();
        foreach (Node child in node.Children.OrderBy(child => child.Index))
            Write(text, child, depth + 1);
    }
}

