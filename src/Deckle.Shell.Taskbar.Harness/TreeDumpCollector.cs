using System.Text;

namespace Deckle.Shell.Taskbar.Harness;

// Asks the engine for the live visual trees and writes them to a file when the
// last record arrives. One request at a time.
internal sealed class TreeDumpCollector
{
    private readonly object _gate = new();
    private StringBuilder? _text;
    private string? _path;

    public async Task RequestAsync(TaskbarEngineHost host, string path)
    {
        lock (_gate)
        {
            _text = new StringBuilder();
            _path = path;
        }
        bool sent = await host.SendAsync(NativeCommand.Dump);
        HarnessCommand.Print($"dump    {(sent ? "requested" : "NOT sent")} -> {path}");
    }

    public void Handle(NativeMessage message, Action<string> print)
    {
        lock (_gate)
        {
            if (_text is null || _path is null) return;

            if (message is NativeTreeDump tree)
            {
                _text.AppendLine($"# thread {tree.Tid}").Append(tree.Text);
            }
            else if (message is NativeTreeEnd end)
            {
                File.WriteAllText(_path, _text.ToString());
                print($"dump    {end.Roots} roots written to {_path}");
                _text = null;
                _path = null;
            }
        }
    }
}
