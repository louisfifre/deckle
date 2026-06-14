using System.Runtime.InteropServices;
using Deckle.Input;

namespace Deckle.Input;

// Decodes the RAWHID payload of a WM_INPUT into TouchpadReports through
// the hid.dll preparsed-data parser. One parser instance per device,
// created once and cached by the host: the preparsed data blob and the
// caps discovery (which link collections carry which usages) are paid at
// construction, never per frame — the references re-fetch both on every
// WM_INPUT, a needless cost at report cadence.
//
// Parsing approach follows emoacht/RawInput.Touchpad (MIT), extended
// with what the references leave out and the recognizer depends on:
//   • tip switch (0x0D/0x42) and confidence (0x0D/0x47) read per contact
//     via HidP_GetUsages — they are HID buttons, invisible to the
//     value-caps path the references use;
//   • the device button (0x09 page, link collection 0 — the trackpad's
//     mechanical click);
//   • every report of a multi-report WM_INPUT decoded separately
//     (dwCount > 1), each against its own dwSizeHid-byte window.
//
// Confined to the input thread — scratch buffers are reused across calls
// without locking.
public sealed class TouchpadParser : IDisposable
{
    private IntPtr _preparsed;
    private readonly ushort[] _contactCollections;
    private readonly bool _hasButton;
    private readonly ushort[] _usageScratch = new ushort[16];

    public TouchpadCapabilities Capabilities { get; }

    private TouchpadParser(
        IntPtr preparsed,
        TouchpadCapabilities capabilities,
        ushort[] contactCollections,
        bool hasButton)
    {
        _preparsed = preparsed;
        Capabilities = capabilities;
        _contactCollections = contactCollections;
        _hasButton = hasButton;
    }

