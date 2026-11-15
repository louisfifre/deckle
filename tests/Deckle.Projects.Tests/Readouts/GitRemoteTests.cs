using Xunit;

namespace Deckle.Projects.Tests;

[Trait("Category", "unit")]
public sealed class GitRemoteTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo.git", "GitHub", "https://github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo", "GitHub", "https://github.com/owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "GitHub", "https://github.com/owner/repo")]
    [InlineData("ssh://git@github.com/owner/repo.git", "GitHub", "https://github.com/owner/repo")]
    [InlineData("ssh://git@gitlab.com:2222/group/sub/repo.git", "GitLab", "https://gitlab.com/group/sub/repo")]
    [InlineData("https://codeberg.org/owner/repo.git", "Codeberg", "https://codeberg.org/owner/repo")]
    [InlineData("git@bitbucket.org:owner/repo.git", "Bitbucket", "https://bitbucket.org/owner/repo")]
    [InlineData("https://www.github.com/owner/repo/", "GitHub", "https://www.github.com/owner/repo")]
    [InlineData("https://git.example.org/team/tool.git", "git.example.org", "https://git.example.org/team/tool")]
    [InlineData("myserver:owner/repo.git", "myserver", "https://myserver/owner/repo")]
    public void EachUrlShapeGivesTheHostsNameAndAnHttpsPage(string url, string hostName, string page)
    {
        GitRemote? remote = GitRemote.Parse(url);

        Assert.NotNull(remote);
        Assert.Equal(hostName, remote.HostName);
        Assert.Equal(page, remote.WebPage.ToString());
        Assert.Equal("https", remote.WebPage.Scheme);
    }

    [Fact]
    public void ACredentialWrittenInTheAddressNeverReachesThePage()
    {
        GitRemote remote = GitRemote.Parse("https://someone:s3cret-token@github.com/owner/repo.git")!;

        Assert.DoesNotContain("s3cret", remote.WebPage.ToString());
        Assert.DoesNotContain("someone", remote.WebPage.ToString());
        Assert.Equal("https://github.com/owner/repo", remote.WebPage.ToString());
    }

    [Theory]
    [InlineData("http://github.com/owner/repo.git")]
    [InlineData("git://github.com/owner/repo.git")]
    [InlineData("file:///d:/repos/repo.git")]
    [InlineData(@"D:\repos\repo.git")]
    [InlineData("C:/repos/repo.git")]
    [InlineData("../sibling/repo")]
    [InlineData("https://github.com/only-owner")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AShapeThatGivesNoWebPageGivesNoRemote(string? url)
    {
        Assert.Null(GitRemote.Parse(url));
    }

    [Fact]
    public void OriginIsPickedElseTheFirstRemote()
    {
        string both = "remote.upstream.url https://github.com/a/b.git\nremote.origin.url https://github.com/c/d.git\n";
        string noOrigin = "remote.backup.url https://example.org/x/y.git\nremote.other.url https://example.org/z/w.git\n";

        Assert.Equal("https://github.com/c/d.git", GitRemote.PickUrl(both));
        Assert.Equal("https://example.org/x/y.git", GitRemote.PickUrl(noOrigin));
    }

    [Fact]
    public void NoRemoteGivesNoUrl()
    {
        Assert.Null(GitRemote.PickUrl(""));
        Assert.Null(GitRemote.PickUrl("core.bare false\n"));
    }

    [Fact]
    public void ARemoteNameWithADotIsKeptWhole()
    {
        Assert.Equal("https://h.io/a/b.git", GitRemote.PickUrl("remote.my.fork.url https://h.io/a/b.git\n"));
    }
}
