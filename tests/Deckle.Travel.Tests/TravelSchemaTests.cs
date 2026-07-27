using Deckle.Anytype;
using Xunit;

namespace Deckle.Travel.Tests;

// The schema contract is the module's foundation: provisioning applies the
// manifest, and every gesture validates against the live space before writing.
// These assert the two halves agree, and that validation fails CLOSED.
public class TravelSchemaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SchemaSnapshot> ReadAsync(FakeTravelSpace space) =>
        await new SchemaSnapshotReader(space.NewClient()).ReadAsync(
            FakeTravelSpace.Space,
            [.. TravelSchema.ClosedVocabularies.Keys],
            Ct);

    // The whole point of the contract: what provisioning sends is what the
    // gestures then require. The fake answers tag options under opaque
    // provider keys — Anytype derives them from the French label at creation —
    // so this also pins that validation resolves options by label, not by the
    // manifest key it sent.
    [Trait("Category", "integration")]
    [Fact]
    public async Task AManifestProvisionedSpaceValidatesEvenThoughAnytypeRewritesOptionKeys()
    {
        using var space = new FakeTravelSpace();
        SchemaSnapshot snapshot = await ReadAsync(space);

        // No tag in the space carries a manifest option key, yet validation passes.
        Assert.DoesNotContain(
            snapshot.TagsByProperty[TravelSchema.Properties.Mode].Values,
            tag => tag.Key == TravelSchema.TransferModes.Plane);

        TravelSchema.Validate(snapshot);
    }

    [Trait("Category", "integration")]
    [Fact]
    public async Task ValidationFailsClosedAndPointsAtSchemaAdminWhenAPropertyIsMissing()
    {
        using var space = new FakeTravelSpace();
        SchemaSnapshot snapshot = await ReadAsync(space);
        SchemaSnapshot amputated = Without(snapshot, TravelSchema.Properties.Files);

        var error = Assert.Throws<TravelSchemaException>(() => TravelSchema.Validate(amputated));
        Assert.Contains(TravelSchema.Properties.Files, error.Message, StringComparison.Ordinal);
        Assert.Contains("schema-admin", error.Message, StringComparison.Ordinal);
    }

    [Trait("Category", "integration")]
    [Fact]
    public async Task ValidationFailsWhenAClosedVocabularyLacksAnOption()
    {
        using var space = new FakeTravelSpace();
        SchemaSnapshot snapshot = await ReadAsync(space);

        var tags = snapshot.TagsByProperty.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        tags[TravelSchema.Properties.Mode] = new Dictionary<string, SchemaTagInfo>(StringComparer.Ordinal);

        var error = Assert.Throws<TravelSchemaException>(() =>
            TravelSchema.Validate(new SchemaSnapshot(snapshot.Types, snapshot.Properties, tags)));
        Assert.Contains(TravelSchema.TransferModes.Ferry, error.Message, StringComparison.Ordinal);
    }

    [Trait("Category", "integration")]
    [Fact]
    public async Task ValidationFailsWhenATypeDoesNotCarryOneOfItsProperties()
    {
        using var space = new FakeTravelSpace();
        SchemaSnapshot snapshot = await ReadAsync(space);

        SchemaTypeInfo lodging = snapshot.Types[TravelSchema.Types.Lodging];
        var types = snapshot.Types.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        types[TravelSchema.Types.Lodging] = lodging with
        {
            PropertyLinks = [.. lodging.PropertyLinks.Where(link => link.Key != TravelSchema.Properties.Stage)],
        };

        var error = Assert.Throws<TravelSchemaException>(() =>
            TravelSchema.Validate(new SchemaSnapshot(types, snapshot.Properties, snapshot.TagsByProperty)));
        Assert.Contains(TravelSchema.Properties.Stage, error.Message, StringComparison.Ordinal);
    }

    // Decided twice and for the same reason: the Date is the state of an
    // Activity, and a recorded Expense is a fact. A status property creeping
    // back into the contract would undo both.
    [Fact]
    public void TheContractCarriesNoStatusProperty()
    {
        Assert.DoesNotContain(
            TravelSchema.RequiredProperties.Keys,
            key => key.Contains("status", StringComparison.OrdinalIgnoreCase));
    }

    // A type provisioned without an icon shows up blank in Anytype, and the
    // manifest is the only place that can carry one — schema_apply sets icons
    // it finds, never invents them.
    [Fact]
    public void EveryCreatableTypeDeclaresAnIcon()
    {
        Assert.All(
            TravelSchema.CreatableTypes,
            type => Assert.True(TravelSchema.TypeIcons.ContainsKey(type), type));
    }

    private static SchemaSnapshot Without(SchemaSnapshot snapshot, string propertyKey)
    {
        var properties = snapshot.Properties.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        properties.Remove(propertyKey);
        return new SchemaSnapshot(snapshot.Types, properties, snapshot.TagsByProperty);
    }
}
