using System;
using System.Runtime.InteropServices;

namespace Deckle.Transcription.Whisper;

// Reads the video memory budget of the GPU whisper's Vulkan backend will use,
// through IDXGIAdapter3::QueryVideoMemoryInfo. Raw vtable calls, like the
// DXGI interop of Deckle.Vision: no [ComImport] interface to clash with the
// CsWinRT projection.
//
// Vulkan names devices by its own index and DXGI by adapter order; whisper
// takes Vulkan0, which on a desktop with one discrete card is that card. The
// probe therefore reads the hardware adapter with the most dedicated memory.
// A machine with two discrete GPUs could read the wrong one: not handled.
internal static class VideoMemoryProbe
{
    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IID_IDXGIAdapter3 = new("645967a4-1392-4310-a798-8053ce3e93fd");

    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
    private const int DXGI_MEMORY_SEGMENT_GROUP_LOCAL = 0;
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1([In] in Guid riid, out nint factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct DXGI_ADAPTER_DESC1
    {
        public fixed char Description[128];
        public uint  VendorId;
        public uint  DeviceId;
        public uint  SubSysId;
        public uint  Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint  AdapterLuidLowPart;
        public int   AdapterLuidHighPart;
        public uint  Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_QUERY_VIDEO_MEMORY_INFO
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }

    // Null when no hardware adapter answers; the error says why.
    public static VideoMemoryReading? TryRead(out string error)
    {
        error = "";
        nint factory = 0;
        nint best = 0;
        string bestName = "";
        try
        {
            int hr = CreateDXGIFactory1(in IID_IDXGIFactory1, out factory);
            if (hr != 0) { error = $"CreateDXGIFactory1 hr=0x{hr:X8}"; return null; }

            unsafe
            {
                // IDXGIFactory1::EnumAdapters1 is slot 12: IUnknown 3,
                // IDXGIObject 4, IDXGIFactory 5.
                var enumAdapters1 = (delegate* unmanaged<nint, uint, nint*, int>)(*(nint**)factory)[12];
                ulong bestDedicated = 0;

                for (uint i = 0; i < 16; i++)
                {
                    nint adapter;
                    hr = enumAdapters1(factory, i, &adapter);
                    if (hr == DXGI_ERROR_NOT_FOUND || adapter == 0) break;
                    if (hr != 0) { error = $"EnumAdapters1 hr=0x{hr:X8}"; break; }

                    // IDXGIAdapter1::GetDesc1 is slot 10: IDXGIAdapter ends at 9.
                    var getDesc1 = (delegate* unmanaged<nint, DXGI_ADAPTER_DESC1*, int>)(*(nint**)adapter)[10];
                    DXGI_ADAPTER_DESC1 desc;
                    bool keep = getDesc1(adapter, &desc) == 0
                        && (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) == 0
                        && desc.DedicatedVideoMemory > bestDedicated;

                    if (keep)
                    {
                        if (best != 0) Marshal.Release(best);
                        best = adapter;
                        bestDedicated = desc.DedicatedVideoMemory;
                        bestName = new string(desc.Description);
                    }
                    else
                    {
                        Marshal.Release(adapter);
                    }
                }

                if (best == 0)
                {
                    if (error.Length == 0) error = "no_hardware_adapter";
                    return null;
                }

                hr = Marshal.QueryInterface(best, in IID_IDXGIAdapter3, out nint adapter3);
                if (hr != 0) { error = $"IDXGIAdapter3 hr=0x{hr:X8}"; return null; }
                try
                {
                    // IDXGIAdapter3::QueryVideoMemoryInfo is slot 14: GetDesc2
                    // closes IDXGIAdapter2 at 11, then two content-protection
                    // methods.
                    var query = (delegate* unmanaged<nint, uint, int, DXGI_QUERY_VIDEO_MEMORY_INFO*, int>)(*(nint**)adapter3)[14];
                    DXGI_QUERY_VIDEO_MEMORY_INFO info;
                    hr = query(adapter3, 0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL, &info);
                    if (hr != 0) { error = $"QueryVideoMemoryInfo hr=0x{hr:X8}"; return null; }

                    return new VideoMemoryReading(
                        bestName.Length == 0 ? "unknown" : bestName,
                        (long)Math.Min(info.Budget, long.MaxValue),
                        (long)Math.Min(info.CurrentUsage, long.MaxValue));
                }
                finally
                {
                    Marshal.Release(adapter3);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            error = ex.GetType().Name;
            return null;
        }
        finally
        {
            if (best != 0) Marshal.Release(best);
            if (factory != 0) Marshal.Release(factory);
        }
    }
}
