using System.Runtime.InteropServices;
using Deckle.Transcription.Whisper;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

[Trait("Category", "component")]
public sealed class NativeRuntimeTests : IDisposable
{
    private readonly string _nativeDirectory = Path.Combine(
        Path.GetTempPath(), $"deckle-native-{Guid.NewGuid():N}");

    [Fact]
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

    [Fact]
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
