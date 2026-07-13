using System.Runtime.InteropServices;

namespace Deckle.Lighting;

// Native shapes and allocation helpers for the Windows DNS-SD API. Kept away
// from discovery orchestration so the asynchronous lifetime rules remain readable.
internal static partial class HueLocalDiscovery
{
    private const int DnsFreeRecordList = 1;

    private static nint Allocate<T>(T value) where T : struct
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        Marshal.StructureToPtr(value, pointer, false);
        return pointer;
    }

    private static void Free(ref nint pointer)
    {
        if (pointer == 0) return;
        Marshal.FreeHGlobal(pointer);
        pointer = 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DnsServiceBrowseCallback(uint status, nint context, nint record);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DnsServiceResolveComplete(uint status, nint context, nint instance);

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceBrowseRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public nint QueryName;
        public nint BrowseCallback;
        public nint QueryContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceResolveRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public nint QueryName;
        public nint ResolveCallback;
        public nint QueryContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceCancel
    {
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsRecord
    {
        public nint Next;
        public nint Name;
        public ushort Type;
        public ushort DataLength;
        public uint Flags;
        public uint Ttl;
        public uint Reserved;
        public nint Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceInstance
    {
        public nint InstanceName;
        public nint HostName;
        public nint Ip4Address;
        public nint Ip6Address;
        public ushort Port;
        public ushort Priority;
        public ushort Weight;
        public uint PropertyCount;
        public nint Keys;
        public nint Values;
        public uint InterfaceIndex;
    }

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceBrowse(nint request, nint cancel);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceBrowseCancel(nint cancel);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceResolve(nint request, nint cancel);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceResolveCancel(nint cancel);

    [DllImport("dnsapi.dll")]
    private static extern void DnsServiceFreeInstance(nint instance);

    [DllImport("dnsapi.dll")]
    private static extern void DnsRecordListFree(nint records, int freeType);
}
