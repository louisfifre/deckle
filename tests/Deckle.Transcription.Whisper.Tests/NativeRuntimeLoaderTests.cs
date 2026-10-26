using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

// The loader is exercised on a native folder of fixture DLLs named after the
// real catalog: Windows resolves their imports for real, with the search the
// loader imposes (the folder and System32, never PATH).
[Collection(OperationalObservabilityCollection.Name)]
public sealed class NativeRuntimeLoaderTests : IDisposable
{
    private readonly string _nativeDirectory = Path.Combine(
        Path.GetTempPath(), $"deckle-native-load-{Guid.NewGuid():N}");

    public NativeRuntimeLoaderTests()
    {
        Directory.CreateDirectory(_nativeDirectory);
    }

    // Every catalog DLL imports only the one before it and Windows: the
    // folder is a complete, loadable runtime.
    private void WriteLoadableCatalog()
    {
        string? previous = null;
        foreach (string name in NativeRuntime.RequiredDllNames)
        {
            var imports = new List<string> { "KERNEL32.dll" };
            if (previous is not null && !NativeRuntime.IsBackendDll(previous)) imports.Add(previous);
            TestPeImage.Write(Path.Combine(_nativeDirectory, name), imports);
            previous = name;
        }
    }

    [Fact]
    [Trait("Category", "integration")]
    public void ACompleteCatalogLoadsWithVulkan()
    {
        WriteLoadableCatalog();

        NativeRuntimeLoadCheck check = NativeRuntime.CheckLoad(_nativeDirectory);

        Assert.True(check.Loads);
        Assert.True(check.VulkanAvailable);
        Assert.Null(check.FirstFailure);
        Assert.Equal(NativeRuntime.RequiredDllNames, check.Dlls.Select(d => d.Name));
        Assert.All(check.Dlls, d => Assert.True(d.Loaded));
    }

    // A machine without a Vulkan loader: the Vulkan backend does not load,
    // the runtime still does, on the CPU.
    [Fact]
    [Trait("Category", "integration")]
    public void AMissingVulkanLoaderLeavesTheRuntimeLoadableOnTheCpu()
    {
        WriteLoadableCatalog();
        TestPeImage.Write(Path.Combine(_nativeDirectory, "ggml-vulkan.dll"), ["ggml-base.dll", "deckle-absent-vulkan-1.dll"]);

        NativeRuntimeLoadCheck check = NativeRuntime.CheckLoad(_nativeDirectory);

        Assert.True(check.Loads);
        Assert.False(check.VulkanAvailable);
        Assert.Null(check.FirstFailure);
        NativeDllLoadResult vulkan = check.Dlls.Single(d => NativeRuntime.IsVulkanBackend(d.Name));
        Assert.Equal(NativeRuntimeLoader.ErrorModNotFound, vulkan.Win32Error);
        Assert.Equal("deckle-absent-vulkan-1.dll", vulkan.MissingImport);
    }

