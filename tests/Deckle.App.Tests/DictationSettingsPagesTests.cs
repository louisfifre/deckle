using System.Xml.Linq;
using Deckle.Catalog;
using Deckle.Settings;
using Xunit;

namespace Deckle.App.Tests;

// The Dictation node of the settings rail as the resident app registers it:
// General, Microphone, Lexicon, with no Streaming page, and a search that finds
// the moved cards on the page that now hosts them. The registration is the
// app's own (DictationSettingsPages), run here against the real registry and the
// real index.
//
// The index resolves each entry's words from the process's PRI, which a test
// host does not have, so the entries are re-registered with their label read
// from the owning assembly's resw: the ranking and the page coordinates are the
// index's own, the words are the ones the person reads.
[Trait("Category", "unit")]
[Collection("SettingsRegistry")]
public sealed class DictationSettingsPagesTests : IDisposable
{
    private static readonly string[] DictationIds = ["whisp", "recording", "whisp-lexicon"];

    private const string GeneralTag = "Deckle.Transcription.Views.TranscriptionPage, Deckle.Transcription.Views";
    private const string MicrophoneTag = "Deckle.Audio.Views.RecordingPage, Deckle.Audio.Views";

    private readonly List<SettingsModuleDescriptor> _registered = [];

    public DictationSettingsPagesTests()
    {
        DictationSettingsPages.Register((page, entries) =>
        {
            _registered.Add(page);
            SettingsModuleRegistry.Register(page);
            SettingsSearchIndex.RegisterPage(
                page.PageTag, page.OwningAssembly, page.Glyph, Label(page.OwningAssembly, page.LabelKey),
                [.. entries.Select(e => e with { LiteralLabel = Label(page.OwningAssembly, e.LabelKey + ".Header") })]);
        });
    }

    public void Dispose()
    {
        foreach (string id in DictationIds) SettingsModuleRegistry.Unregister(id);
    }

    [Fact]
    public void TheRailUnderDictationReadsGeneralMicrophoneLexiconWithNoStreaming()
    {
        List<SettingsModuleDescriptor> rail = SettingsModuleRegistry.Modules
            .Where(m => m.Id == "whisp" || m.ParentId == "whisp")
            .ToList();

        Assert.Equal(["whisp", "recording", "whisp-lexicon"], rail.Select(m => m.Id));
        Assert.Equal(["Dictation", "Microphone", "Lexicon"], rail.Select(m => Label(m.OwningAssembly, m.LabelKey)));
        Assert.DoesNotContain(SettingsModuleRegistry.Modules, m => m.Id == "whisp-streaming" || m.PageTag.Contains("StreamingPage"));
    }

    [Fact]
    public void TheNodeRegistersBeforeItsChildren()
    {
        Assert.Equal("whisp", _registered[0].Id);
        Assert.All(_registered.Skip(1), child => Assert.Equal("whisp", child.ParentId));
    }

    [Fact]
    public void TheMicrophonePageKeepsItsTagUnderTheNode()
    {
        SettingsModuleDescriptor microphone = _registered.Single(m => m.Id == "recording");

        Assert.Equal(MicrophoneTag, microphone.PageTag);
        Assert.Equal("whisp", microphone.ParentId);
    }

    [Fact]
    public void SearchFindsTranscribeAsYouSpeakOnDictationGeneral()
    {
        (IReadOnlyList<SettingSearchHit> hits, _) = SettingsSearchIndex.Search("transcribe as you speak", 5);

        SettingSearchHit top = Assert.Single(hits);
        Assert.Equal(GeneralTag, top.PageTag);
        Assert.Equal("TranscriptionStreamingEnabledCard", top.CardTag);
    }

    [Fact]
    public void SearchFindsLimitRecordingDurationOnDictationGeneral()
    {
        (IReadOnlyList<SettingSearchHit> hits, _) = SettingsSearchIndex.Search("limit recording", 5);

        SettingSearchHit top = Assert.Single(hits);
        Assert.Equal(GeneralTag, top.PageTag);
        Assert.Equal("RecordingLimitCard", top.CardTag);
    }

    [Theory]
    [InlineData("streaming", "TranscriptionStreamingEnabledCard", GeneralTag)]
    [InlineData("transcription", "TranscriptionStreamingEnabledCard", GeneralTag)]
    [InlineData("recording", "RecordingLimitCard", GeneralTag)]
    [InlineData("recording", "GeneralMicrophoneCard", MicrophoneTag)]
    public void TheOldWordsStillReachTheNewPlaces(string query, string cardTag, string pageTag)
    {
        (IReadOnlyList<SettingSearchHit> hits, _) = SettingsSearchIndex.Search(query, 20);

        Assert.Contains(hits, h => h.CardTag == cardTag && h.PageTag == pageTag);
    }

    [Theory]
    [InlineData("hangover")]
    [InlineData("silero")]
    [InlineData("segmentation")]
    [InlineData("silence and noise")]
    [InlineData("cut margin")]
    public void TheSegmentationCardsAreNeverOffered(string query)
    {
        (IReadOnlyList<SettingSearchHit> hits, _) = SettingsSearchIndex.Search(query, 20);

        Assert.DoesNotContain(hits, h => h.CardTag is "TranscriptionCutCurveCard" or "TranscriptionSilenceGuardsFold");
    }

    // The label of a key, read from the owning assembly's resw: the nav label is
    // the bare key, a card's is "<key>.Header" as the index resolves it.
    private static string Label(string assembly, string key)
    {
        string path = Path.Combine(RepoRoot(), "src", assembly, "Strings", "en-US", "Resources.resw");
        XElement data = XDocument.Load(path).Root!.Elements("data")
            .Single(d => (string)d.Attribute("name")! == key);
        return (string)data.Element("value")!;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}

// Both registries are process-wide, so the suites that fill them run one at a time.
[CollectionDefinition("SettingsRegistry", DisableParallelization = true)]
public sealed class SettingsRegistryCollection;
