using static Deckle.Orchestration.DisplayConfigNativeMethods;

namespace Deckle.Orchestration;

// The monitors in use right now, one per active display path. A monitor whose
// names or HDR state cannot be read is still listed, as not HDR-capable. Never
// throws: from Enumerate, an empty list means the configuration could not be
// read, which the page treats the same as "no HDR monitor"; TryEnumerate tells
// the two apart.
internal static class DisplayTargets
{
    // The configuration can change between sizing the buffers and filling
    // them (a monitor plugged in); Windows then answers "insufficient buffer"
    // and the query is sized again.
    private const int QueryAttempts = 3;

    // 1 between a failed query and the next one that succeeds: the page and
    // every HDR rule query the list, so a lasting failure would otherwise
    // raise one Warning per launch and per page visit.
    private static int _queryFailing;

    public static IReadOnlyList<DisplayTarget> Enumerate()
    {
        TryEnumerate(out IReadOnlyList<DisplayTarget> targets);
        return targets;
    }

    // False when the configuration itself could not be read, so a rule can
    // tell "monitor not connected" from "monitor list unknown".
    public static bool TryEnumerate(out IReadOnlyList<DisplayTarget> result)
    {
        DISPLAYCONFIG_PATH_INFO[]? paths = QueryActivePaths();
        if (paths is null)
        {
            result = [];
            return false;
        }

        var targets = new List<DisplayTarget>(paths.Length);
        foreach (DISPLAYCONFIG_PATH_INFO path in paths)
        {
            string? gdiName = ReadSourceName(path.sourceInfo.adapterId, path.sourceInfo.id);
            if (string.IsNullOrEmpty(gdiName)) continue;

            LUID adapter = path.targetInfo.adapterId;
            uint target = path.targetInfo.id;
            string friendly = ReadTargetName(adapter, target) is { Length: > 0 } name ? name : gdiName;
            HdrStateSwitch.TryRead(adapter, target, out bool supported, out bool enabled);

            targets.Add(new DisplayTarget(gdiName, friendly, supported, enabled, adapter, target));
        }
        result = targets;
        return true;
    }

    private static DISPLAYCONFIG_PATH_INFO[]? QueryActivePaths()
    {
        int result = ERROR_SUCCESS;
        for (int attempt = 0; attempt < QueryAttempts; attempt++)
        {
            result = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
            if (result != ERROR_SUCCESS) break;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, 0);
            if (result == ERROR_SUCCESS)
            {
                if (Interlocked.Exchange(ref _queryFailing, 0) == 1)
                    DeckleOrchestrationSource.Log.DisplayQueryRecovered();
                return paths[..(int)pathCount];
            }
            if (result != ERROR_INSUFFICIENT_BUFFER) break;
        }

        if (Interlocked.Exchange(ref _queryFailing, 1) == 0)
            DeckleOrchestrationSource.Log.DisplayQueryFailed(result);
        else
            DeckleOrchestrationSource.Log.DisplayQueryFailedDetail(result);
        return null;
    }

    private static string? ReadSourceName(LUID adapter, uint source)
    {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = Header<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, adapter, source),
        };
        return DisplayConfigGetDeviceInfo(ref request) == ERROR_SUCCESS ? request.viewGdiDeviceName : null;
    }

    private static string? ReadTargetName(LUID adapter, uint target)
    {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = Header<DISPLAYCONFIG_TARGET_DEVICE_NAME>(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, adapter, target),
        };
        return DisplayConfigGetDeviceInfo(ref request) == ERROR_SUCCESS ? request.monitorFriendlyDeviceName : null;
    }
}
