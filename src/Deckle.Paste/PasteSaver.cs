using System.Diagnostics;
using System.Text;

namespace Deckle.Paste;

// Turns what a paste holds into files in the chosen folder. Text becomes a
// UTF-8 .txt, an image the .png it already is, copied files are copied under
// their own names. Nothing is ever overwritten (PasteNames).
//
// Save never throws for a failure the person can meet: no folder, a folder
// gone, a copied file gone, a disk that refuses the write. Each comes back as
// a result with its reason, and the window shows it. The log says how the
// save ended, never what the paste held: no text, no bytes, no path.
public sealed class PasteSaver
{
    // UTF-8 without a byte-order mark, the encoding every editor reads.
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Func<DateTimeOffset> _clock;

    public PasteSaver(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public PasteSaveResult Save(PasteContent content, string? folder)
    {
        ArgumentNullException.ThrowIfNull(content);
        PasteKind kind = KindOf(content);

        if (string.IsNullOrWhiteSpace(folder))
            return Fail(kind, PasteFailureReason.NoFolder);
        if (!Directory.Exists(folder))
            return Fail(kind, PasteFailureReason.FolderMissing);

        var watch = Stopwatch.StartNew();
        return content switch
        {
            PastedText text => SaveText(text, folder, watch),
            PastedImage image => SaveImage(image, folder, watch),
            PastedFiles files => CopyFiles(files, folder, watch),
            _ => throw new ArgumentOutOfRangeException(nameof(content), content.GetType().Name, "Unknown paste content."),
        };
    }

    private PasteSaveResult SaveText(PastedText text, string folder, Stopwatch watch)
    {
        if (string.IsNullOrEmpty(text.Text))
            return Fail(PasteKind.Text, PasteFailureReason.NothingToSave);
        return WriteNew(PasteKind.Text, folder, PasteNames.Generated(_clock(), ".txt"), Utf8.GetBytes(text.Text), watch);
    }

    private PasteSaveResult SaveImage(PastedImage image, string folder, Stopwatch watch)
    {
        if (image.PngBytes.IsEmpty)
            return Fail(PasteKind.Image, PasteFailureReason.NothingToSave);
        return WriteNew(PasteKind.Image, folder, PasteNames.Generated(_clock(), ".png"), image.PngBytes, watch);
    }

    // Writes the bytes under the first free name. FileMode.CreateNew refuses a
    // name taken since the check, and the next candidate is tried. Opening
    // and writing are apart: a write that fails part way (a full disk) is a
    // failure, and its incomplete file is removed rather than left behind.
    private static PasteSaveResult WriteNew(PasteKind kind, string folder, string name, ReadOnlyMemory<byte> bytes, Stopwatch watch)
    {
        string? written = null;
        try
        {
            FileStream? stream = null;
            foreach (string candidate in PasteNames.Candidates(folder, name))
            {
                if (PasteNames.IsTaken(candidate)) continue;
                try
                {
                    stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
                catch (IOException) when (PasteNames.IsTaken(candidate))
                {
                    continue;
                }
                written = candidate;
                break;
            }

            using (stream!)
                stream!.Write(bytes.Span);

            PasteSaveLog.Saved(kind, items: 1, bytes.Length, watch);
            return PasteSaveResult.Saved([written!]);
        }
        catch (Exception ex) when (IsWriteFailure(ex))
        {
            if (written is not null)
            {
                try { File.Delete(written); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return Fail(kind, PasteFailureReason.WriteFailed, ex);
        }
    }

    private static PasteSaveResult CopyFiles(PastedFiles files, string folder, Stopwatch watch)
    {
        if (files.Paths.Count == 0)
            return Fail(PasteKind.Files, PasteFailureReason.NothingToSave);

        var saved = new List<string>(files.Paths.Count);
        long bytes = 0;
        foreach (string source in files.Paths)
        {
            bool isFile = File.Exists(source);
            if (!isFile && !Directory.Exists(source))
                return Fail(PasteKind.Files, PasteFailureReason.SourceMissing, savedSoFar: saved);

            try
            {
                string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
                string target = isFile ? CopyFile(source, folder, name) : CopyFolder(source, folder, name);
                saved.Add(target);
                bytes += isFile ? new FileInfo(target).Length : 0;
            }
            catch (Exception ex) when (IsWriteFailure(ex))
            {
                return Fail(PasteKind.Files, PasteFailureReason.WriteFailed, ex, saved);
            }
        }

        PasteSaveLog.Saved(PasteKind.Files, saved.Count, bytes, watch);
        return PasteSaveResult.Saved(saved);
    }

    // File.Copy without overwrite refuses a name taken since the check.
    private static string CopyFile(string source, string folder, string name)
    {
        foreach (string candidate in PasteNames.Candidates(folder, name))
        {
            if (PasteNames.IsTaken(candidate)) continue;
            try
            {
                File.Copy(source, candidate, overwrite: false);
                return candidate;
            }
            catch (IOException) when (PasteNames.IsTaken(candidate) && File.Exists(source)) { }
        }
        throw new UnreachableException();
    }

    // A copied folder is copied whole, under its own name. A folder pasted
    // into itself (or into one of its own folders) is refused: the copy
    // would never end. A drive root is refused too: it has no name of its
    // own, and copying a whole drive is not what a paste is for.
    //
    // The copy is built in a hidden staging folder beside its target, then
    // renamed: Directory.Move refuses a name taken since the check, where
    // Directory.CreateDirectory would silently merge into it, and a copy that
    // fails part way is removed whole instead of left half-copied.
    private static string CopyFolder(string source, string folder, string name)
    {
        if (name.Length == 0)
            throw new IOException("A drive root cannot be copied as a folder.");
        if (IsSameOrInside(source, folder))
            throw new IOException("A folder cannot be copied into itself.");

        string fullSource = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        string staging = Path.Combine(folder, $".{name}.{Guid.NewGuid():N}.partial");
        try
        {
            Directory.CreateDirectory(staging);
            File.SetAttributes(staging, File.GetAttributes(staging) | FileAttributes.Hidden);
            foreach (string dir in Directory.EnumerateDirectories(fullSource, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(staging, Path.GetRelativePath(fullSource, dir)));
            foreach (string file in Directory.EnumerateFiles(fullSource, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(staging, Path.GetRelativePath(fullSource, file)), overwrite: false);

            foreach (string candidate in PasteNames.Candidates(folder, name))
            {
                if (PasteNames.IsTaken(candidate)) continue;
                try
                {
                    Directory.Move(staging, candidate);
                }
                catch (IOException) when (PasteNames.IsTaken(candidate))
                {
                    continue;
                }
                File.SetAttributes(candidate, File.GetAttributes(candidate) & ~FileAttributes.Hidden);
                return candidate;
            }
            throw new UnreachableException();
        }
        catch when (Directory.Exists(staging))
        {
            try { Directory.Delete(staging, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    // Whether target is source itself or lies inside it, by full path. Both
    // end with a separator before the comparison, so a drive root ("D:\")
    // contains its folders and "C:\a" does not contain "C:\ab".
    internal static bool IsSameOrInside(string source, string target)
    {
        static string WithSeparator(string path)
        {
            string full = Path.GetFullPath(path);
            return Path.EndsInDirectorySeparator(full) ? full : full + Path.DirectorySeparatorChar;
        }
        return WithSeparator(target).StartsWith(WithSeparator(source), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWriteFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
            or System.Security.SecurityException;

    private static PasteKind KindOf(PasteContent content) => content switch
    {
        PastedText => PasteKind.Text,
        PastedImage => PasteKind.Image,
        _ => PasteKind.Files,
    };

    private static PasteSaveResult Fail(PasteKind kind, PasteFailureReason reason, Exception? ex = null, IReadOnlyList<string>? savedSoFar = null)
    {
        PasteSaveLog.Failed(kind, reason, ex, savedSoFar?.Count ?? 0);
        return PasteSaveResult.Failed(PasteSaveErrors.For(reason), savedSoFar is null ? null : [.. savedSoFar]);
    }
}
