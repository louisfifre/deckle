using System.Xml.Linq;
using Xunit;

namespace Deckle.Audio.Tests;

// Why this test exists. On 2026-11-07 the app had no microphone at all: every
// probe failed with PortAudio error -10000 because portaudio.dll was not beside
// Deckle.exe. Deckle.Audio declared it as Content, and with EnableMsixTooling a
// library's Content lands in the app under a folder named after the library
// (Deckle.Audio\portaudio.dll); the P/Invoke loader is held to the assembly
// directory and System32 (PortAudioInterop.cs) and never looks there. Test
// projects received the DLL at their own root, so every suite stayed green.
//
// A test cannot see the app's output layout without building the app, so it
// holds the declaration that produces the right layout: a None item, copied to
// the output and to a publish, linked at the root.
[Trait("Category", "regression")]
public sealed class PortAudioDeploymentTests
{
    [Fact]
    public void ThePortAudioLibraryIsDeclaredSoItLandsBesideTheApp()
    {
        XDocument project = XDocument.Load(Path.Combine(RepoRoot(), "src", "Deckle.Audio", "Deckle.Audio.csproj"));

        XElement? item = project.Descendants()
            .FirstOrDefault(e => (string?)e.Attribute("Include") == @"Native\win-x64\portaudio.dll");

        Assert.NotNull(item);
        Assert.Equal("None", item!.Name.LocalName);
        Assert.Equal("portaudio.dll", (string?)item.Attribute("Link"));
        Assert.NotNull(item.Attribute("CopyToOutputDirectory"));
        Assert.NotNull(item.Attribute("CopyToPublishDirectory"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
