using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

[Trait("Category", "unit")]
public class SpacePlanToolCatalogTests
{
    static IReadOnlyList<ToolDescriptor> BuildCatalog()
    {
        var client = new AnytypeApiClient(new AnytypeCredentials(
            "http://localhost:31009", "2025-11-08", "dummy-key", "dummy-space"));
        var aliases = new AnytypeSpaceAliases(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dev"] = "dummy-space",
        });
        return SpacePlanToolCatalog.Build(new SpacePlanGestures(client, aliases));
    }

    [Fact]
    public void ExposesPlanToolsAndTypeCreation() =>
        Assert.Equal(
            new[] { "create_type", "create_type_preview", "space_plan_apply", "space_plan_preview", "space_plan_read" },
            BuildCatalog().Select(t => t.Name).OrderBy(n => n).ToArray());

    [Fact]
    public void OnlyApplyWrites()
    {
        IReadOnlyList<ToolDescriptor> tools = BuildCatalog();
        Assert.Equal(ToolEffect.ReadOnly, tools.Single(t => t.Name == "space_plan_preview").Execution.Effect);
        Assert.Equal(ToolEffect.ReadOnly, tools.Single(t => t.Name == "space_plan_read").Execution.Effect);
        Assert.NotEqual(ToolEffect.ReadOnly, tools.Single(t => t.Name == "space_plan_apply").Execution.Effect);
    }

    [Fact]
    public void PlanApplyDeclaresItOverwritesAndCreateTypeOnlyAdds()
    {
        IReadOnlyList<ToolDescriptor> tools = BuildCatalog();
        Assert.Equal(ToolChangeKind.Overwriting, tools.Single(t => t.Name == "space_plan_apply").Execution.Change);
        Assert.Equal(ToolChangeKind.Additive, tools.Single(t => t.Name == "create_type").Execution.Change);
    }

    [Fact]
    public void EveryInputSchemaForbidsAdditionalProperties()
    {
        foreach (ToolDescriptor tool in BuildCatalog())
            Assert.False(tool.InputSchema["additionalProperties"]!.GetValue<bool>(), tool.Name);
    }

    [Fact]
    public void BothPreviewsAdvertiseTheStructuredOutputTheyReturn()
    {
        foreach (string name in new[] { "space_plan_preview", "create_type_preview" })
            Assert.NotNull(BuildCatalog().Single(t => t.Name == name).OutputSchema);
    }

    [Fact]
    public void PreviewProjectionMatchesItsAdvertisedOutputSchema()
    {
        var contract = new McpJsonSchemaContract(SpacePlanToolCatalog.PreviewOutputSchema());
        var planned = new SpacePlanPreview(
            new string('a', 64), "dev",
            [new SchemaPreviewAction("type_panel_add", "ticket:state", "featured"), new SchemaPreviewAction("views", "type:ticket", "")],
            [], ["conflict"], ["skipped"], "rendered");
        // A refused plan carries no preview id.
        var refused = new SpacePlanPreview(string.Empty, "dev", [], ["a view on a property nobody declared"], [], [], "rendered");

        Assert.True(contract.Accepts(SpacePlanToolCatalog.PreviewContent(planned)));
        Assert.True(contract.Accepts(SpacePlanToolCatalog.PreviewContent(refused)));
        Assert.Equal("featured", SpacePlanToolCatalog.PreviewContent(planned)["actions"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyDemandsAConfirmation()
    {
        ToolDescriptor apply = BuildCatalog().Single(t => t.Name == "space_plan_apply");
        Assert.Contains("confirm", apply.InputSchema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
    }
}
