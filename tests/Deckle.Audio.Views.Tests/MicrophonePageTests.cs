using System.Xml.Linq;
using Deckle.Catalog;
using Xunit;

namespace Deckle.Audio.Views.Tests;

// The Microphone page, once the Recording page: under the Dictation node (the
// transcription module's id "whisp"), keeping its page type and tag so every
// link to it still works, with the recording length gone to Dictation > General.
[Trait("Category", "unit")]
public class MicrophonePageTests
{
    [Fact]
    public void ThePageNestsUnderTheDictationNodeAndKeepsItsTag()
    {
        SettingsModuleDescriptor page = RecordingSettingsModule.Describe(order: 105);

        Assert.Equal("whisp", page.ParentId);
        Assert.Equal("recording", page.Id);
        Assert.Equal("Deckle.Audio.Views.RecordingPage, Deckle.Audio.Views", page.PageTag);
        Assert.Equal("SettingsModuleNavLabel", page.LabelKey);
        Assert.Equal("Microphone", Resw()["SettingsModuleNavLabel"]);
    }

    [Fact]
    public void TheOldWordRecordingStillFindsTheFirstCard()
    {
        SettingSearchEntry first = SettingsSearch.Entries[0];

        Assert.Equal("GeneralMicrophoneCard", first.LabelKey);
        Assert.Contains("recording", first.Keywords);
    }

    [Fact]
    public void TheRecordingLengthIsNoLongerOnThisPage()
    {
        Assert.DoesNotContain(SettingsSearch.Entries, e => e.LabelKey == "RecordingLimitCard");
        Assert.DoesNotContain("RecordingLimitCard.Header", Resw().Keys);
        Assert.DoesNotContain("RecordingPageRecordingSection.Title", Resw().Keys);
    }

    [Fact]
    public void EveryEntryHasItsWordsInThisAssembliesResw()
    {
        Dictionary<string, string> resw = Resw();
        var missing = SettingsSearch.Entries
            .Select(e => e.LabelKey + ".Header")
            .Where(key => !resw.ContainsKey(key))
            .ToList();

        Assert.True(missing.Count == 0, "Missing from the Audio Views Resources.resw: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheTwoSectionsAreMicrophoneThenVoice()
    {
        Dictionary<string, string> resw = Resw();

        Assert.Equal("Microphone", resw["RecordingPageHeader.Title"]);
        Assert.Equal("Microphone", resw["RecordingPageMicrophoneSection.Title"]);
        Assert.Equal("Voice", resw["RecordingPageVoiceSection.Title"]);
    }

    private static Dictionary<string, string> Resw()
    {
        string path = Path.Combine(RepoRoot(), "src", "Deckle.Audio.Views", "Strings", "en-US", "Resources.resw");
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "", StringComparer.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
