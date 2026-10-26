using System.Runtime.InteropServices;
using Deckle.Transcription.Whisper;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

public sealed class NativeRuntimeTests : IDisposable
{
    private readonly string _nativeDirectory = Path.Combine(
        Path.GetTempPath(), $"deckle-native-{Guid.NewGuid():N}");

    [Fact]
    [Trait("Category", "integration")]
    public void CompleteCatalogIsRequiredForAnInstalledRuntime()
    {
        Directory.CreateDirectory(_nativeDirectory);
        foreach (string name in NativeRuntime.RequiredDllNames)
            File.WriteAllBytes(Path.Combine(_nativeDirectory, name), [0]);

        Assert.True(NativeRuntime.IsInstalled(_nativeDirectory));

        foreach (string missingName in NativeRuntime.RequiredDllNames)
        {
            string path = Path.Combine(_nativeDirectory, missingName);
            File.Delete(path);

            Assert.False(NativeRuntime.IsInstalled(_nativeDirectory));
            Assert.Equal([missingName], NativeRuntime.GetMissing(_nativeDirectory));

            File.WriteAllBytes(path, [0]);
        }
    }

    // The catalog is in load order, so the loader can walk it top to bottom:
    // the C++ runtime first, the ggml core, the backends ggml loads itself,
    // and the entry DLL last.
    [Fact]
    [Trait("Category", "unit")]
    public void TheCatalogListsTheRuntimeFirstAndTheEntryDllLast()
    {
        List<string> names = NativeRuntime.RequiredDllNames.ToList();

        Assert.Equal(NativeRuntime.EntryDll, names[^1]);
        Assert.Equal("whisper.dll", NativeRuntime.EntryDll);
        Assert.True(names.IndexOf("vcomp140.dll") < names.IndexOf("ggml-base.dll"));
        Assert.True(names.IndexOf("ggml-base.dll") < names.IndexOf("ggml.dll"));
        Assert.True(names.IndexOf("ggml.dll") < names.IndexOf("ggml-cpu-x64.dll"));
        Assert.Equal(names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), names.Count);

        string[] backends = names.Where(NativeRuntime.IsBackendDll).ToArray();
        Assert.All(backends, b => Assert.True(b.StartsWith("ggml-cpu-", StringComparison.Ordinal) || NativeRuntime.IsVulkanBackend(b)));
        Assert.Contains("ggml-vulkan.dll", backends);
        Assert.Contains("ggml-cpu-x64.dll", backends);
        Assert.DoesNotContain("ggml-base.dll", backends);
        Assert.DoesNotContain("ggml.dll", backends);
        Assert.DoesNotContain(NativeRuntime.EntryDll, backends);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void WhisperV191InteropLayoutsMatchTheNativeAbi()
    {
        Assert.Equal(48, Marshal.SizeOf<WhisperContextParams>());
        Assert.Equal(24, Marshal.OffsetOf<WhisperContextParams>(nameof(WhisperContextParams.dtw_aheads_n_heads)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<WhisperContextParams>(nameof(WhisperContextParams.dtw_mem_size)).ToInt32());

        Assert.Equal(304, Marshal.SizeOf<WhisperFullParams>());
        Assert.Equal(64, Marshal.OffsetOf<WhisperFullParams>(nameof(WhisperFullParams.suppress_regex)).ToInt32());
        Assert.Equal(160, Marshal.OffsetOf<WhisperFullParams>(nameof(WhisperFullParams.new_segment_callback)).ToInt32());
        Assert.Equal(240, Marshal.OffsetOf<WhisperFullParams>(nameof(WhisperFullParams.grammar_rules)).ToInt32());
        Assert.Equal(272, Marshal.OffsetOf<WhisperFullParams>(nameof(WhisperFullParams.vad_model_path)).ToInt32());
    }

    public void Dispose()
    {
        try { Directory.Delete(_nativeDirectory, recursive: true); }
        catch { }
    }
}