    // The bug the first outside tester hit: a catalog DLL importing the
    // compiler's runtime, which the build machine found on PATH and a bare
    // machine did not. The check names the DLL and the import, with the
    // search PATH excluded, on any machine.
    [Fact]
    [Trait("Category", "regression")]
    public void ADllImportingARuntimeTheBundleDoesNotShipIsNamedWithItsImport()
    {
        WriteLoadableCatalog();
        TestPeImage.Write(Path.Combine(_nativeDirectory, "ggml.dll"), ["ggml-base.dll", "libgomp-1.dll", "KERNEL32.dll"]);

        NativeRuntimeLoadCheck check = NativeRuntime.CheckLoad(_nativeDirectory);

        Assert.False(check.Loads);
        NativeDllLoadResult failure = Assert.IsType<NativeDllLoadResult>(check.FirstFailure);
        Assert.Equal("ggml.dll", failure.Name);
        Assert.Equal(NativeRuntimeLoader.ErrorModNotFound, failure.Win32Error);
        Assert.Equal("libgomp-1.dll", failure.MissingImport);
        Assert.Equal("ggml.dll imports libgomp-1.dll, which is neither beside it in the native folder nor in System32 (Win32 error 126).", failure.Summary);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void AFileThatIsNotADllIsNamedWithItsError()
    {
        WriteLoadableCatalog();
        File.WriteAllText(Path.Combine(_nativeDirectory, "whisper.dll"), "not a DLL");

        NativeRuntimeLoadCheck check = NativeRuntime.CheckLoad(_nativeDirectory);

        Assert.False(check.Loads);
        Assert.Equal("whisper.dll", check.FirstFailure?.Name);
        Assert.Equal(NativeRuntimeLoader.ErrorBadExeFormat, check.FirstFailure?.Win32Error);
        Assert.Contains("not a 64-bit Windows DLL", check.FirstFailure?.Summary);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void AnAbsentCatalogDllIsNamed()
    {
        WriteLoadableCatalog();
        File.Delete(Path.Combine(_nativeDirectory, "vcomp140.dll"));

        NativeRuntimeLoadCheck check = NativeRuntime.CheckLoad(_nativeDirectory);

        Assert.False(check.Loads);
        Assert.Equal("vcomp140.dll", check.FirstFailure?.Name);
        Assert.Equal("vcomp140.dll is not in the native folder.", check.FirstFailure?.Summary);
    }

    // What the P/Invoke resolver throws carries the same facts, so the
    // exception a caller sees names the file to fix.
    [Fact]
    [Trait("Category", "regression")]
    public void TheResolverExceptionNamesTheDllAndItsMissingImport()
    {
        WriteLoadableCatalog();
        TestPeImage.Write(Path.Combine(_nativeDirectory, "ggml-base.dll"), ["vcomp140.dll", "libgomp-1.dll"]);

        var ex = Assert.Throws<DllNotFoundException>(() => NativeRuntimeLoader.LoadForPInvoke(_nativeDirectory, "whisper.dll"));

        Assert.Contains("ggml-base.dll imports libgomp-1.dll", ex.Message);
        Assert.Contains("Win32 error 126", ex.Message);
    }

    [Fact]
    [Trait("Category", "observability")]
    public void AFailedCheckIsLoggedWithTheDllAndItsImport()
    {
        WriteLoadableCatalog();
        TestPeImage.Write(Path.Combine(_nativeDirectory, "ggml.dll"), ["ggml-base.dll", "libgomp-1.dll"]);
        using var listener = new TestEventListener("Deckle-Whisper");

        NativeRuntime.CheckLoad(_nativeDirectory);

        EventWrittenEventArgs failed = Assert.Single(listener.Events, e => e.EventId == DeckleWhisperSource.EvtNativeRuntimeLoadCheckFailed);
        Assert.Equal(EventLevel.Warning, failed.Level);
        Assert.Equal("ggml.dll", failed.Payload![0]);
        Assert.Equal("libgomp-1.dll", failed.Payload[1]);
        Assert.Equal(NativeRuntimeLoader.ErrorModNotFound, failed.Payload[2]);
        Assert.DoesNotContain(listener.Events, e => e.EventId == DeckleWhisperSource.EvtNativeRuntimeLoadCheckPassed);
    }

    [Fact]
    [Trait("Category", "observability")]
    public void APassedCheckIsLoggedWithTheVulkanAnswer()
    {
        WriteLoadableCatalog();
        TestPeImage.Write(Path.Combine(_nativeDirectory, "ggml-vulkan.dll"), ["ggml-base.dll", "deckle-absent-vulkan-1.dll"]);
        using var listener = new TestEventListener("Deckle-Whisper");

        NativeRuntime.CheckLoad(_nativeDirectory);

        EventWrittenEventArgs passed = Assert.Single(listener.Events, e => e.EventId == DeckleWhisperSource.EvtNativeRuntimeLoadCheckPassed);
        Assert.Equal(EventLevel.Informational, passed.Level);
        Assert.Equal(false, passed.Payload![0]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_nativeDirectory, recursive: true); }
        catch { }
    }
}
