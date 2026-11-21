using System;

namespace Deckle.Core;

// ── AppIdentity ────────────────────────────────────────────────────────────
//
// Which executable of the suite this process is. The published Deckle is the
// suite's own app, identity "deckle"; every other executable built on the core
// libraries (a module that ships on its own) declares its own before anything
// reads AppPaths.
//
// The identity decides three things and nothing else:
//   • the data root: the suite root for "deckle" (as always), and
//     <suite root>\apps\<id> for any other, so a side app never touches the
//     data of the published Deckle;
//   • the diagnostics folder: the suite's shared <suite root>\diagnostics for
//     "deckle", and one subfolder of it per other app, because the log sinks
//     write fixed file names with one writer per process;
//   • the resident mutex, so two apps run side by side while a second copy of
//     the same app is refused.
//
// The identity is read once by AppPaths, whose values never change after that:
// Use throws once it is too late rather than let two roots coexist.
public sealed record AppIdentity
{
    public const string SuiteAppId = "deckle";

    // The published Deckle. Also what a process that never declares one is.
    public static AppIdentity Suite { get; } = new(SuiteAppId);

    private static readonly object Gate = new();
    private static AppIdentity _current = Suite;
    private static bool _read;

    private AppIdentity(string id) => Id = id;

    // Lowercase ASCII, digits and hyphens: it names a folder and a mutex.
    public string Id { get; }

    public bool IsSuiteApp => Id == SuiteAppId;

    // Local\Deckle.Resident for the suite's own app (the name the published
    // Deckle has always used), Local\Deckle.<Id>.Resident for the others.
    public string ResidentMutexName => IsSuiteApp
        ? @"Local\Deckle.Resident"
        : $@"Local\Deckle.{Pascal(Id)}.Resident";

    public static AppIdentity Create(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An app id must not be empty.", nameof(id));
        foreach (char c in id)
        {
            bool ok = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-';
            if (!ok)
                throw new ArgumentException(
                    "An app id is lowercase ASCII letters, digits and hyphens: it names a folder and a mutex.", nameof(id));
        }
        if (id[0] == '-' || id[^1] == '-')
            throw new ArgumentException("An app id does not start or end with a hyphen.", nameof(id));
        return id == SuiteAppId ? Suite : new AppIdentity(id);
    }

    // The identity of this process; fixes it for good.
    public static AppIdentity Current
    {
        get
        {
            lock (Gate)
            {
                _read = true;
                return _current;
            }
        }
    }

    // Declares this process's identity. Called first in the executable's entry
    // point, before AppPaths or any module touches the disk.
    public static void Use(AppIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (Gate)
        {
            if (_read && !ReferenceEquals(_current, identity))
                throw new InvalidOperationException(
                    $"The app identity is already fixed as '{_current.Id}': declare '{identity.Id}' before AppPaths is read.");
            _current = identity;
        }
    }

    // "mail" -> "Mail", "folder-map" -> "FolderMap".
    private static string Pascal(string id)
    {
        var parts = id.Split('-', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
            parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i][1..];
        return string.Concat(parts);
    }
}
