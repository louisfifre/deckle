namespace Deckle.Atelier;

public sealed record SizeScanOptions
{
    // Workers reading folders at once. 1 reads the tree in one thread. The
    // default comes from the measure written in the shared notes of the build
    // (C:\Windows and C:\Program Files on an NVMe disk, cold and warm):
    // parallel reading beats sequential, and the gain flattens past a few
    // workers while the disk's queue and the CPU stay shared with the app.
    public int Workers { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 6);

    // How many heaviest files a report lists. The panel shows six (12.7).
    public int HeaviestCount { get; init; } = 6;

    // How many levels beneath the place keep their sub-folders and heaviest
    // files after the scan, so moving down into them re-roots the report
    // without reading the disk again (SizeReport.ForFolder). Three clicks down
    // covers drilling into where the bytes are; deeper levels are summed into
    // their ancestor and let go, so memory follows the top of the tree rather
    // than every folder of a disk.
    public int NavigableDepth { get; init; } = 3;

    // Bytes asked of the file system per directory query. .NET's default is
    // 4 KiB; a larger buffer returns more entries per call into the kernel.
    public int BufferSize { get; init; } = 64 * 1024;
}
