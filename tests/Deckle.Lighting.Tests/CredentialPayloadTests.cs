using Deckle.TestSupport;
using Xunit;

namespace Deckle.Lighting.Tests;

// The Hue application key and the Entertainment client key are credentials, and
// a prefix of either is still part of one. No event of this provider may carry a
// payload named after them, whatever the verbosity. A boolean "_present" flag only
// says whether a credential exists and carries none of it, so it is allowed.
[Trait("Category", "regression")]
public sealed class CredentialPayloadTests
{
    private static readonly string[] CredentialShapedNames =
        ["username", "clientkey", "client_key", "application_key", "app_key"];

    [Fact]
    public void NoHueEventCarriesAPayloadNamedAfterACredential()
    {
        string[] offending = ProviderManifest.PayloadNamesOf(typeof(DeckleLightingSource))
            .Where(name => !name.EndsWith("_present", StringComparison.Ordinal))
            .Where(name => CredentialShapedNames.Any(
                shape => name.Contains(shape, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(offending);
    }
}