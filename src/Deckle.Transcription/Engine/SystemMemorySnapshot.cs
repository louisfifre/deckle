using System.Runtime.InteropServices;

namespace Deckle.Transcription;

// The machine's memory at one instant, for the log line of a failure whose
// cause may be memory pressure. Physical is the RAM, commit is what Windows has
// promised to back with RAM or the page file: a GPU backend that cannot
// allocate fails on either, and the log must show which one was short.
// Megabytes, rounded down; all zeros when the query itself failed, which a
// reader tells apart from a real reading by total_phys_mb = 0.
internal readonly record struct SystemMemorySnapshot(
    long AvailablePhysicalMb,
    long TotalPhysicalMb,
    long AvailableCommitMb,
    long CommitLimitMb)
{
    private const long Mb = 1024 * 1024;

    public static SystemMemorySnapshot Read()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return default;

        return new SystemMemorySnapshot(
            (long)(status.AvailPhys / Mb),
            (long)(status.TotalPhys / Mb),
            (long)(status.AvailPageFile / Mb),
            (long)(status.TotalPageFile / Mb));
    }

    // MEMORYSTATUSEX. In this structure "PageFile" is the system commit charge
    // limit and what is left of it, not the page file alone.
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
