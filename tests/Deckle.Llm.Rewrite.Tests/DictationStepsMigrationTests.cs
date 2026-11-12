using System.Text.Json;
using Deckle.Core;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// A settings file written before the dictation steps (an active profile, a
// length ladder, a switch for batches) loads through the real store and the
// real normalizer hook, and comes out as steps that send a dictation where it
// went before. Fixture files only, written to a temp folder: never a person's
// settings.json.
[Trait("Category", "integration")]
public sealed class DictationStepsMigrationTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"deckle-llm-steps-test-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_folder, "settings.json");

    public DictationStepsMigrationTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private JsonSettingsStore<LlmSettings> OpenStore(string? initialText = null)
    {
        if (initialText is not null) File.WriteAllText(FilePath, initialText);
        return new JsonSettingsStore<LlmSettings>(
            FilePath, $"Deckle-Llm-Steps-Test-{Guid.NewGuid():N}", Options,
            postLoadMigration: LlmSettingsNormalizer.Apply);
    }

    private const string Profiles = """
        "profiles": [
          { "id": "aff", "name": "Affinage", "providerId": "claude", "model": "m", "systemPrompt": "A." },
          { "id": "lis", "name": "Lissage", "providerId": "claude", "model": "m", "systemPrompt": "L." },
          { "id": "arr", "name": "Arrangement", "providerId": "claude", "model": "m", "systemPrompt": "R." }
        ]
        """;

    // No active profile, a ladder on, batches off: below the ladder's first
    // step a dictation stayed as said, so that step becomes the first one,
    // at its length.
    [Fact]
    public void ALadderWithoutAnActiveProfileStartsAtItsFirstStep()
    {
        using var store = OpenStore($$"""
            {
              "enabled": true,
              "batchRewriting": { "enabled": false, "thresholdChars": 600 },
              "lengthSwitch": { "enabled": true, "steps": [
                { "fromChars": 4000, "profileId": "lis" },
                { "fromChars": 1000, "profileId": "aff" },
                { "fromChars": 12000, "profileId": "arr" } ] },
              "activeProfileId": null,
              "activeProfileName": null,
              {{Profiles}}
            }
            """);

        DictationRewriteSettings d = store.Current.Dictation;
        Assert.True(d.Enabled);
        Assert.False(d.RewriteWhileSpeaking);
        Assert.Equal(
            [("aff", 1000, 600), ("lis", 4000, 600), ("arr", 12000, 600)],
            d.Steps.Select(step => (step.ProfileId, step.FromChars, step.BlockChars)));
    }

    // An active profile becomes the first step from the beginning, the
    // ladder follows it, and the batch switch becomes "Rewrite while I
    // speak", its size every step's block size.
    [Fact]
    public void AnActiveProfileBecomesTheFirstStepAndBatchesStreamEveryStep()
    {
        using var store = OpenStore($$"""
            {
              "batchRewriting": { "enabled": true, "thresholdChars": 800 },
              "lengthSwitch": { "enabled": true, "steps": [
                { "fromChars": 4000, "profileId": "lis" },
                { "fromChars": 12000, "profileId": "arr" } ] },
              "activeProfileId": "aff",
              "activeProfileName": "Affinage",
              {{Profiles}}
            }
            """);

        DictationRewriteSettings d = store.Current.Dictation;
        Assert.True(d.Enabled);
        Assert.Equal(0, d.Steps[0].FromChars);
        Assert.True(d.RewriteWhileSpeaking);
        Assert.Equal(
            [("aff", 800), ("lis", 800), ("arr", 800)],
            d.Steps.Select(step => (step.ProfileId, step.BlockChars)));
        Assert.Equal([4000, 12000], d.Steps.Skip(1).Select(step => step.FromChars));
    }

    // An active profile known only by its name is paired with its id before
    // the migration reads it.
    [Fact]
    public void AnActiveProfileKnownByNameBecomesTheFirstStepById()
    {
        using var store = OpenStore($$"""
            {
              "activeProfileName": "lissage",
              {{Profiles}}
            }
            """);

        Assert.Equal("lis", store.Current.Dictation.Steps[0].ProfileId);
        Assert.True(store.Current.Dictation.Enabled);
    }

    // A ladder switched off never applied: as steps it would, so it is not
    // carried over.
    [Fact]
    public void ALadderSwitchedOffIsNotCarriedOver()
    {
        using var store = OpenStore($$"""
            {
              "lengthSwitch": { "enabled": false, "steps": [ { "fromChars": 4000, "profileId": "lis" } ] },
              "activeProfileId": "aff",
              {{Profiles}}
            }
            """);

        RewriteStep only = Assert.Single(store.Current.Dictation.Steps);
        Assert.Equal("aff", only.ProfileId);
    }

    // Nothing active and no ladder: dictations stayed as said, so rewriting
    // dictations is off, and the first step waits with the first profile.
    [Fact]
    public void NothingActiveAndNoLadderLeavesRewritingDictationsOff()
    {
        using var store = OpenStore($$"""
            {
              "batchRewriting": { "enabled": false, "thresholdChars": 600 },
              "lengthSwitch": { "enabled": false, "steps": [] },
              {{Profiles}}
            }
            """);

        DictationRewriteSettings d = store.Current.Dictation;
        Assert.False(d.Enabled);
        RewriteStep only = Assert.Single(d.Steps);
        Assert.Equal(("aff", 0), (only.ProfileId, only.FromChars));
    }

    // The old fields are read, then gone from the file on disk.
    [Fact]
    public void TheOldFieldsLeaveTheFileOnceMigrated()
    {
        using (var store = OpenStore($$"""
            {
              "batchRewriting": { "enabled": true, "thresholdChars": 800 },
              "lengthSwitch": { "enabled": true, "steps": [ { "fromChars": 4000, "profileId": "lis" } ] },
              "activeProfileId": "aff",
              {{Profiles}}
            }
            """))
        {
            store.Flush();
        }

        string onDisk = File.ReadAllText(FilePath);
        Assert.DoesNotContain("batchRewriting", onDisk);
        Assert.DoesNotContain("lengthSwitch", onDisk);
        Assert.DoesNotContain("activeProfile", onDisk);
        Assert.Contains("\"dictation\"", onDisk);
    }

    // The migration runs once: what it wrote loads back as it is, and a step
    // the person changes afterwards is not rebuilt from the old fields.
    [Fact]
    public void AMigratedFileLoadsBackWithoutMigratingAgain()
    {
        using (var first = OpenStore($$"""
            {
              "lengthSwitch": { "enabled": true, "steps": [ { "fromChars": 1000, "profileId": "aff" } ] },
              {{Profiles}}
            }
            """))
        {
            first.Current.Dictation.Steps[0].ProfileId = "arr";
            first.Current.Dictation.Steps[0].BlockChars = 6000;
            first.Current.Dictation.RewriteWhileSpeaking = true;
            first.Flush();
        }

        using var second = OpenStore();

        RewriteStep step = Assert.Single(second.Current.Dictation.Steps);
        Assert.Equal(("arr", 1000, 6000), (step.ProfileId, step.FromChars, step.BlockChars));
        Assert.True(second.Current.Dictation.RewriteWhileSpeaking);
    }

    // A file of 2026-11-11 (a "Streaming" switch on each step, one passage
    // size): any step that streamed turns "Rewrite while I speak" on, every
    // step takes the passage size as its block size, and neither old field
    // is written back.
    [Fact]
    public void AStepThatStreamedTurnsRewriteWhileSpeakingOnAndThePassageSizeBecomesEveryBlockSize()
    {
        using (var store = OpenStore($$"""
            {
              "dictation": {
                "enabled": true,
                "startAtLength": true,
                "steps": [
                  { "profileId": "aff", "fromChars": 1000, "streaming": false },
                  { "profileId": "lis", "fromChars": 4000, "streaming": true },
                  { "profileId": "arr", "fromChars": 12000, "streaming": false } ],
                "passageChars": 800
              },
              {{Profiles}}
            }
            """))
        {
            DictationRewriteSettings d = store.Current.Dictation;
            Assert.True(d.RewriteWhileSpeaking);
            Assert.Equal(
                [("aff", 1000, 800), ("lis", 4000, 800), ("arr", 12000, 800)],
                d.Steps.Select(step => (step.ProfileId, step.FromChars, step.BlockChars)));
            store.Flush();
        }

        string onDisk = File.ReadAllText(FilePath);
        Assert.DoesNotContain("\"streaming\"", onDisk);
        Assert.DoesNotContain("passageChars", onDisk);
        Assert.DoesNotContain("startAtLength", onDisk);
        Assert.Contains("\"rewriteWhileSpeaking\": true", onDisk);
    }

    // No step streamed: the switch stays off, and the lengths do not move.
    [Fact]
    public void NoStepThatStreamedLeavesRewriteWhileSpeakingOff()
    {
        using var store = OpenStore($$"""
            {
              "dictation": {
                "enabled": true,
                "steps": [
                  { "profileId": "aff", "fromChars": 1000, "streaming": false },
                  { "profileId": "lis", "fromChars": 4000, "streaming": false } ],
                "passageChars": 600
              },
              {{Profiles}}
            }
            """);

        DictationRewriteSettings d = store.Current.Dictation;
        Assert.False(d.RewriteWhileSpeaking);
        Assert.Equal([(1000, 600), (4000, 600)], d.Steps.Select(step => (step.FromChars, step.BlockChars)));
    }

    // A file of 2026-11-11 with "Start at" unticked: its first step started
    // from the beginning whatever its length said, so that length becomes 0;
    // ticked, the length is kept. Either way the box leaves the file.
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1000)]
    public void StartAtBecomesTheFirstStepsLength(bool startAt, int firstFrom)
    {
        using (var store = OpenStore($$"""
            {
              "dictation": {
                "enabled": true,
                "startAtLength": {{(startAt ? "true" : "false")}},
                "steps": [
                  { "profileId": "aff", "fromChars": 1000, "blockChars": 600 },
                  { "profileId": "lis", "fromChars": 4000, "blockChars": 2000 } ]
              },
              {{Profiles}}
            }
            """))
        {
            Assert.Equal([firstFrom, 4000], store.Current.Dictation.Steps.Select(step => step.FromChars));
            store.Flush();
        }

        Assert.DoesNotContain("startAtLength", File.ReadAllText(FilePath));
    }
}

