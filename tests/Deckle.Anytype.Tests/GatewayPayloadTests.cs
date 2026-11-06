using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "integration")]
public sealed class GatewayPayloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "deckle-gateway-" + Guid.NewGuid().ToString("N"));

    private string Payload => Path.Combine(_root, "payload");
    private string Versions => Path.Combine(_root, "versions");

    public GatewayPayloadTests()
    {
        Directory.CreateDirectory(Payload);
        Write("Deckle.Anytype.Gateway.exe", "exe");
        Write("Deckle.Anytype.dll", "lib");
        Write(Path.Combine("runtimes", "native.dll"), "native");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Fingerprint_is_absent_without_the_gateway_executable()
    {
        File.Delete(Path.Combine(Payload, GatewayContract.ExecutableName));

        Assert.Null(GatewayPayload.Fingerprint(Payload));
    }

    [Fact]
    public void Same_content_gives_the_same_fingerprint_so_a_rebuild_keeps_the_running_gateway()
    {
        string first = GatewayPayload.Fingerprint(Payload)!;
        // A rebuild rewrites the files with the same bytes.
        Write("Deckle.Anytype.dll", "lib");

        Assert.Equal(first, GatewayPayload.Fingerprint(Payload));
    }

    [Fact]
    public void Changed_code_gives_a_new_fingerprint()
    {
        string first = GatewayPayload.Fingerprint(Payload)!;
        Write("Deckle.Anytype.dll", "lib changed");

        Assert.NotEqual(first, GatewayPayload.Fingerprint(Payload));
    }

    [Fact]
    public void Debug_symbols_do_not_change_the_fingerprint()
    {
        string first = GatewayPayload.Fingerprint(Payload)!;
        Write("Deckle.Anytype.pdb", "symbols");

        Assert.Equal(first, GatewayPayload.Fingerprint(Payload));
    }

    [Fact]
    public void Staging_copies_the_payload_once_and_reuses_it()
    {
        string fingerprint = GatewayPayload.Fingerprint(Payload)!;

        string executable = GatewayPayload.Stage(Payload, fingerprint, Versions);
        DateTime firstCopy = File.GetLastWriteTimeUtc(executable);
        string again = GatewayPayload.Stage(Payload, fingerprint, Versions);

        Assert.Equal(Path.Combine(Versions, fingerprint, GatewayContract.ExecutableName), executable);
        Assert.Equal(executable, again);
        Assert.Equal(firstCopy, File.GetLastWriteTimeUtc(again));
        Assert.True(File.Exists(Path.Combine(Versions, fingerprint, "runtimes", "native.dll")));
        Assert.Single(Directory.GetDirectories(Versions));
    }

    [Fact]
    public void A_copy_cut_short_is_staged_again_rather_than_run()
    {
        string fingerprint = GatewayPayload.Fingerprint(Payload)!;
        // A folder without its completion marker: a crash during an earlier copy.
        Directory.CreateDirectory(Path.Combine(Versions, fingerprint));
        File.WriteAllText(Path.Combine(Versions, fingerprint, GatewayContract.ExecutableName), "partial");

        string executable = GatewayPayload.Stage(Payload, fingerprint, Versions);

        Assert.Equal("exe", File.ReadAllText(executable));
        Assert.True(File.Exists(Path.Combine(Versions, fingerprint, "Deckle.Anytype.dll")));
    }

    [Fact]
    public void Pruning_keeps_the_current_build_and_a_version_still_in_use()
    {
        string fingerprint = GatewayPayload.Fingerprint(Payload)!;
        GatewayPayload.Stage(Payload, fingerprint, Versions);
        string stale = Path.Combine(Versions, "stale");
        string inUse = Path.Combine(Versions, "in-use");
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(inUse);
        File.WriteAllText(Path.Combine(stale, "a.dll"), "a");
        File.WriteAllText(Path.Combine(inUse, "b.dll"), "b");

        int removed;
        // An open file stands for a running gateway's image: Windows refuses to
        // rename its folder, so pruning must leave it whole.
        using (File.Open(Path.Combine(inUse, "b.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            removed = GatewayPayload.Prune(Versions, fingerprint);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(stale));
        Assert.True(File.Exists(Path.Combine(inUse, "b.dll")));
        Assert.True(Directory.Exists(Path.Combine(Versions, fingerprint)));
    }

    [Fact]
    public void Only_a_gateway_executable_under_the_versions_folder_is_a_staged_gateway()
    {
        Assert.True(GatewayPayload.IsStagedGateway(
            Path.Combine(Versions, "abc", GatewayContract.ExecutableName), Versions));
        Assert.False(GatewayPayload.IsStagedGateway(
            Path.Combine(_root, "elsewhere", GatewayContract.ExecutableName), Versions));
        Assert.False(GatewayPayload.IsStagedGateway(
            Path.Combine(Versions, "abc", "Deckle.exe"), Versions));
    }

    private void Write(string relative, string content)
    {
        string path = Path.Combine(Payload, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
