using System;
using System.Collections.Generic;
using System.IO;

namespace Deckle.Diagnostics;

// The names of the two operational log files under diagnostics/. The host
// writes the log, the Logging module writes the trace, and Settings names the
// trace in its consent dialog, so each name is spelled once, here.
public static class DiagnosticsFiles
{
    // Always written: every Warning and Error, every Milestone, the crash
    // details. The file a reader on another machine opens first.
    public const string Log = "log.jsonl";

    // Written only on consent: every admitted operational entry.
    public const string Trace = "trace.jsonl";

    // Archived generations live beside the active file, under this folder,
    // as `<file>.<NNNN>` (JsonlRotationPolicy).
    private const string ArchiveFolder = "archive";

    // The names these files had before v0.69. An install that predates the
    // rename still holds them; RenameLegacyFiles gives them the current name
    // so the history reads as one file.
    private static readonly (string Legacy, string Current)[] _renamed =
    [
        ("journal.jsonl", Log),
        ("app.jsonl", Trace),
    ];

    // Gives each legacy file, active and archived, its current name. A family
    // is renamed only when nothing exists yet under the current name, so a
    // file the new name already owns is never overwritten or interleaved.
    // Runs before any sink opens these files: a sink holds its file open for
    // the whole run. Never throws: the first failure stops the pass and comes
    // back in the result, for the host to report once a sink exists.
    public static LegacyFileRename RenameLegacyFiles(string diagnosticsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsDirectory);

        int renamed = 0;
        string archive = Path.Combine(diagnosticsDirectory, ArchiveFolder);
        foreach ((string legacy, string current) in _renamed)
        {
            string? file = null;
            try
            {
                if (OwnsAnything(diagnosticsDirectory, archive, current)) continue;

                foreach ((string from, string to) in Moves(diagnosticsDirectory, archive, legacy, current))
                {
                    file = System.IO.Path.GetFileName(from);
                    File.Move(from, to);
                    renamed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new LegacyFileRename(renamed, file ?? legacy, ex);
            }
        }
        return new LegacyFileRename(renamed, null, null);
    }

    private static bool OwnsAnything(string directory, string archive, string name) =>
        File.Exists(Path.Combine(directory, name)) || Generations(archive, name).Count > 0;

    // Materialized before the first move, so renaming does not disturb the
    // enumeration it comes from.
    private static List<(string From, string To)> Moves(
        string directory, string archive, string legacy, string current)
    {
        var moves = new List<(string, string)>();
        string active = Path.Combine(directory, legacy);
        if (File.Exists(active)) moves.Add((active, Path.Combine(directory, current)));

        foreach ((string path, string suffix) in Generations(archive, legacy))
            moves.Add((path, Path.Combine(archive, current + suffix)));
        return moves;
    }

    // The archived generations of one file: `<name>.<digits>` and nothing else,
    // since the Windows pattern `<name>.*` also matches looser names.
    private static List<(string Path, string Suffix)> Generations(string archive, string name)
    {
        var found = new List<(string, string)>();
        if (!Directory.Exists(archive)) return found;

        foreach (string path in Directory.EnumerateFiles(archive, name + ".*"))
        {
            string suffix = System.IO.Path.GetFileName(path)[name.Length..];
            if (suffix.Length > 1 && suffix[0] == '.' && IsDigits(suffix.AsSpan(1)))
                found.Add((path, suffix));
        }
        return found;
    }

    private static bool IsDigits(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
            if (c is < '0' or > '9') return false;
        return true;
    }
}

// What RenameLegacyFiles did: how many files took their current name and,
// when it stopped early, the file it stopped on and why.
public sealed record LegacyFileRename(int Renamed, string? FailedFile, Exception? Failure);
