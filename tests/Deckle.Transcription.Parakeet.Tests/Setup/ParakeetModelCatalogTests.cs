using Deckle.Core;
using Xunit;

namespace Deckle.Transcription.Parakeet.Tests.Setup;

// What the install downloads: the pinned file set. None of these tests reaches
// the network or the real model.
[Trait("Category", "unit")]
public sealed class ParakeetModelCatalogTests
{
    [Fact]
    public void The_catalog_names_exactly_the_files_the_completeness_check_requires()
    {
        const string root = @"C:\models\parakeet";

        var fromCatalog = ParakeetModelCatalog.Files.Select(file => file.LocalPath(root)).Order().ToList();
        var required = new ParakeetModelFiles(root).RequiredFiles.Order().ToList();

        Assert.Equal(required, fromCatalog);
    }

    [Fact]
    public void Every_url_is_pinned_to_the_revision_and_ends_with_the_file_path()
    {
        foreach (ParakeetModelFile file in ParakeetModelCatalog.Files)
        {
            Assert.Equal(
                $"https://huggingface.co/Olicorne/parakeet-tdt-0.6b-v3-ultra-onnx/resolve/db1fd7a2d89672e8e52383505f0149076faab6f7/{file.Path}",
                file.Url);
        }
    }

    [Fact]
    public void Every_file_carries_a_size_and_a_lowercase_sha256()
    {
        foreach (ParakeetModelFile file in ParakeetModelCatalog.Files.Append(ParakeetModelCatalog.Fp16Encoder))
        {
            Assert.True(file.SizeBytes > 0, file.Path);
            Assert.Matches("^[0-9a-f]{64}$", file.Sha256);
        }
    }

    [Fact]
    public void The_default_download_is_FP32_only_and_leaves_out_what_Deckle_does_not_use()
    {
        var paths = ParakeetModelCatalog.Files.Select(file => file.Path).ToList();

        Assert.Equal(paths.Count, paths.Distinct().Count());
        Assert.DoesNotContain(ParakeetModelCatalog.Fp16Encoder.Path, paths);
        // Deckle ships its own mel graph, and a 4-bit or 8-bit speech model is never a shipping candidate.
        Assert.DoesNotContain(paths, path => path.Contains("nemo128", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.StartsWith("int8/", StringComparison.Ordinal) || path.StartsWith("w4a8/", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.EndsWith(".zst", StringComparison.Ordinal) || path.EndsWith(".nemo", StringComparison.Ordinal));
    }

    [Fact]
    public void The_total_is_the_sum_of_the_files_for_the_wizard_estimate()
    {
        Assert.Equal(2_508_543_343L, ParakeetModelCatalog.TotalBytes);
    }

    [Fact]
    public void The_manual_download_address_is_the_pinned_repository_listing()
    {
        Assert.Equal(
            "https://huggingface.co/Olicorne/parakeet-tdt-0.6b-v3-ultra-onnx/tree/db1fd7a2d89672e8e52383505f0149076faab6f7",
            ParakeetModelCatalog.BrowseUrl);
    }
}

// The provisioning sequence on a temporary folder, with a stand-in for the HTTP download.
[Trait("Category", "integration")]
public sealed class ParakeetModelProvisioningTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"deckle-parakeet-provision-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private static readonly ParakeetModelFile[] Small =
    [
        new("vocab.txt", 10, new string('a', 64)),
        new("fp32/encoder-model.onnx.data.000", 100, new string('b', 64)),
        new("fp32/decoder_joint-model.onnx", 20, new string('c', 64)),
    ];

    private sealed class FakeDownload
    {
        public List<(string Url, string Destination, string? Sha256)> Requests { get; } = [];
        public string? FailOn { get; init; }

        public Task<Downloader.DownloadResult> RunAsync(
            string url, string destination, string? sha256,
            IProgress<Downloader.DownloadProgress>? progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add((url, destination, sha256));
            if (FailOn is not null && url.EndsWith(FailOn, StringComparison.Ordinal))
                return Task.FromResult(Downloader.DownloadResult.Fail("checksum mismatch"));

            ParakeetModelFile file = Small.Single(f => url.EndsWith(f.Path, StringComparison.Ordinal));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, new byte[file.SizeBytes]);
            progress?.Report(new Downloader.DownloadProgress(file.SizeBytes / 2, file.SizeBytes));
            progress?.Report(new Downloader.DownloadProgress(file.SizeBytes, file.SizeBytes));
            return Task.FromResult(Downloader.DownloadResult.Ok(sha256 ?? ""));
        }
    }

