using Deckle.Lighting;

namespace Deckle.Lighting.Ambient;

internal static class HueEndpointResolver
{
    public static async Task<(HueBridge? Bridge, int Candidates, int Valid)> FindAsync(
        string persistedBridgeId,
        IReadOnlyList<HueBridge> discovered,
        Func<HueBridge, CancellationToken, Task<bool>> validate,
        CancellationToken ct)
    {
        var candidates = string.Equals(
            persistedBridgeId,
            HuePairingService.ManualBridgeId,
            StringComparison.OrdinalIgnoreCase)
            ? discovered
            : discovered.Where(bridge => string.Equals(
                bridge.Id,
                persistedBridgeId,
                StringComparison.OrdinalIgnoreCase)).ToArray();

        HueBridge? match = null;
        int valid = 0;
        foreach (var candidate in candidates)
        {
            if (!await validate(candidate, ct).ConfigureAwait(false)) continue;
            match ??= candidate;
            valid++;
        }

        return (valid == 1 ? match : null, candidates.Count, valid);
    }
}
