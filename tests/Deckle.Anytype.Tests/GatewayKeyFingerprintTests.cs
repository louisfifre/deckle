using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "unit")]
public sealed class GatewayKeyFingerprintTests
{
    private const string Key = "a-long-random-api-key-0123456789";

    [Fact]
    public void The_same_key_always_gives_the_same_fingerprint()
        => Assert.Equal(GatewayKeyFingerprint.Of(Key), GatewayKeyFingerprint.Of(Key));

    [Fact]
    public void Two_keys_give_two_fingerprints()
        => Assert.NotEqual(GatewayKeyFingerprint.Of(Key), GatewayKeyFingerprint.Of(Key + "x"));

    [Fact]
    public void The_fingerprint_is_sixteen_hex_characters_and_holds_no_part_of_the_key()
    {
        string fingerprint = GatewayKeyFingerprint.Of(Key);

        Assert.Matches("^[0-9a-f]{16}$", fingerprint);
        Assert.DoesNotContain(Key, fingerprint, StringComparison.Ordinal);
    }

    private static AnytypeCredentials Credentials(string space) =>
        new("http://localhost:31009", "2025-11-08", Key, space);

    [Fact]
    public void The_same_key_on_another_space_gives_another_fingerprint()
        => Assert.NotEqual(
            GatewayKeyFingerprint.Of(Credentials("space-one")),
            GatewayKeyFingerprint.Of(Credentials("space-two")));

    [Fact]
    public void The_same_credentials_always_give_the_same_fingerprint()
        => Assert.Equal(
            GatewayKeyFingerprint.Of(Credentials("space-one")),
            GatewayKeyFingerprint.Of(Credentials("space-one")));

    [Fact]
    public void An_empty_key_has_no_fingerprint()
        => Assert.Throws<ArgumentException>(() => GatewayKeyFingerprint.Of(""));
}