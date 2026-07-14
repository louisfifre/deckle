namespace Deckle.Setup;

// Copies a data tree while retaining an exact ledger of artifacts it created.
// Rollback removes that ledger only, never an unrelated file that appeared in
// the target concurrently. Uncopyable diagnostics may be shed; user data may not.
internal sealed class DataRootTree : IDataRootCopier
{
    private readonly List<string> _createdFiles = [];
    private readonly List<string> _temporaryFiles = [];
    private readonly HashSet<string> _createdDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, string, bool>? _copyFile;

    public DataRootTree() { }

    internal DataRootTree(Func<string, string, bool> copyFile) => _copyFile = copyFile;

    public DataRootCopyResult Copy(
        string source,
        string target,
        long totalBytes,
        IProgress<DataRootCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        _createdFiles.Clear();
        _temporaryFiles.Clear();
        _createdDirectories.Clear();

        long copied = 0;
        int files = 0;
        int skipped = 0;
        long lastReport = 0;

        foreach ((string file, string relative, bool reparsePoint) in
                 EnumerateFiles(source, cancellationToken))
        {
            if (reparsePoint)
            {
                if (IsSheddable(relative)) { skipped++; continue; }
                throw new IOException($"Required data entry '{relative}' is a reparse point.");
            }

            string destination = Path.Combine(target, relative);
            EnsureDirectory(Path.GetDirectoryName(destination)!, target);

            bool copiedFile = CopyFile(file, destination) || CopyFile(file, destination);
            if (!copiedFile)
            {
                if (IsSheddable(relative))
                {
                    skipped++;
                    continue;
                }

                throw new IOException($"Could not copy required data file '{relative}'.");
            }

            _createdFiles.Add(destination);
            files++;
            copied += new FileInfo(destination).Length;

            long now = Environment.TickCount64;
            if (now - lastReport >= 200)
            {
                progress?.Report(new DataRootCopyProgress(copied, totalBytes));
                lastReport = now;
            }
        }

        progress?.Report(new DataRootCopyProgress(copied, totalBytes));
        return new DataRootCopyResult(copied, files, skipped);
    }

    public void RollBack(string target, string source)
    {
        if (PathsEqual(target, source)) return;

        var failures = new List<Exception>();

        foreach (string temporary in _temporaryFiles.AsEnumerable().Reverse())
        {
            try { File.Delete(temporary); }
            catch (Exception ex) { failures.Add(ex); }
        }

        foreach (string file in _createdFiles.AsEnumerable().Reverse())
        {
            try { File.Delete(file); }
            catch (Exception ex) { failures.Add(ex); }
        }

        foreach (string directory in _createdDirectories
                     .OrderByDescending(path => path.Length))
        {
            try { Directory.Delete(directory, recursive: false); }
            catch { /* Foreign or still-open content is deliberately spared. */ }
        }

        if (failures.Count > 0)
            throw new AggregateException("The partial relocation target could not be fully cleaned.", failures);
    }

    private void EnsureDirectory(string directory, string target)
    {
        if (Directory.Exists(directory)) return;

        var missing = new Stack<string>();
        string? current = directory;
        while (current is not null && !Directory.Exists(current))
        {
            missing.Push(current);
            if (PathsEqual(current, target)) break;
            current = Path.GetDirectoryName(current);
        }

        while (missing.TryPop(out string? path))
        {
            Directory.CreateDirectory(path);
            _createdDirectories.Add(path);
        }
    }

    private bool CopyFile(string source, string destination) =>
        _copyFile?.Invoke(source, destination) ?? TryCopyFile(source, destination);

    private bool TryCopyFile(string source, string destination)
    {
        string partial = $"{destination}.deckle-{Guid.NewGuid():N}.partial";
        _temporaryFiles.Add(partial);
        try
        {
            File.Copy(source, partial, overwrite: false);
            File.Move(partial, destination, overwrite: false);
            _temporaryFiles.Remove(partial);
            return true;
        }
        catch
        {
            try
            {
                File.Delete(partial);
                _temporaryFiles.Remove(partial);
            }
            catch { }
            return false;
        }
    }

    internal static bool IsSheddable(string relativePath)
    {
        int separator = relativePath.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string firstSegment = separator < 0 ? relativePath : relativePath[..separator];
        return string.Equals(firstSegment, "diagnostics", StringComparison.OrdinalIgnoreCase);
    }

    internal static long MeasureBytes(string source)
    {
        long total = 0;
        foreach ((string file, string relative, bool reparsePoint) in
                 EnumerateFiles(source, CancellationToken.None))
        {
            if (reparsePoint)
            {
                if (IsSheddable(relative)) continue;
                throw new IOException($"Required data entry '{relative}' is a reparse point.");
            }
            try { total += new FileInfo(file).Length; }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return total;
    }

    private static IEnumerable<(string Path, string Relative, bool ReparsePoint)> EnumerateFiles(
        string source,
        CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(source);
        while (directories.TryPop(out string? directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(entry);
                string relative = Path.GetRelativePath(source, entry);
                bool reparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
                if (reparsePoint)
                {
                    yield return (entry, relative, true);
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                    directories.Push(entry);
                else
                    yield return (entry, relative, false);
            }
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
