using System.Text.Json;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Personal vocabulary adoption: clean recurrence across distinct days, explicit
// suppressions, category-sensitive protection, and persistence/migration.
[Trait("Category", "unit")]
public sealed class PersonalDictionaryTests : IDisposable
{
    private readonly string _path;
    private DateTimeOffset _now = new(2026, 06, 12, 0, 0, 0, TimeSpan.Zero);

    public PersonalDictionaryTests()
    {
        _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"deckle-pdict-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (System.IO.File.Exists(_path)) System.IO.File.Delete(_path);
    }

    private PersonalDictionary New(Func<string, bool>? wordAdmission = null) =>
        new(_path, () => _now, wordAdmission);

    private void Adopt(PersonalDictionary dict, string word)
    {
        dict.RecordCommit(word);
        _now = _now.AddDays(1);
        dict.RecordCommit(word);
        dict.RecordCommit(word);
    }

    [Fact]
    public void ThreeCleanCommitsAcrossTwoDistinctDaysAdoptTheWord()
    {
        using var dict = New();

        Adopt(dict, "café");

        Assert.True(dict.IsAdopted("café"));
        Assert.Contains("café", dict.AdoptedWords);
        var snap = Assert.Single(dict.SnapshotWords());
        Assert.Equal(3.0, snap.EffectiveWeight);
        Assert.True(snap.Adopted);
    }

    [Fact]
    public void ThreeCommitsOnOneDayDoNotAdopt()
    {
        using var dict = New();

        dict.RecordCommit("café");
        dict.RecordCommit("café");
        dict.RecordCommit("café");

        Assert.False(dict.IsAdopted("café"));
        Assert.DoesNotContain("café", dict.AdoptedWords);
    }

    [Fact]
    public void TwoCleanCommitsAcrossTwoDaysDoNotAdopt()
    {
        using var dict = New();

        dict.RecordCommit("café");
        _now = _now.AddDays(1);
        dict.RecordCommit("café");

        Assert.False(dict.IsAdopted("café"));
    }

    [Fact]
    public void ReEditRemovesTheSameDayCleanOccurrence()
    {
        using var dict = New();

        dict.RecordCommit("café");
        _now = _now.AddDays(1);
        dict.RecordCommit("café");
        dict.RecordReEdit("café");
        dict.RecordCommit("café");

        Assert.False(dict.IsAdopted("café")); // clean occurrences: day1=1, day2=1

        dict.RecordCommit("café");
        Assert.True(dict.IsAdopted("café"));
    }

    [Fact]
    public void ManualAccentFixDoesNotCountAsCleanAdoption()
    {
        using var dict = New();

        dict.RecordManualAccentFix("francais", "français");
        _now = _now.AddDays(1);
        dict.RecordManualAccentFix("francais", "français");
        dict.RecordManualAccentFix("francais", "français");

        Assert.False(dict.IsAdopted("français"));
        Assert.Empty(dict.SnapshotWords());
    }

    [Fact]
    public void RejectedWordNeverEntersTheLearningSurface()
    {
        using var dict = New(word => word != "prepare");

        Assert.False(dict.RecordCommit("prepare"));
        _now = _now.AddDays(1);
        Assert.False(dict.RecordCommit("prepare"));
        Assert.False(dict.RecordCommit("prepare"));

        Assert.False(dict.IsAdopted("prepare"));
        Assert.Empty(dict.SnapshotWords());
    }

    [Fact]
    public void ProperNounAdoptionIsCaseSensitive()
    {
        using var dict = New();

        Adopt(dict, "Claude");

        Assert.True(dict.IsAdopted("Claude"));
        Assert.False(dict.IsAdopted("claude"));
        Assert.Contains("Claude", dict.AdoptedWords);
    }

    [Fact]
    public void AnglicismAdoptionIsCaseInsensitive()
    {
        using var dict = New();

        Adopt(dict, "github");

        Assert.True(dict.IsAdopted("github"));
        Assert.True(dict.IsAdopted("GitHub"));
    }

    [Fact]
    public void OtherAdoptionIsCaseInsensitive()
    {
        using var dict = New();

        Adopt(dict, "démo");

        Assert.True(dict.IsAdopted("démo"));
        Assert.True(dict.IsAdopted("DÉMO"));
    }

    [Fact]
    public void SuppressForeverDoesNotAdoptTheLiteral()
    {
        using var dict = New();

        dict.RecordSuppression("foo", "bar");

        Assert.True(dict.IsSuppressed("foo", "bar"));
        Assert.False(dict.IsAdopted("foo"));
        Assert.Empty(dict.SnapshotWords());

        _now = _now.AddDays(365);
        Assert.True(dict.IsSuppressed("foo", "bar"));
    }

    [Fact]
    public void SuppressIsIdempotentOnThePair()
    {
        using var dict = New();

        dict.RecordSuppression("foo", "bar");
        dict.RecordSuppression("foo", "bar");

        Assert.Single(dict.SnapshotSuppressions());
    }

    [Fact]
    public void IsSuppressedIsCaseInsensitive()
    {
        using var dict = New();

        dict.RecordSuppression("Foo", "Bar");

        Assert.True(dict.IsSuppressed("foo", "bar"));
        Assert.True(dict.IsSuppressed("FOO", "BAR"));
    }

    [Fact]
    public void CapPruneKeepsTheAdoptedWord()
    {
        using var dict = New();

        const string heavy = "héritage";
        Adopt(dict, heavy);

        for (int i = 0; i < PersonalDictionary.MaxWords; i++)
            dict.RecordCommit($"light{i}");

        var words = dict.SnapshotWords();
        Assert.Equal(PersonalDictionary.MaxWords, words.Count);
        Assert.Contains(words, w => w.Word == heavy);
        Assert.Equal(PersonalDictionary.MaxWords - 1, words.Count(w => w.Word.StartsWith("light")));
    }

