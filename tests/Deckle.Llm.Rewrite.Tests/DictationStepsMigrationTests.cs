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
    // step a dictation stayed as said, so that step becomes the first one
    // with "Start at" on at its length.
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
        Assert.True(d.StartAtLength);
        Assert.Equal(
            [("aff", 1000, false), ("lis", 4000, false), ("arr", 12000, false)],
            d.Steps.Select(step => (step.ProfileId, step.FromChars, step.Streaming)));
        Assert.Equal(600, d.PassageChars);
    }

    // An active profile becomes the first step from the beginning, the
    // ladder follows it, and the batch switch streams every step.
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
        Assert.False(d.StartAtLength);
        Assert.Equal(0, d.StartOf(0));
        Assert.Equal(
            [("aff", true), ("lis", true), ("arr", true)],
            d.Steps.Select(step => (step.ProfileId, step.Streaming)));
        Assert.Equal([4000, 12000], d.Steps.Skip(1).Select(step => step.FromChars));
        Assert.Equal(800, d.PassageChars);
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
        Assert.False(d.StartAtLength);
        Assert.Equal("aff", Assert.Single(d.Steps).ProfileId);
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
            first.Current.Dictation.Steps[0].Streaming = true;
            first.Flush();
        }

        using var second = OpenStore();

        RewriteStep step = Assert.Single(second.Current.Dictation.Steps);
        Assert.Equal(("arr", 1000, true), (step.ProfileId, step.FromChars, step.Streaming));
        Assert.True(second.Current.Dictation.StartAtLength);
    }
}

[Trait("Category", "unit")]
public sealed class DictationStepsNormalizerTests
{
    private static LlmSettings WithSteps(bool startAt, params (int From, bool Streaming)[] steps)
    {
        var settings = new LlmSettings();
        LlmSettingsNormalizer.Apply(settings);
        settings.Dictation.StartAtLength = startAt;
        settings.Dictation.Steps = steps
            .Select(step => new RewriteStep
            {
                ProfileId = settings.Profiles[0].Id,
                FromChars = step.From,
                Streaming = step.Streaming,
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

    // The first step stays first whatever its length; the later ones sort.
    [Fact]
    public void TheLaterStepsSortAndTheFirstStaysFirst()
    {
        LlmSettings settings = WithSteps(false, (5000, false), (4000, true), (1000, false));

        Assert.True(LlmSettingsNormalizer.NormalizeDictation(settings));

        Assert.Equal([5000, 1000, 4000], settings.Dictation.Steps.Select(step => step.FromChars));
        Assert.Equal([false, false, true], settings.Dictation.Steps.Select(step => step.Streaming));
    }

    // With "Start at" on, a later step at or below the first moves just above
    // it rather than being dropped.
    [Fact]
    public void WithStartAtALaterStepBelowTheFirstMovesJustAboveIt()
    {
        LlmSettings settings = WithSteps(true, (1000, false), (800, true));

        LlmSettingsNormalizer.NormalizeDictation(settings);

        Assert.Equal([1000, 1100], settings.Dictation.Steps.Select(step => step.FromChars));
    }

    [Fact]
    public void LengthsSnapToTheirGrainAndAStepPastTheMaximumIsDropped()
    {
        LlmSettings settings = WithSteps(false, (0, false), (1234, false), (29600, false), (29900, false));

        LlmSettingsNormalizer.NormalizeDictation(settings);

        Assert.Equal([100, 1200, 30000], settings.Dictation.Steps.Select(step => step.FromChars));
        Assert.Equal(0, settings.Dictation.StartOf(0));
    }

    [Fact]
    public void ThePassageSizeIsKeptWithinItsBounds()
    {
        LlmSettings settings = WithSteps(false, (1000, false));
        settings.Dictation.PassageChars = 50;

        LlmSettingsNormalizer.NormalizeDictation(settings);

        Assert.Equal(DictationRewriteSettings.MinPassageChars, settings.Dictation.PassageChars);
    }
}
