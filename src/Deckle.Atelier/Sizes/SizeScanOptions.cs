namespace Deckle.Atelier;

public sealed record SizeScanOptions
{
    // Workers reading folders at once; 1 reads the tree in one thread.
    // Measured on this project's machine (16 logical processors, NVMe), warm
    // cache, median of three: C:\Windows (101 000 folders) took 7.9 s with 1
    // worker, 3.2 s with 4, 2.4 s with 8 and 2.0 s with 12; C:\Program Files
    // 424 ms, 121 ms, 64 ms, 49 ms. Past 8 the gain shrinks while the scan takes
    // more of a machine the app shares with its other modules, so half the
    // processors, at most 8.
    public int Workers { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

    // How many heaviest files a report lists. The panel shows six (12.7).
    public int HeaviestCount { get; init; } = 6;

    // How many levels beneath the place keep their sub-folders and heaviest
    // files after the scan, so moving down into them re-roots the report
    // without reading the disk again (SizeReport.ForFolder). Deeper levels are
    // summed into their ancestor and let go.
    //
    // Why 1: the move the Sizes list invites is a click on one of its rows, one
    // level down, and that one is free. Each further level multiplies what a
    // report holds on a wide tree: on C:\Windows (101 000 folders, 90 000 of
    // them two levels down in WinSxS) a report holds 0.7 MB at depth 0, 10 MB
    // at 1, 37 MB at 2 and 42 MB at 3. A move two levels down rescans a
    // sub-tree, which the first scan has just brought into the file system's
    // cache.
    public int NavigableDepth { get; init; } = 1;

    // Bytes asked of the file system per directory query; 0 keeps .NET's
    // default (4 KiB). Measured on C:\Windows and C:\Program Files, warm, with
    // 6 workers: 64 KiB was no faster than the default, so the default stays.
    public int BufferSize { get; init; }
}