    /// <summary>
    /// Builds a parser for the given raw input device. Returns null when
    /// the device is not a usable Precision Touchpad collection — the
    /// failure reason lands in <paramref name="failure"/> for the caller
    /// to log.
    /// </summary>
    public static TouchpadParser? TryCreate(IntPtr hDevice, out string? failure)
    {
        failure = null;

        // Preparsed data — owned copy, freed in Dispose.
        uint preparsedSize = 0;
        if (RawInputInterop.GetRawInputDeviceInfo(
                hDevice, RawInputInterop.RIDI_PREPARSEDDATA, IntPtr.Zero, ref preparsedSize) != 0
            || preparsedSize == 0)
        {
            failure = "preparsed data size query failed";
            return null;
        }

        IntPtr preparsed = Marshal.AllocHGlobal((int)preparsedSize);
        try
        {
            if (RawInputInterop.GetRawInputDeviceInfo(
                    hDevice, RawInputInterop.RIDI_PREPARSEDDATA, preparsed, ref preparsedSize) != preparsedSize)
            {
                failure = "preparsed data read failed";
                return Fail(ref preparsed);
            }

            if (HidInterop.HidP_GetCaps(preparsed, out var caps) != HidInterop.HIDP_STATUS_SUCCESS)
            {
                failure = "HidP_GetCaps failed";
                return Fail(ref preparsed);
            }

            if (caps.UsagePage != RawInputInterop.UsagePageDigitizer ||
                caps.Usage != RawInputInterop.UsageTouchpad)
            {
                failure = $"not a touchpad collection (page=0x{caps.UsagePage:X2} usage=0x{caps.Usage:X2})";
                return Fail(ref preparsed);
            }

            // Value caps → which link collections carry a Contact ID, and
            // the X/Y logical ranges (identical across contact collections
            // per the PTP spec; the first seen wins).
            ushort valueCapsLength = caps.NumberInputValueCaps;
            var valueCaps = new HidInterop.HIDP_VALUE_CAPS[valueCapsLength];
            if (HidInterop.HidP_GetValueCaps(
                    HidInterop.HIDP_REPORT_TYPE.HidP_Input, valueCaps, ref valueCapsLength, preparsed)
                != HidInterop.HIDP_STATUS_SUCCESS)
            {
                failure = "HidP_GetValueCaps failed";
                return Fail(ref preparsed);
            }

            var contactCollections = new SortedSet<ushort>();
            int xMin = 0, xMax = 0, yMin = 0, yMax = 0;
            bool sawX = false, sawY = false;

            for (int i = 0; i < valueCapsLength; i++)
            {
                ref readonly var cap = ref valueCaps[i];
                if (cap.LinkCollection == 0) continue;

                switch (cap.UsagePage, cap.Usage)
                {
                    case (0x0D, 0x51): // Contact ID
                        contactCollections.Add(cap.LinkCollection);
                        break;
                    case (0x01, 0x30) when !sawX: // X (Generic Desktop)
                        xMin = cap.LogicalMin; xMax = cap.LogicalMax; sawX = true;
                        break;
                    case (0x01, 0x31) when !sawY: // Y
                        yMin = cap.LogicalMin; yMax = cap.LogicalMax; sawY = true;
                        break;
                }
            }

            if (contactCollections.Count == 0)
            {
                failure = "no contact link collection in the report layout";
                return Fail(ref preparsed);
            }

            // Button caps → mechanical click on the report-level collection.
            bool hasButton = false;
            ushort buttonCapsLength = caps.NumberInputButtonCaps;
            if (buttonCapsLength > 0)
            {
                var buttonCaps = new HidInterop.HIDP_BUTTON_CAPS[buttonCapsLength];
                if (HidInterop.HidP_GetButtonCaps(
                        HidInterop.HIDP_REPORT_TYPE.HidP_Input, buttonCaps, ref buttonCapsLength, preparsed)
                    == HidInterop.HIDP_STATUS_SUCCESS)
                {
                    for (int i = 0; i < buttonCapsLength; i++)
                        if (buttonCaps[i].LinkCollection == 0 && buttonCaps[i].UsagePage == 0x09)
                            hasButton = true;
                }
            }

            var capabilities = new TouchpadCapabilities(
                DeviceName:       ReadDeviceName(hDevice),
                VendorId:         ReadVendorProduct(hDevice, out uint productId),
                ProductId:        productId,
                XMin: xMin, XMax: xMax, YMin: yMin, YMax: yMax,
                ContactSlots:     contactCollections.Count,
                ReportByteLength: caps.InputReportByteLength);

            var parser = new TouchpadParser(
                preparsed, capabilities, contactCollections.ToArray(), hasButton);
            preparsed = IntPtr.Zero; // ownership transferred
            return parser;
        }
        finally
        {
            if (preparsed != IntPtr.Zero) Marshal.FreeHGlobal(preparsed);
        }

        static TouchpadParser? Fail(ref IntPtr preparsed)
        {
            Marshal.FreeHGlobal(preparsed);
            preparsed = IntPtr.Zero;
            return null;
        }
    }

    /// <summary>
    /// Decodes the HID payload of one WM_INPUT — <paramref name="count"/>
    /// reports of <paramref name="sizeHid"/> bytes, back to back in
    /// <paramref name="data"/> starting at <paramref name="offset"/>.
    /// Returns one TouchpadReport per decoded report, in device order.
    /// </summary>
    public unsafe TouchpadReport[] Parse(byte[] data, int offset, int sizeHid, int count)
    {
        var reports = new TouchpadReport[count];

        fixed (byte* basePtr = data)
        {
            for (int r = 0; r < count; r++)
            {
                IntPtr report = (IntPtr)(basePtr + offset + (long)r * sizeHid);
                reports[r] = ParseSingle(report, (uint)sizeHid);
            }
        }

        return reports;
    }

