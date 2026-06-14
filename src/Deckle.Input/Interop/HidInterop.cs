using System.Runtime.InteropServices;

namespace Deckle.Input;

// hid.dll preparsed-data parsing — the user-mode HID parser that turns a
// raw input report into per-usage values without hand-decoding the report
// descriptor. Signatures follow emoacht/RawInput.Touchpad (MIT), the
// reference implementation for Precision Touchpad parsing in C#; the
// button-caps / GetUsages surface (tip switch, confidence) is added on
// top — the references never read it, Deckle's recognizer depends on it.
public static class HidInterop
{
    public const uint HIDP_STATUS_SUCCESS = 0x00110000;

    public enum HIDP_REPORT_TYPE
    {
        HidP_Input,
        HidP_Output,
        HidP_Feature,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage;
        public byte   ReportID;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsAlias;

        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsRange;
        [MarshalAs(UnmanagedType.U1)]
        public bool IsStringRange;
        [MarshalAs(UnmanagedType.U1)]
        public bool IsDesignatorRange;
        [MarshalAs(UnmanagedType.U1)]
        public bool IsAbsolute;
        [MarshalAs(UnmanagedType.U1)]
        public bool HasNull;

        public byte   Reserved;
        public ushort BitSize;
        public ushort ReportCount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public ushort[] Reserved2;

        public uint UnitsExp;
        public uint Units;
        public int  LogicalMin;
        public int  LogicalMax;
        public int  PhysicalMin;
        public int  PhysicalMax;

        // Range / NotRange union: for a NotRange cap the single Usage sits
        // in UsageMin (same layout trick as the reference implementation).
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;

        public readonly ushort Usage => UsageMin;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_BUTTON_CAPS
    {
        public ushort UsagePage;
        public byte   ReportID;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsAlias;

        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsRange;
        [MarshalAs(UnmanagedType.U1)]
        public bool IsStringRange;
        [MarshalAs(UnmanagedType.U1)]
        public bool IsDesignatorRange;
        [MarshalAs(UnmanagedType.U1)]
        public bool IsAbsolute;

        public ushort ReportCount;
        public ushort Reserved2;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public uint[] Reserved;

        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;

        public readonly ushort Usage => UsageMin;
    }

    [DllImport("hid.dll")]
    public static extern uint HidP_GetCaps(
        IntPtr PreparsedData,
        out HIDP_CAPS Capabilities);

    [DllImport("hid.dll")]
    public static extern uint HidP_GetValueCaps(
        HIDP_REPORT_TYPE ReportType,
        [Out] HIDP_VALUE_CAPS[] ValueCaps,
        ref ushort ValueCapsLength,
        IntPtr PreparsedData);

    [DllImport("hid.dll")]
    public static extern uint HidP_GetButtonCaps(
        HIDP_REPORT_TYPE ReportType,
        [Out] HIDP_BUTTON_CAPS[] ButtonCaps,
        ref ushort ButtonCapsLength,
        IntPtr PreparsedData);

    [DllImport("hid.dll")]
    public static extern uint HidP_GetUsageValue(
        HIDP_REPORT_TYPE ReportType,
        ushort UsagePage,
        ushort LinkCollection,
        ushort Usage,
        out uint UsageValue,
        IntPtr PreparsedData,
        IntPtr Report,
        uint ReportLength);

    // Returns the list of button usages currently SET (pressed / on) for
    // the given usage page and link collection. UsageLength is in/out:
    // capacity in, count of set usages out.
    [DllImport("hid.dll")]
    public static extern uint HidP_GetUsages(
        HIDP_REPORT_TYPE ReportType,
        ushort UsagePage,
        ushort LinkCollection,
        [Out] ushort[] UsageList,
        ref uint UsageLength,
        IntPtr PreparsedData,
        IntPtr Report,
        uint ReportLength);
}
