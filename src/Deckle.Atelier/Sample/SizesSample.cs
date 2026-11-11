using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Deckle.Atelier;

// What the sizes tool needs of the sample folder (SampleFolder): folders whose
// weights differ by orders of magnitude, so the bars have lengths to compare,
// a folder two levels deep, so the heaviest files say which sub-folder they lie
// in, and heavy files spread across them. The self-check adds it to the sample
// before it photographs the sizes tool.
//
// The large files are sparse: NTFS records their size and allocates no
// cluster, so a "1,4 Go" video costs nothing on disk and is created at once.
// The sizes the tool shows are logical sizes, the ones File Explorer shows in
// its Size column, so they read as the real thing. Where sparse files are not
// supported, the file is only extended, which NTFS also does without writing.
public static partial class SizesSample
{
    private const long Mo = 1024 * 1024;

    private static readonly DateTime Base = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Local);

    public static void AddTo(string sampleRoot)
    {
        string videos = Folder(sampleRoot, "Vidéos");
        Sparse(videos, "2026-06-14 Mariage.mp4", 1_434 * Mo);
        Sparse(videos, "Anniversaire Léa.mov", 618 * Mo);
        string raw = Folder(videos, "Rushes");
        Sparse(raw, "C0001.MP4", 2_150 * Mo);
        Sparse(raw, "C0002.MP4", 1_820 * Mo);
        Sparse(raw, "C0003.MP4", 96 * Mo);

        string projects = Path.Combine(sampleRoot, "Projets");
        Directory.CreateDirectory(projects);
        Sparse(projects, "Archive-clients.zip", 862 * Mo);
        string mockups = Folder(projects, "Maquettes");
        Sparse(mockups, "Site-atelier.fig", 243 * Mo);
        Sparse(mockups, "Affiche-A2.psd", 418 * Mo);

        string music = Folder(sampleRoot, "Musique");
        Sparse(music, "Concert-2025.flac", 312 * Mo);
        Sparse(music, "Répétition.wav", 64 * Mo);
    }

    private static string Folder(string parent, string name)
    {
        string path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Sparse(string parent, string name, long bytes)
    {
        string path = Path.Combine(parent, name);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            MarkSparse(stream.SafeFileHandle);
            stream.SetLength(bytes);
        }
        File.SetLastWriteTime(path, Base.AddDays(-(int)(bytes % 300)));
    }

    // FSCTL_SET_SPARSE with no input buffer sets the sparse attribute. A
    // failure (a file system without sparse files) leaves an ordinary file.
    private static void MarkSparse(SafeFileHandle handle) =>
        _ = DeviceIoControl(handle, FsctlSetSparse, 0, 0, 0, 0, out _, 0);

    private const uint FsctlSetSparse = 0x000900C4;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code,
        nint inBuffer, uint inSize, nint outBuffer, uint outSize, out uint returned, nint overlapped);
}