    private TouchpadReport ParseSingle(IntPtr report, uint reportLength)
    {
        // Report-level usages (link collection 0).
        uint scanTime = 0, contactCount = 0;
        HidInterop.HidP_GetUsageValue(
            HidInterop.HIDP_REPORT_TYPE.HidP_Input, 0x0D, 0, 0x56,
            out scanTime, _preparsed, report, reportLength);
        HidInterop.HidP_GetUsageValue(
            HidInterop.HIDP_REPORT_TYPE.HidP_Input, 0x0D, 0, 0x54,
            out contactCount, _preparsed, report, reportLength);

        bool buttonDown = _hasButton && AnyUsageSet(0x09, 0, report, reportLength);

        var contacts = new List<TouchpadContact>(_contactCollections.Length);
        foreach (ushort collection in _contactCollections)
        {
            bool hasId = HidInterop.HidP_GetUsageValue(
                HidInterop.HIDP_REPORT_TYPE.HidP_Input, 0x0D, collection, 0x51,
                out uint id, _preparsed, report, reportLength) == HidInterop.HIDP_STATUS_SUCCESS;
            bool hasX = HidInterop.HidP_GetUsageValue(
                HidInterop.HIDP_REPORT_TYPE.HidP_Input, 0x01, collection, 0x30,
                out uint x, _preparsed, report, reportLength) == HidInterop.HIDP_STATUS_SUCCESS;
            bool hasY = HidInterop.HidP_GetUsageValue(
                HidInterop.HIDP_REPORT_TYPE.HidP_Input, 0x01, collection, 0x31,
                out uint y, _preparsed, report, reportLength) == HidInterop.HIDP_STATUS_SUCCESS;

            if (!hasId || !hasX || !hasY) continue;

            (bool tip, bool confidence) = ReadContactBits(collection, report, reportLength);
            contacts.Add(new TouchpadContact((int)id, (int)x, (int)y, tip, confidence));
        }

        return new TouchpadReport(scanTime, (int)contactCount, buttonDown, contacts.ToArray());
    }

    private (bool tip, bool confidence) ReadContactBits(ushort collection, IntPtr report, uint reportLength)
    {
        uint usageCount = (uint)_usageScratch.Length;
        uint status = HidInterop.HidP_GetUsages(
            HidInterop.HIDP_REPORT_TYPE.HidP_Input, 0x0D, collection,
            _usageScratch, ref usageCount, _preparsed, report, reportLength);
        if (status != HidInterop.HIDP_STATUS_SUCCESS) return (false, false);

        bool tip = false, confidence = false;
        for (int i = 0; i < usageCount; i++)
        {
            if (_usageScratch[i] == 0x42) tip = true;
            else if (_usageScratch[i] == 0x47) confidence = true;
        }
        return (tip, confidence);
    }

    private bool AnyUsageSet(ushort usagePage, ushort collection, IntPtr report, uint reportLength)
    {
        uint usageCount = (uint)_usageScratch.Length;
        uint status = HidInterop.HidP_GetUsages(
            HidInterop.HIDP_REPORT_TYPE.HidP_Input, usagePage, collection,
            _usageScratch, ref usageCount, _preparsed, report, reportLength);
        return status == HidInterop.HIDP_STATUS_SUCCESS && usageCount > 0;
    }

    private static string ReadDeviceName(IntPtr hDevice)
    {
        uint chars = 0;
        if (RawInputInterop.GetRawInputDeviceInfo(
                hDevice, RawInputInterop.RIDI_DEVICENAME, IntPtr.Zero, ref chars) != 0 || chars == 0)
            return "(unknown)";

        IntPtr buffer = Marshal.AllocHGlobal((int)chars * sizeof(char));
        try
        {
            uint written = RawInputInterop.GetRawInputDeviceInfo(
                hDevice, RawInputInterop.RIDI_DEVICENAME, buffer, ref chars);
            if (written == unchecked((uint)-1) || written == 0) return "(unknown)";
            return Marshal.PtrToStringUni(buffer) ?? "(unknown)";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static uint ReadVendorProduct(IntPtr hDevice, out uint productId)
    {
        productId = 0;
        var info = new RawInputInterop.RID_DEVICE_INFO
        {
            cbSize = (uint)Marshal.SizeOf<RawInputInterop.RID_DEVICE_INFO>(),
        };
        uint size = info.cbSize;
        if (RawInputInterop.GetRawInputDeviceInfo(
                hDevice, RawInputInterop.RIDI_DEVICEINFO, ref info, ref size) == unchecked((uint)-1))
            return 0;

        productId = info.hid.dwProductId;
        return info.hid.dwVendorId;
    }

    public void Dispose()
    {
        if (_preparsed != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_preparsed);
            _preparsed = IntPtr.Zero;
        }
    }
}
