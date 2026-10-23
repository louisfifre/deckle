using Deckle.TestSupport;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleAnytypeMcpSource.HostInternalLog", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.HostInternalLogCounted", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.HostInternalLogRepeated", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.HostProvisioningFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.HostStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.HostStartFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.HostStartRefused", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.HostStopFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.RequestRejected", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.RequestRejectedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.RequestRejectionsEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.TokensMinted", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.TokenVariablesMissing", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.TokenVariablesPublished", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.ToolCallCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.ToolCallFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAnytypeMcpSource.ToolCallStarted", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleAnytypeMcpSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
