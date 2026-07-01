using System.IO;
using System.Text;
using Deckle.Security;
using Xunit;

namespace Deckle.Security.Tests;

// Behavioural tests over SecretVault: the store's promises to its callers —
// round-trip, isolation between named secrets, sealing at rest, and the way it
// distinguishes a missing file (empty) from an unreadable one (anomaly). The
// DPAPI path runs for real (the test process is the same Windows account that
// wrote the file), so the sealing and persistence assertions exercise the
// genuine encrypt/decrypt round-trip, not a stub.
//
// xUnit builds a fresh instance per test, so the constructor + Dispose give
// each test its own throwaway file under the temp tree.
[Trait("Category", "unit")]
public sealed class SecretVaultTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SecretVaultTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "deckle-vault-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "secrets.dat");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private SecretVault NewVault() => new(_path);

    [Fact]
    public void GetReturnsTheValueThatWasSet()
    {
        var vault = NewVault();
        vault.Set("anytype.apiKey", "sk-abc-123");

        Assert.True(vault.TryGet("anytype.apiKey", out string? value));
        Assert.Equal("sk-abc-123", value);
    }

    [Fact]
    public void GetOnAnUnknownNameReportsAbsence()
    {
        var vault = NewVault();

        Assert.False(vault.TryGet("nope", out string? value));
        Assert.Null(value);
    }

    [Fact]
    public void SetOverwritesAnExistingSecret()
    {
        var vault = NewVault();
        vault.Set("token", "first");
        vault.Set("token", "second");

        Assert.True(vault.TryGet("token", out string? value));
        Assert.Equal("second", value);
    }

    [Fact]
    public void DistinctSecretsDoNotClobberEachOther()
    {
        // The whole vault is one file; a naive whole-file write could drop keys.
        var vault = NewVault();
        vault.Set("a", "1");
        vault.Set("b", "2");
        vault.Set("c", "3");

        Assert.True(vault.TryGet("a", out string? a));
        Assert.True(vault.TryGet("b", out string? b));
        Assert.True(vault.TryGet("c", out string? c));
        Assert.Equal("1", a);
        Assert.Equal("2", b);
        Assert.Equal("3", c);
    }

    [Fact]
    public void ContainsTracksPresence()
    {
        var vault = NewVault();
        Assert.False(vault.Contains("k"));

        vault.Set("k", "v");
        Assert.True(vault.Contains("k"));
    }

    [Fact]
    public void RemoveDeletesAndReportsWhetherSomethingWasRemoved()
    {
        var vault = NewVault();
        vault.Set("k", "v");

        Assert.True(vault.Remove("k"));
        Assert.False(vault.Contains("k"));
        Assert.False(vault.Remove("k")); // already gone
    }

    [Fact]
    public void SecretsSurviveAcrossVaultInstances()
    {
        // A fresh handle over the same file reads back what a prior one wrote —
        // the disk + DPAPI round-trip, no shared in-memory state.
        NewVault().Set("persisted", "value");

        Assert.True(NewVault().TryGet("persisted", out string? value));
        Assert.Equal("value", value);
    }

    [Fact]
    public void TheFileOnDiskIsSealedNotPlaintext()
    {
        const string secret = "top-secret-plaintext-marker";
        NewVault().Set("k", secret);

        byte[] raw = File.ReadAllBytes(_path);
        string asText = Encoding.UTF8.GetString(raw);

        Assert.DoesNotContain(secret, asText);
    }

    [Fact]
    public void AMissingFileBehavesAsAnEmptyVault()
    {
        // No file written yet: reads report absence, no exception.
        var vault = NewVault();

        Assert.False(File.Exists(_path));
        Assert.False(vault.TryGet("k", out _));
        Assert.False(vault.Contains("k"));
    }

    [Fact]
    public void AnUnreadableFileThrowsRatherThanStartingEmpty()
    {
        // A present-but-undecryptable file (garbage bytes) must surface as an
        // anomaly, not be silently treated as an empty vault.
        File.WriteAllBytes(_path, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });
        var vault = NewVault();

        Assert.Throws<SecretVaultException>(() => vault.TryGet("k", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankNameIsRejected(string? name)
    {
        var vault = NewVault();

        // null → ArgumentNullException, blank → ArgumentException; both derive
        // from ArgumentException, so accept either.
        Assert.ThrowsAny<ArgumentException>(() => vault.Set(name!, "v"));
    }
}
