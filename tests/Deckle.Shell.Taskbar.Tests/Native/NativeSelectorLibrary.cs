using System.Runtime.InteropServices;
using System.Text;

namespace Deckle.Shell.Taskbar.Tests;

// Loads the DLL the module build produced and calls its selector parser
// (xamldiag/rules/selector.cpp), the code explorer runs. Null when the build had
// no C++ tools and so no DLL (the build warned with DECKLE001): the tests that
// use it then check nothing, as they say.
internal sealed class NativeSelectorLibrary : IDisposable
{
    private delegate int DescribeSelectorFn(
        [MarshalAs(UnmanagedType.LPWStr)] string text, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder output, int capacity);

    private readonly IntPtr _library;
    private readonly DescribeSelectorFn? _describe;

    public NativeSelectorLibrary()
    {
        string path = Path.Combine(AppContext.BaseDirectory, VersionedNativeCopy.DllName);
        if (!File.Exists(path)) return;

        _library = NativeLibrary.Load(path);
        _describe = Marshal.GetDelegateForFunctionPointer<DescribeSelectorFn>(
            NativeLibrary.GetExport(_library, "DeckleTaskbarDescribeSelector"));
    }

    public bool IsAvailable => _describe is not null;

    /// <summary>The canonical text on success, "error: ..." on failure; null when there is no DLL.</summary>
    public string? Describe(string selector)
    {
        if (_describe is null) return null;
        var output = new StringBuilder(2048);
        int length = _describe(selector, output, output.Capacity);
        return length >= 0 ? output.ToString() : "error: " + output;
    }

    public void Dispose()
    {
        if (_library != IntPtr.Zero) NativeLibrary.Free(_library);
    }
}
