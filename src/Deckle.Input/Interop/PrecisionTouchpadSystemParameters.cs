using System.Runtime.InteropServices;

namespace Deckle.Input;

// Reads the user's native Precision Touchpad direction so injected contact
// motion preserves the semantic direction of the physical mouse wheel.
public static class PrecisionTouchpadSystemParameters
{
    public static bool TryGetScrollDirectionReversed(out bool reversed, out int error)
    {
        var parameters = new TouchpadParametersInterop.TouchpadParameters
        {
            VersionNumber = TouchpadParametersInterop.LatestVersion,
        };

        bool succeeded = TouchpadParametersInterop.SystemParametersInfo(
            TouchpadParametersInterop.SPI_GETTOUCHPADPARAMETERS,
            (uint)Marshal.SizeOf<TouchpadParametersInterop.TouchpadParameters>(),
            ref parameters,
            0);

        error = succeeded ? 0 : Marshal.GetLastWin32Error();
        reversed = succeeded
            && (parameters.UserSettings & TouchpadParametersInterop.ScrollDirectionReversed) != 0;
        return succeeded;
    }
}

internal static class TouchpadParametersInterop
{
    public const uint SPI_GETTOUCHPADPARAMETERS = 0x00AE;
    public const uint LatestVersion = 1;
    public const uint ScrollDirectionReversed = 1u << 9;

    // Native TOUCHPAD_PARAMETERS_V1. The two bit-field groups are represented
    // by their underlying 32-bit storage words.
    [StructLayout(LayoutKind.Sequential)]
    internal struct TouchpadParameters
    {
        public uint VersionNumber;
        public uint MaxSupportedContacts;
        public uint LegacyTouchpadFeatures;
        public uint SystemInformation;
        public uint UserSettings;
        public uint SensitivityLevel;
        public uint CursorSpeed;
        public uint FeedbackIntensity;
        public uint ClickForceSensitivity;
        public uint RightClickZoneWidth;
        public uint RightClickZoneHeight;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        ref TouchpadParameters value,
        uint flags);
}
