using System.Runtime.InteropServices;

namespace Deckle.Orchestration;

// Win32 display configuration (CCD) the HDR rule needs: list the active
// display paths, name their source (GDI device) and target (monitor), read and
// set HDR. Layouts transcribed from wingdi.h (Windows SDK 10.0.26100) and
// pinned by DisplayConfigLayoutTests. Module-local, like TaskbarCover's interop.
internal static class DisplayConfigNativeMethods
{
    internal const int ERROR_SUCCESS = 0;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;

    internal const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    // DISPLAYCONFIG_DEVICE_INFO_TYPE
    internal const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    internal const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    internal const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    internal const uint DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10;
    internal const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2 = 15;
    internal const uint DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE = 16;

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    // 20 bytes: adapterId 8 + id 4 + modeInfoIdx union 4 + statusFlags 4.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    // 48 bytes: adapterId 8 + id 4 + modeInfoIdx 4 + outputTechnology 4
    // + rotation 4 + scaling 4 + refreshRate (rational) 8 + scanLineOrdering 4
    // + targetAvailable (BOOL) 4 + statusFlags 4.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public uint refreshRateNumerator;
        public uint refreshRateDenominator;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    // infoType 4 + id 4 + adapterId 8, then a union whose largest member is
    // DISPLAYCONFIG_TARGET_MODE (one DISPLAYCONFIG_VIDEO_SIGNAL_INFO, 48
    // bytes). Never read here, so the union is an opaque block.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        public byte[] modeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] // CCHDEVICENAME
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    // Type 9. value bits: 0 advancedColorSupported, 1 advancedColorEnabled,
    // 2 wideColorEnforced, 3 advancedColorForceDisabled.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    // Type 10. value bit 0: enableAdvancedColor.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
    }

    // Type 15 (Windows 11 24H2). value bits: 0 advancedColorSupported,
    // 1 advancedColorActive, 2 reserved1, 3 advancedColorLimitedByPolicy,
    // 4 highDynamicRangeSupported, 5 highDynamicRangeUserEnabled,
    // 6 wideColorSupported, 7 wideColorUserEnabled.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
        public uint activeColorMode;
    }

    // Type 16 (Windows 11 24H2). value bit 0: enableHdr.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_SET_HDR_STATE
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
    }

    internal const uint AdvancedColorSupportedBit = 1u << 0;
    internal const uint AdvancedColorEnabledBit = 1u << 1;
    internal const uint HighDynamicRangeSupportedBit = 1u << 4;
    internal const uint HighDynamicRangeUserEnabledBit = 1u << 5;

    [DllImport("user32.dll")]
    internal static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    // currentTopologyId must be null with QDC_ONLY_ACTIVE_PATHS.
    [DllImport("user32.dll")]
    internal static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        nint currentTopologyId);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 requestPacket);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE setPacket);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_HDR_STATE setPacket);

    internal static DISPLAYCONFIG_DEVICE_INFO_HEADER Header<T>(uint type, LUID adapterId, uint id) where T : struct => new()
    {
        type = type,
        size = (uint)Marshal.SizeOf<T>(),
        adapterId = adapterId,
        id = id,
    };
}
