using System.Runtime.InteropServices;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// One engine per process (native residency.h). Two copies of the DLL are
// started in this test process through the same entry explorer uses, the
// exported hook procedure, each copy on a pipe of its own. The second copy
// must leave without starting an engine: on 2026-11-10 a second engine beside
// a live one (the development console's copy next to the app's) called
// InitializeXamlDiagnosticsEx a second time in one explorer, which then failed
// fast in Windows.UI.Xaml.dll (ExplorerReinjectionBugNotes.md).
//
// Only the refusal is reachable here: this process has no XAML core, so the
// first copy's XAML Diagnostics never answer and it cannot park. Parking and
// the hand-over to a later copy are checked in a live explorer (the bug note's
// verification).
[Trait("Category", "regression")]
public sealed class EngineResidencyTests : IAsyncDisposable
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(15);
    private const int HcAction = 0;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "deckle-residency-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _stop = new();
    private readonly List<(TaskbarPipeServer Server, Task Run)> _servers = [];
    private readonly List<nint> _modules = [];

    [StructLayout(LayoutKind.Sequential)]
    private struct CallWindowProcData
    {
        public nint LParam;
        public nint WParam;
        public uint Message;
        public nint Window;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint HookProcedure(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string name);

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        foreach (var (server, run) in _servers)
        {
            try { await run; } catch (OperationCanceledException) { }
            await server.DisposeAsync();
        }
        foreach (nint module in _modules) NativeLibrary.Free(module);
        _stop.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // A copy of the built DLL in a folder of its own, named `instance` so it
    // gets a pipe of its own, and the server of that pipe with what it hears.
    private (string Dll, List<NativeMessage> Heard) Copy(string instance)
    {
        string folder = Path.Combine(_root, instance);
        Directory.CreateDirectory(folder);
        string dll = Path.Combine(folder, VersionedNativeCopy.DllName);
        File.Copy(Path.Combine(AppContext.BaseDirectory, VersionedNativeCopy.DllName), dll);
        VersionedNativeCopy.WriteInstance(dll, instance);

        // The DLL names its pipe after the process it runs in (util.cpp PipeName).
        string image = Path.GetFileNameWithoutExtension(Environment.ProcessPath)!;
        var target = new TaskbarTarget(TaskbarTargetKind.StartMenu, image, Packaged: false);
        var server = new TaskbarPipeServer(TaskbarPipeServer.NameFor(target, instance));
        var heard = new List<NativeMessage>();
        server.MessageReceived += message => { lock (heard) heard.Add(message); };
        _servers.Add((server, Task.Run(() => server.RunAsync(_stop.Token))));
        return (dll, heard);
    }

    // What explorer does when Deckle's hook fires: runs the procedure on the
    // registered message.
    private void Inject(string dll)
    {
        nint module = NativeLibrary.Load(dll);
        _modules.Add(module);
        var procedure = Marshal.GetDelegateForFunctionPointer<HookProcedure>(
            NativeLibrary.GetExport(module, "DeckleTaskbarHookProc"));

        var data = new CallWindowProcData { Message = RegisterWindowMessage(TaskbarHookInjector.InjectMessageName) };
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<CallWindowProcData>());
        try
        {
            Marshal.StructureToPtr(data, pointer, false);
            procedure(HcAction, 0, pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static async Task WaitForAsync(List<NativeMessage> heard, Func<NativeMessage, bool> match)
    {
        var deadline = DateTime.UtcNow + _wait;
        while (DateTime.UtcNow < deadline)
        {
            lock (heard) if (heard.Any(match)) return;
            await Task.Delay(50);
        }
        Assert.Fail("the expected message never arrived");
    }

    [Fact]
    public async Task ASecondCopyInAProcessWhoseEngineIsServingLeavesWithoutStartingAnEngine()
    {
        var first = Copy("first");
        var second = Copy("second");

        Inject(first.Dll);
        await WaitForAsync(first.Heard, m => m is NativeHello);

        Inject(second.Dll);
        await WaitForAsync(second.Heard, m => m is NativeLogRecord { Event: "engine_busy" });
        await Task.Delay(500);   // anything the copy would say after its refusal

        lock (second.Heard)
        {
            Assert.DoesNotContain(second.Heard, m => m is NativeHello);
            Assert.DoesNotContain(second.Heard, m => m is NativeLogRecord { Event: "tap_connection" or "tap_failed" or "loaded" });
        }

        // The first copy is still the process's engine: it ends its session on
        // its own word.
        await _servers[0].Server.SendAsync(NativeCommand.Stop);
        await WaitForAsync(first.Heard, m => m is NativeLogRecord { Event: "unloading" or "parked" });
    }
}
