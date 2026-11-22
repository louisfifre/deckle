using System.Net;
using System.Net.Http;
using Xunit;

namespace Deckle.Core.Tests;

// What a person gets when a download goes quiet or the link drops: a readable
// failure and no half file, never a hang.
[Trait("Category", "regression")]
public sealed class DownloaderStallTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "deckle-dl-" + Guid.NewGuid().ToString("N"));

    public DownloaderStallTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(ct);
    }

    // A body that sends one chunk then never answers until cancelled.
    private sealed class SilentStream : Stream
    {
        private bool _sent;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!_sent) { _sent = true; buffer.Span[0] = 1; return 1; }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ASilentLinkEndsWithAReadableStallMessageAndNoPartialFile()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SilentStream()) })));
        string dest = Path.Combine(_dir, "model.bin");

        var result = await Downloader.DownloadAsync(
            http, TimeSpan.FromMilliseconds(200), "http://example.test/m", dest, null, null, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("stalled", result.ErrorMessage);
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".partial"));
    }

    [Fact]
    public async Task ACancelByThePersonStillThrowsInsteadOfReportingAStall()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SilentStream()) })));
        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Downloader.DownloadAsync(
            http, TimeSpan.FromSeconds(30), "http://example.test/m", Path.Combine(_dir, "m.bin"), null, null, cts.Token));
    }

    [Fact]
    public async Task ADroppedConnectionIsReportedInPlainWords()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            throw new HttpRequestException("x", new IOException("The remote party closed the transport stream."))));

        var result = await Downloader.DownloadAsync(
            http, TimeSpan.FromSeconds(5), "http://example.test/m", Path.Combine(_dir, "m.bin"), null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.StartsWith("The connection failed", result.ErrorMessage);
        Assert.DoesNotContain("HttpRequestException", result.ErrorMessage);
    }

    [Fact]
    public async Task AServerErrorNamesTheStatusCode()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));

        var result = await Downloader.DownloadAsync(
            http, TimeSpan.FromSeconds(5), "http://example.test/m", Path.Combine(_dir, "m.bin"), null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("404", result.ErrorMessage);
    }
}
