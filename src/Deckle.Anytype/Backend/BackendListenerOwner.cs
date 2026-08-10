using System.Net;
using System.Runtime.InteropServices;

namespace Deckle.Anytype;

internal enum BackendListenerState
{
    Unbound,
    Owned,
    Ambiguous,
    Failed,
}

internal sealed record BackendListenerSnapshot(
    BackendListenerState State,
    int ProcessId = 0,
    string? Error = null);

internal interface IBackendListenerOwner
{
    BackendListenerSnapshot Inspect();
}

// Positive identity for the fixed Anytype REST endpoint. A health response says
// what is serving; the TCP owner table says which process performed the bind.
internal sealed class BackendListenerOwner(int port = 31012) : IBackendListenerOwner
{
    private const int AddressFamilyInet = 2;
    private const int OwnerPidListenerTable = 3;
    private const uint NoError = 0;
    private const uint ErrorInsufficientBuffer = 122;
    private static readonly uint LoopbackAddress =
        BitConverter.ToUInt32(IPAddress.Loopback.GetAddressBytes());

    public BackendListenerSnapshot Inspect()
    {
        uint size = 0;
        uint result = GetExtendedTcpTable(
            IntPtr.Zero, ref size, false, AddressFamilyInet, OwnerPidListenerTable, 0);
        if (result is not ErrorInsufficientBuffer and not NoError)
            return Failed(result);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                result = GetExtendedTcpTable(
                    buffer, ref size, false, AddressFamilyInet, OwnerPidListenerTable, 0);
                if (result == ErrorInsufficientBuffer) continue;
                if (result != NoError) return Failed(result);

                int count = Marshal.ReadInt32(buffer);
                int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
                var owners = new HashSet<int>();
                for (int index = 0; index < count; index++)
                {
                    IntPtr rowAddress = IntPtr.Add(buffer, sizeof(uint) + index * rowSize);
                    MibTcpRowOwnerPid row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowAddress);
                    int localPort = unchecked((ushort)IPAddress.NetworkToHostOrder((short)row.LocalPort));
                    if (row.LocalAddress == LoopbackAddress && localPort == port)
                        owners.Add(checked((int)row.OwningProcessId));
                }

                return owners.Count switch
                {
                    0 => new(BackendListenerState.Unbound),
                    1 => new(BackendListenerState.Owned, owners.Single()),
                    _ => new(BackendListenerState.Ambiguous,
                        Error: $"{owners.Count} processes own the listener"),
                };
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return new(BackendListenerState.Failed, Error: "TCP table changed repeatedly");
    }

    private static BackendListenerSnapshot Failed(uint error) =>
        new(BackendListenerState.Failed, Error: $"GetExtendedTcpTable returned {error}");

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningProcessId;
    }

    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        int tableClass,
        uint reserved);
}
