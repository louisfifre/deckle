using System.Diagnostics;
using Deckle.Shell.Taskbar;

namespace Deckle.Shell.Taskbar.Harness;

internal static class HarnessCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = HarnessOptions.Parse(args);
        string source = options.DllPath ?? Path.Combine(AppContext.BaseDirectory, VersionedNativeCopy.DllName);
        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"harness: native DLL not found at {source} (build Deckle.Shell.Taskbar first)");
            return 2;
        }

        var copy = new VersionedNativeCopy(options.Root ?? VersionedNativeCopy.DefaultRoot());
        var recorder = new VisualTreeRecorder();
        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };

        await using var host = new TaskbarEngineHost(source, copy, new TaskbarPipeServer(TaskbarPipeServer.DefaultName()));
        long connectedAt = 0;
        host.MessageReceived += message =>
        {
            switch (message)
            {
                case NativeHello hello:
                    connectedAt = Environment.TickCount64;
                    Print($"hello   explorer pid {hello.ProcessId}, protocol {hello.Protocol}, {hello.ModulePath}");
                    break;
                case NativeLogRecord record:
                    recorder.Record(record);
                    bool isTree = record.Event is "visual_add" or "visual_remove";
                    if (!isTree || options.Visual)
                        Print($"{record.Level.ToString().ToLowerInvariant(),-7} {record.Event}  {record.Message}  {record.FieldsText()}");
                    break;
                case NativeUnreadableMessage unreadable:
                    Print("unreadable  " + unreadable.Line);
                    break;
            }
        };

        Print($"source  {source}");
        InjectionResult injected = host.Start();
        Print($"copy    {host.LoadedPath}");
        Print(injected.Succeeded
            ? "inject  hook sent to explorer; waiting for the DLL (Ctrl+C stops it)"
            : $"inject  FAILED at {injected.Step} (win32 {injected.Win32Error})");

        _ = Task.Run(() => WatchAsync(options, host, recorder, stop, () => connectedAt));

        await stop.Task;
        return await StopAsync(host, recorder, options);
    }

    // Timers and the control file, on a background task.
    private static async Task WatchAsync(
        HarnessOptions options, TaskbarEngineHost host, VisualTreeRecorder recorder,
        TaskCompletionSource stop, Func<long> connectedAt)
    {
        var started = Stopwatch.StartNew();
        bool sent = false;
        bool treeWritten = false;
        while (!stop.Task.IsCompleted)
        {
            await Task.Delay(200);

            if (options.StopAfter is { } stopAfter && started.Elapsed.TotalSeconds >= stopAfter)
            {
                Print("harness stop-after reached");
                stop.TrySetResult();
                return;
            }

            if (host.IsConnected && !sent && options.SendFile is { } file)
            {
                sent = true;
                await SendFileAsync(host, file);
            }

            long at = connectedAt();
            if (host.IsConnected && !treeWritten && options.TreeFile is { } tree && at != 0
                && Environment.TickCount64 - at > options.TreeAfter * 1000 && recorder.MillisecondsSinceLastChange > 1500)
            {
                treeWritten = true;
                WriteTree(recorder, tree);
            }

            if (options.ControlFile is { } control && File.Exists(control))
            {
                string[] lines;
                try { lines = File.ReadAllLines(control); File.Delete(control); }
                catch (IOException) { continue; }

                foreach (string line in lines.Select(l => l.Trim()).Where(l => l.Length > 0))
                {
                    if (line == "stop") { stop.TrySetResult(); return; }
                    if (line == "ping") await host.SendAsync(NativeCommand.Ping);
                    else if (line.StartsWith("send ")) await SendFileAsync(host, line[5..].Trim());
                    else if (line.StartsWith("tree ")) WriteTree(recorder, line[5..].Trim());
                    else Print("control  unknown command: " + line);
                }
            }
        }
    }

    private static async Task SendFileAsync(TaskbarEngineHost host, string file)
    {
        try
        {
            string line = NativeCommand.Apply(await File.ReadAllTextAsync(file));
            bool ok = await host.SendAsync(line);
            Print($"send    {file} -> {(ok ? "sent" : "NOT sent")} ({line.Length} chars)");
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            Print($"send    {file} failed: {exception.Message}");
        }
    }

    private static void WriteTree(VisualTreeRecorder recorder, string path)
    {
        File.WriteAllText(path, recorder.Dump());
        Print($"tree    {recorder.Count} elements written to {path}");
    }

    private static async Task<int> StopAsync(TaskbarEngineHost host, VisualTreeRecorder recorder, HarnessOptions options)
    {
        if (options.TreeFile is { } tree && recorder.Count > 0) WriteTree(recorder, tree);

        int explorer = host.ExplorerProcessId;
        string loaded = host.LoadedPath;
        Print("stop    asking the DLL to restore, unadvise and unload");
        bool unloaded = await host.StopEngineAsync();
        Print(unloaded ? "stop    the DLL reported that it unloaded" : "stop    the DLL did NOT report an unload");

        // The pipe closing is the DLL's word. The proof is explorer's module list.
        bool moduleGone = explorer == 0 || await WaitAsync(() => !IsModuleLoaded(explorer, loaded), TimeSpan.FromSeconds(10));
        Print(moduleGone ? "stop    explorer no longer lists the DLL" : "stop    explorer STILL lists the DLL");

        bool unlocked = TryOpenExclusively(loaded);
        Print(unlocked ? "stop    the DLL file can be opened for writing (not locked)" : "stop    the DLL file is LOCKED");

        bool alive = explorer == 0 || IsProcessAlive(explorer);
        Print(alive ? "stop    explorer is still running" : "stop    explorer is GONE");

        return unloaded && moduleGone && unlocked && alive ? 0 : 3;
    }

    private static bool IsModuleLoaded(int processId, string path)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            foreach (ProcessModule module in process.Modules)
            {
                if (string.Equals(module.FileName, path, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryOpenExclusively(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static async Task<bool> WaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (condition()) return true;
            await Task.Delay(250);
        }
        return condition();
    }

    private static void Print(string line) =>
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
}
