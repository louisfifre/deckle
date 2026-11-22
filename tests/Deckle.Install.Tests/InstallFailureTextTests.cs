using System.ComponentModel;
using System.Net;
using System.Net.Http;
using Deckle.Install;
using Xunit;

namespace Deckle.Install.Tests;

[Trait("Category", "unit")]
public sealed class InstallFailureTextTests
{
    [Theory]
    [InlineData(1260)]
    [InlineData(225)]
    public void BlockedWizardLaunchNamesSmartScreenAndAntivirus(int nativeError)
    {
        string text = InstallFailureText.For(new Win32Exception(nativeError));

        Assert.Contains("blocked", text);
        Assert.Contains("SmartScreen", text);
    }

    [Fact]
    public void OtherWin32ErrorKeepsTheGenericText()
    {
        string text = InstallFailureText.For(new Win32Exception(5));

        Assert.StartsWith("Deckle Setup could not complete", text);
    }

    [Fact]
    public void NotFoundMeansNoPublishedRelease()
    {
        string text = InstallFailureText.For(new HttpRequestException("x", null, HttpStatusCode.NotFound));

        Assert.Contains("No published Deckle release", text);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void ForbiddenMeansGitHubRefused(HttpStatusCode status)
    {
        string text = InstallFailureText.For(new HttpRequestException("x", null, status));

        Assert.Contains("GitHub refused", text);
    }

    [Fact]
    public void NetworkFailureStillSaysConnection()
    {
        string text = InstallFailureText.For(new HttpRequestException("x"));

        Assert.Contains("internet connection", text);
    }
}
