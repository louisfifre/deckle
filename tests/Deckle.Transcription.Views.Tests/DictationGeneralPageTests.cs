using System.Xml.Linq;
using Deckle.Catalog;
using Xunit;

namespace Deckle.Transcription.Views.Tests;

// Dictation > General as the search and the page's cards agree on it. A search
// hit lands on the card whose Tag equals the entry's LabelKey, and the composer
// stamps that Tag from the descriptor's LabelKey, so the entries and the
// manifests the page composes must name the same top-level cards. The strings of
// an entry must exist in the resw of the assembly it is registered under.
[Trait("Category", "unit")]
public class DictationGeneralPageTests
{
    // The manifests TranscriptionPage composes in the sections that are always
    // shown, in page order.
    private static IEnumerable<string> ShownCards()
    {
        var vm = new TranscriptionViewModel();
        return vm.StreamingSwitch
            .Concat(vm.BehaviourSettings)
            .Concat(vm.RecordingLimitSettings)
            .Concat(vm.SpeechEngineSettings)
            .Concat(vm.ModelsDirectorySettingsManifest)
            .Concat(vm.UseGpuSettingsManifest)
            .Select(d => d.LabelKey);
    }

    // The Segmentation section: developer mode only, so never searched.
    private static IEnumerable<string> SegmentationCards()
    {
        var vm = new TranscriptionViewModel();
        return vm.SegmentationSettings.Concat(vm.SilenceGuardSettings).Select(d => d.LabelKey);
    }

    [Fact]
    public void EveryShownCardIsFoundByItsLabelKey()
    {
        string[] entries = SettingsSearch.Entries.Select(e => e.LabelKey).ToArray();

        Assert.Equal(ShownCards().OrderBy(k => k, StringComparer.Ordinal), entries.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void TheSegmentationCardsAreInNoSearchList()
    {
        string[] searched = SettingsSearch.Entries
            .Concat(SettingsSearch.LexiconEntries)
            .Select(e => e.LabelKey)
            .ToArray();

        foreach (string card in SegmentationCards())
            Assert.DoesNotContain(card, searched);
        Assert.DoesNotContain("TranscriptionCutCurveCard", searched);
        Assert.DoesNotContain("TranscriptionSilenceGuardsFold", searched);
    }

    [Fact]
    public void TheOldPageNamesStillFindTheirCards()
    {
        SettingSearchEntry streaming = Entry("TranscriptionStreamingEnabledCard");
        SettingSearchEntry limit = Entry("RecordingLimitCard");

        Assert.Contains("transcription", streaming.Keywords);
        Assert.Contains("streaming", streaming.Keywords);
        Assert.Contains("recording", limit.Keywords);
    }

    [Fact]
    public void KeywordsAreLowercase()
    {
        foreach (string keyword in SettingsSearch.Entries.Concat(SettingsSearch.LexiconEntries).SelectMany(e => e.Keywords))
            Assert.Equal(keyword.ToLowerInvariant(), keyword);
    }

    [Fact]
    public void EveryEntryHasItsWordsInThisAssembliesResw()
    {
        Dictionary<string, string> resw = ReadResw();
        var missing = SettingsSearch.Entries.Concat(SettingsSearch.LexiconEntries)
            .Select(e => e.LabelKey + ".Header")
            .Where(key => !resw.ContainsKey(key))
            .ToList();

        Assert.True(missing.Count == 0, "Missing from the Transcription Views Resources.resw: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheNodeIsNamedDictationAndTheGeneralPageSaysWhatItIsFor()
    {
        Dictionary<string, string> resw = ReadResw();

        Assert.Equal("Dictation", resw["SettingsModuleNavLabel"]);
        Assert.Equal("Dictation", resw["TranscriptionPageHeader.Title"]);
        Assert.Equal("How you dictate, and how Deckle transcribes it.", resw["TranscriptionPageHeader.Description"]);
        Assert.Equal("While you speak", resw["TranscriptionPageWhileYouSpeakSection.Title"]);
        Assert.Equal("Segmentation", resw["TranscriptionPageSegmentationSection.Title"]);
        Assert.Equal(
            "Shown in developer mode only.",
            resw["TranscriptionPageSegmentationSection.Caption"]);
    }

    [Fact]
    public void TheNodeKeepsItsModuleIdAndPageTag()
    {
        SettingsModuleDescriptor node = TranscriptionSettingsModule.Describe(order: 1);

        Assert.Equal("whisp", node.Id);
        Assert.Equal("whisp", TranscriptionSettingsModule.ModuleId);
        Assert.Equal("Deckle.Transcription.Views.TranscriptionPage, Deckle.Transcription.Views", node.PageTag);
        Assert.Null(node.ParentId);
    }

    private static SettingSearchEntry Entry(string labelKey) =>
        SettingsSearch.Entries.Single(e => e.LabelKey == labelKey);

    private static Dictionary<string, string> ReadResw()
    {
        string path = Path.Combine(RepoRoot(), "src", "Deckle.Transcription.Views", "Strings", "en-US", "Resources.resw");
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