[Trait("Category", "unit")]
public sealed class DictationStepsNormalizerTests
{
    private static LlmSettings WithSteps(params (int From, int BlockChars)[] steps)
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.Apply(settings);
        settings.Dictation.Steps = steps
            .Select(step => new RewriteStep
            {
                ProfileId = settings.Profiles[0].Id,
                FromChars = step.From,
                BlockChars = step.BlockChars,
            })
            .ToList();
        return settings;
    }

    [Fact]
    public void FreshSettingsHaveOneFirstStepWithAProfileAndRewritingDictationsOff()
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.Apply(settings);

        RewriteStep first = Assert.Single(settings.Dictation.Steps);
        Assert.Equal(settings.Profiles[0].Id, first.ProfileId);
        Assert.False(settings.Dictation.Enabled);
        Assert.False(LlmSettingsNormalizer.Apply(settings));
    }

    // The first step stays first; the later ones sort.
    [Fact]
    public void TheLaterStepsSortAndTheFirstStaysFirst()
    {
        LlmSettings settings = WithSteps((0, 600), (4000, 2000), (1000, 600));

        Assert.True(LlmSettingsNormalizer.NormalizeDictation(settings));

        Assert.Equal([0, 1000, 4000], settings.Dictation.Steps.Select(step => step.FromChars));
        Assert.Equal([600, 600, 2000], settings.Dictation.Steps.Select(step => step.BlockChars));
    }

    // A later step at or below the first moves just above it rather than
    // being dropped.
    [Fact]
    public void ALaterStepBelowTheFirstMovesJustAboveIt()
    {
        LlmSettings settings = WithSteps((1000, 600), (800, 600));

        LlmSettingsNormalizer.NormalizeDictation(settings);

        Assert.Equal([1000, 1100], settings.Dictation.Steps.Select(step => step.FromChars));
    }

    [Fact]
    public void LengthsSnapToTheirGrainAndAStepPastTheMaximumIsDropped()
    {
        LlmSettings settings = WithSteps((40, 600), (1234, 600), (29600, 600), (29900, 600));

        LlmSettingsNormalizer.NormalizeDictation(settings);

        Assert.Equal([0, 1200, 30000], settings.Dictation.Steps.Select(step => step.FromChars));
    }

    // Page 8: a block of 6,000 is accepted as typed; below the minimum or
    // above the maximum a size comes back within the bounds, and a step
    // without one takes the default.
    [Fact]
    public void ABlockSizeIsKeptWithinItsBoundsAndSixThousandIsAccepted()
    {
        LlmSettings settings = WithSteps((1000, 50), (4000, 0), (12000, 6000), (20000, 99000));

        LlmSettingsNormalizer.NormalizeDictation(settings);

        Assert.Equal(
            [DictationRewriteSettings.MinBlockChars, DictationRewriteSettings.DefaultBlockChars, 6000, DictationRewriteSettings.MaxBlockChars],
            settings.Dictation.Steps.Select(step => step.BlockChars));
    }
}
