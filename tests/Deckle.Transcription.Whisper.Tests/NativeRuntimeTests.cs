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

    // What a 0.71 install leaves in the native folder: bundle 1.9.1's MinGW
    // build. ggml loads every ggml-*.dll of the folder as a backend, so a
    // stale backend name is part of the fixture.
    private static readonly string[] OlderBundleDlls =
    [
        "libwhisper.dll",
        "libgcc_s_seh-1.dll",
        "libstdc++-6.dll",
        "libwinpthread-1.dll",
        "ggml-cuda.dll",
    ];

    // Regression: 0.72.0 extracted 1.9.2 beside 1.9.1's files and removed
    // none of them.
    [Fact]
    [Trait("Category", "regression")]
    public async Task InstallingFromTheBundleLeavesTheCatalogAlone()
    {
        Directory.CreateDirectory(_nativeDirectory);
        WriteFakes(_nativeDirectory, OlderBundleDlls);
        File.WriteAllBytes(Path.Combine(_nativeDirectory, "_bundle.part.txt"), [0]);

        string zipPath = Path.Combine(_nativeDirectory + "-zip", "bundle.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        try
        {
            using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                foreach (string name in NativeRuntime.RequiredDllNames)
                    using (Stream s = zip.CreateEntry(name).Open()) s.WriteByte(0);

            int extracted = await NativeRuntime.InstallFromZipAsync(zipPath, _nativeDirectory, CancellationToken.None);

            Assert.Equal(NativeRuntime.RequiredDllNames.Count, extracted);
            AssertCatalogAlone();
            // Only DLLs are the catalog's business; the download's own zip
            // sits in this folder while it extracts.
            Assert.True(File.Exists(Path.Combine(_nativeDirectory, "_bundle.part.txt")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(zipPath)!, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "regression")]
    public void CopyingFromAFolderLeavesTheCatalogAlone()
    {
        string source = _nativeDirectory + "-source";
        Directory.CreateDirectory(_nativeDirectory);
        WriteFakes(_nativeDirectory, OlderBundleDlls);
        try
        {
            // A build folder holds more than the catalog: none of it is copied.
            WriteFakes(source, NativeRuntime.RequiredDllNames);
            WriteFakes(source, ["whisper-cli-helper.dll"]);

            NativeRuntime.CopyFromFolder(source, _nativeDirectory);

            AssertCatalogAlone();
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    // The 8.3 rule makes "*.dll" match "x.dllx": only true .dll names are
    // the catalog's business.
    [Fact]
    [Trait("Category", "unit")]
    public void StaleNamesAreTheDllsTheCatalogDoesNotName()
    {
        Directory.CreateDirectory(_nativeDirectory);
        WriteFakes(_nativeDirectory, ["whisper.dll", "LibWhisper.DLL", "notes.dllx", "whisper.dll.partial"]);

        Assert.Equal(["LibWhisper.DLL"], NativeRuntime.GetStale(_nativeDirectory));
        Assert.Empty(NativeRuntime.GetStale(_nativeDirectory + "-absent"));
    }

    private void AssertCatalogAlone()
    {
        Assert.Empty(NativeRuntime.GetStale(_nativeDirectory));
        Assert.True(NativeRuntime.IsInstalled(_nativeDirectory));
        foreach (string name in OlderBundleDlls)
            Assert.False(File.Exists(Path.Combine(_nativeDirectory, name)), name);
    }

    private static void WriteFakes(string directory, IEnumerable<string> names)
    {
        Directory.CreateDirectory(directory);
        foreach (string name in names)
            File.WriteAllBytes(Path.Combine(directory, name), [0]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_nativeDirectory, recursive: true); }
        catch { }
    }
}
