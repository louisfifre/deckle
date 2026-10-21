using static Deckle.Orchestration.DisplayConfigNativeMethods;

namespace Deckle.Orchestration;

// Reads and sets one monitor's HDR switch, the one in Settings > Display >
// "Use HDR". Windows 11 24H2 (build 26100) split HDR from wide color gamut and
// added a dedicated pair (GET_ADVANCED_COLOR_INFO_2 / SET_HDR_STATE). Earlier
// builds only have the "advanced color" pair, whose flag conflates HDR and WCG:
// on those, "supported" also covers a WCG-only SDR monitor and switching it
// toggles advanced color, not HDR alone.
internal static class HdrStateSwitch
{
    private static bool UseHdrPair => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100);

    // Which pair TryRead and TrySet use, for the logs: the two behave
    // differently on WCG-only monitors, so a report must say which ran.
    public static string ApiName => UseHdrPair ? "hdr_state" : "advanced_color";

    public static bool TryRead(LUID adapter, uint target, out bool supported, out bool enabled)
    {
        if (UseHdrPair)
        {
            var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
            {
                header = Header<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>(
                    DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2, adapter, target),
            };
            if (DisplayConfigGetDeviceInfo(ref info) == ERROR_SUCCESS)
            {
                supported = (info.value & HighDynamicRangeSupportedBit) != 0;
                enabled = (info.value & HighDynamicRangeUserEnabledBit) != 0;
                return true;
            }
        }
        else
        {
            var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
            {
                header = Header<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(
                    DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO, adapter, target),
            };
            if (DisplayConfigGetDeviceInfo(ref info) == ERROR_SUCCESS)
            {
                supported = (info.value & AdvancedColorSupportedBit) != 0;
                enabled = (info.value & AdvancedColorEnabledBit) != 0;
                return true;
            }
        }

        supported = false;
        enabled = false;
        return false;
    }

    /// <summary>Switch HDR. False with the Win32 error code when Windows refused.</summary>
    public static bool TrySet(LUID adapter, uint target, bool enable, out int error)
    {
        if (UseHdrPair)
        {
            var packet = new DISPLAYCONFIG_SET_HDR_STATE
            {
                header = Header<DISPLAYCONFIG_SET_HDR_STATE>(DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE, adapter, target),
                value = enable ? 1u : 0u,
            };
            error = DisplayConfigSetDeviceInfo(ref packet);
        }
        else
        {
            var packet = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
            {
                header = Header<DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>(
                    DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE, adapter, target),
                value = enable ? 1u : 0u,
            };
            error = DisplayConfigSetDeviceInfo(ref packet);
        }
        return error == ERROR_SUCCESS;
    }
}