    private sealed class RecordingProgress : IProgress<Downloader.DownloadProgress>
    {
        public List<Downloader.DownloadProgress> Reports { get; } = [];
        public void Report(Downloader.DownloadProgress value) => Reports.Add(value);
    }

    [Fact]
    public async Task Every_file_is_requested_with_its_pinned_hash_into_the_model_folder()
    {
        var download = new FakeDownload();

        ProvisioningResult result = await ParakeetModelCatalog.ProvisionAsync(
            _folder, Small, null, download.RunAsync, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(130, result.Bytes);
        Assert.Equal(Small.Select(f => f.Sha256), download.Requests.Select(r => r.Sha256));
        Assert.Equal(Small.Select(f => f.LocalPath(_folder)), download.Requests.Select(r => r.Destination));
        Assert.Equal(Small.Select(f => f.Url), download.Requests.Select(r => r.Url));
    }

    [Fact]
    public async Task A_file_already_on_disk_at_its_pinned_size_is_not_fetched_again()
    {
        string present = Small[1].LocalPath(_folder);
        Directory.CreateDirectory(Path.GetDirectoryName(present)!);
        File.WriteAllBytes(present, new byte[Small[1].SizeBytes]);
        var download = new FakeDownload();

        ProvisioningResult result = await ParakeetModelCatalog.ProvisionAsync(
            _folder, Small, null, download.RunAsync, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(130, result.Bytes);
        Assert.Equal(2, download.Requests.Count);
        Assert.DoesNotContain(download.Requests, r => r.Destination == present);
    }

    [Fact]
    public async Task A_file_of_the_wrong_size_is_fetched_again()
    {
        string truncated = Small[1].LocalPath(_folder);
        Directory.CreateDirectory(Path.GetDirectoryName(truncated)!);
        File.WriteAllBytes(truncated, new byte[7]);
        var download = new FakeDownload();

        await ParakeetModelCatalog.ProvisionAsync(_folder, Small, null, download.RunAsync, CancellationToken.None);

        Assert.Contains(download.Requests, r => r.Destination == truncated);
        Assert.Equal(100, new FileInfo(truncated).Length);
    }

    [Fact]
    public async Task A_failed_file_stops_the_run_and_names_the_file()
    {
        var download = new FakeDownload { FailOn = "fp32/encoder-model.onnx.data.000" };

        ProvisioningResult result = await ParakeetModelCatalog.ProvisionAsync(
            _folder, Small, null, download.RunAsync, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("fp32/encoder-model.onnx.data.000", result.ErrorMessage);
        Assert.Contains("checksum mismatch", result.ErrorMessage);
        Assert.Equal(2, download.Requests.Count);
    }

    [Fact]
    public async Task Progress_covers_the_whole_set_not_one_file()
    {
        var progress = new RecordingProgress();

        await ParakeetModelCatalog.ProvisionAsync(
            _folder, Small, progress, new FakeDownload().RunAsync, CancellationToken.None);

        Assert.All(progress.Reports, report => Assert.Equal(130, report.TotalBytes));
        Assert.Equal(130, progress.Reports[^1].BytesDownloaded);
        // Second file, halfway: the first file's 10 bytes plus half of 100.
        Assert.Contains(progress.Reports, report => report.BytesDownloaded == 60);
        Assert.Equal(
            progress.Reports.Select(r => r.BytesDownloaded).Order(),
            progress.Reports.Select(r => r.BytesDownloaded));
    }

    [Fact]
    public async Task Cancellation_reaches_the_caller()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ParakeetModelCatalog.ProvisionAsync(
            _folder, Small, null, new FakeDownload().RunAsync, cts.Token));
    }
}
