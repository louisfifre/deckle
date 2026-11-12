using System.Runtime.InteropServices;
using Microsoft.Win32;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The XAML composition diagnostics stay off in explorer (native
// composition_diag_guard.h). From 2026-11-09 explorer failed fast in dcomp.dll
// (0xc0000602) dozens of times while it hosted the engine: those diagnostics
// corrupt the heap when two XAML threads add DirectComposition visuals at once.
// Windows.UI.Xaml.dll reads HKLM\Software\Microsoft\XAML\Debug\
// DisableCompositionDiag once, inside the first AdviseVisualTreeChange, and the
// engine answers 1 to that read.
//
// The DLL's test export reads the value the way Windows.UI.Xaml.dll does,
// through Windows.UI.Xaml.dll's own import slots, loaded in this process.
// Whether explorer then really skips the diagnostics is checked live: no
// Windows.UI.Composition element reaches the engine's visual_add records.
[Trait("Category", "regression")]
public sealed class CompositionDiagGuardTests : IDisposable
{
    private const string DebugKey = @"Software\Microsoft\XAML\Debug";
    private const string ValueName = "DisableCompositionDiag";
    private const int Absent = -1;

    private delegate int ReadSwitchFn(int guarded);

    private readonly nint _library;
    private readonly ReadSwitchFn? _read;

    public CompositionDiagGuardTests()
    {
        string path = Path.Combine(AppContext.BaseDirectory, VersionedNativeCopy.DllName);
        if (!File.Exists(path)) return;   // no C++ tools: the build warned with DECKLE001
        _library = NativeLibrary.Load(path);
        _read = Marshal.GetDelegateForFunctionPointer<ReadSwitchFn>(
            NativeLibrary.GetExport(_library, "DeckleTaskbarReadCompositionDiagSwitch"));
    }

    public void Dispose()
    {
        if (_library != 0) NativeLibrary.Free(_library);
    }

    [Fact]
    public void TheAdvisingThreadReadsTheCompositionDiagnosticsAsDisabled()
    {
        if (_read is null) return;
        Assert.Equal(1, _read(1));
    }

    [Fact]
    public void EveryOtherReadSeesTheRegistryAsItIs()
    {
        if (_read is null) return;
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(DebugKey);
        int expected = key?.GetValue(ValueName) is int value ? value : Absent;
        Assert.Equal(expected, _read(0));
    }
}
