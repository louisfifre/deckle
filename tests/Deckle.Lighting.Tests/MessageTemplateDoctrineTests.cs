using Deckle.TestSupport;
using Xunit;

namespace Deckle.Lighting.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleLightingSource.BridgePairedDetail2", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.BridgeReturnedNoLightsDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.BridgeUnreachable", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.ClipV2RequestFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.DiscoveryBridgeFound", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.DiscoveryFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.DiscoveryFoundDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.DiscoveryStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentArea", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentConnectCancelled", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentConnectFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentEmpty", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleLightingSource.EntertainmentListed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentPrePrimeDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentPrePrimeFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentRestFallback", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentSessionClosed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentStreamingStartingDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentStreamingStopFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentStreamPrimed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentTransportConnected", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EntertainmentV2Catalog", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EventStreamIncident", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EventStreamIncidentAbandoned", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EventStreamReconnecting", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EventStreamRecoveryDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.EventStreamStarting", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleLightingSource.EventStreamStopped", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.FetchingV2IdMaps", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleLightingSource.GroupListed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.GroupsListed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.HueStepFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.IdentifyFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.LightIdentified", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.LightListed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.LightOutputSelected", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.LightsListed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.LightsListedEmpty", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.ListingLightsInGroupDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.LocalDiscoveryCompletedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.LocalDiscoveryFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.LocalDiscoveryStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.PairingHttpError", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.PairingRejected", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.PairingStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.PairingTimedOut", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.PairingWaiting", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.PlacementListed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.PushColor", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.PushColorOff", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.SetColorFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleLightingSource.V2IdMapsFetched", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleLightingSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