    [Fact]
    public void RemoveWordAndRemoveSuppressionReturnWhetherSomethingWent()
    {
        using var dict = New();

        dict.RecordCommit("mot");
        dict.RecordSuppression("orig", "repl");

        Assert.True(dict.RemoveWord("mot"));
        Assert.False(dict.RemoveWord("mot"));
        Assert.True(dict.RemoveSuppression("orig", "repl"));
        Assert.False(dict.RemoveSuppression("orig", "repl"));

        Assert.DoesNotContain(dict.SnapshotWords(), w => w.Word == "mot");
        Assert.Empty(dict.SnapshotSuppressions());
    }

    [Fact]
    public void PurgeClearsEverything()
    {
        using var dict = New();

        dict.RecordCommit("a");
        dict.RecordSuppression("b", "c");

        dict.Purge();

        Assert.Empty(dict.SnapshotWords());
        Assert.Empty(dict.SnapshotSuppressions());
    }

    [Fact]
    public void PersistenceRoundtrip()
    {
        using (var dict = New())
        {
            Adopt(dict, "café");
            dict.RecordSuppression("foo", "bar");
            dict.Flush();
        }

        using var reopened = New();
        Assert.True(reopened.IsAdopted("café"));
        Assert.True(reopened.IsSuppressed("foo", "bar"));
        Assert.False(reopened.IsAdopted("foo"));
    }

    [Fact]
    public void DisposeFlushesToDisk()
    {
        using (var dict = New())
        {
            Adopt(dict, "durable");
        }

        using var reopened = New();
        Assert.True(reopened.IsAdopted("durable"));
    }

    [Fact]
    public void LegacyPreSchemaTwoFileIsPurgedAndStampedOnLoad()
    {
        System.IO.File.WriteAllText(_path, """
            {
              "schemaVersion": 1,
              "words": [
                { "word": "café", "weight": 3.0, "lastSeenUtc": "2026-06-12T00:00:00+00:00" }
              ],
              "suppressions": [
                { "original": "cable", "replacement": "câble", "createdUtc": "2026-06-12T00:00:00+00:00" }
              ]
            }
            """);

        using var dict = New();

        Assert.Empty(dict.SnapshotWords());
        Assert.Empty(dict.SnapshotSuppressions());

        dict.Flush();
        var onDisk = JsonSerializer.Deserialize<PersonalDictionaryData>(
            System.IO.File.ReadAllText(_path),
            CamelCase);
        Assert.NotNull(onDisk);
        Assert.Equal(PersonalDictionaryData.CurrentSchemaVersion, onDisk!.SchemaVersion);
    }

    [Fact]
    public void CurrentVersionFileIsNotRePurged()
    {
        var stamped = new PersonalDictionaryData { SchemaVersion = PersonalDictionaryData.CurrentSchemaVersion };
        stamped.Words.Add(new WordEntry
        {
            Word = "café",
            Category = PersonalWordCategory.Other,
            CleanOccurrences = 3,
            CleanDays = new Dictionary<string, int>
            {
                ["2026-06-12"] = 1,
                ["2026-06-13"] = 2,
            },
            FirstSeenUtc = _now,
            LastSeenUtc = _now.AddDays(1),
        });
        System.IO.File.WriteAllText(_path, JsonSerializer.Serialize(stamped, CamelCase));

        using var dict = New();

        Assert.True(dict.IsAdopted("café"));
        Assert.Contains(dict.SnapshotWords(), w => w.Word == "café");
    }

    [Fact]
    public void CurrentFileDropsRejectedWordsAndPreservesAcceptedState()
    {
        var stamped = new PersonalDictionaryData { SchemaVersion = PersonalDictionaryData.CurrentSchemaVersion };
        stamped.Words.Add(AdoptedEntry("prepare", PersonalWordCategory.Anglicism));
        stamped.Words.Add(AdoptedEntry("telemetry", PersonalWordCategory.Anglicism));
        stamped.Suppressions.Add(new SuppressionEntry
        {
            Original = "docs",
            Replacement = "dos",
            CreatedUtc = _now,
        });
        System.IO.File.WriteAllText(_path, JsonSerializer.Serialize(stamped, CamelCase));

        using var dict = New(word => word != "prepare");

        Assert.False(dict.IsAdopted("prepare"));
        Assert.True(dict.IsAdopted("telemetry"));
        Assert.DoesNotContain(dict.SnapshotWords(), w => w.Word == "prepare");
        Assert.True(dict.IsSuppressed("docs", "dos"));
        Assert.Equal(1, dict.RemovedOnLoad);
    }

    [Fact]
    public void CorruptFileFallsBackStampedSoLaterLearningSurvives()
    {
        System.IO.File.WriteAllText(_path, "{ this is not valid json ");

        using (var dict = New())
        {
            Adopt(dict, "café");
            dict.Flush();
        }

        using var third = New();
        Assert.True(third.IsAdopted("café"));
    }

    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private WordEntry AdoptedEntry(string word, PersonalWordCategory category) => new()
    {
        Word = word,
        Category = category,
        CleanOccurrences = 3,
        CleanDays = new Dictionary<string, int>
        {
            ["2026-06-12"] = 1,
            ["2026-06-13"] = 2,
        },
        FirstSeenUtc = _now,
        LastSeenUtc = _now.AddDays(1),
    };
}
