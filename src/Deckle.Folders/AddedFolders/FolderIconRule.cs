using System.Globalization;
using System.Text;
using Deckle.Core;

namespace Deckle.Folders;

// The icon the app gives a folder when it is added (spec, Window: "The app
// picks it when the folder is added"), by a script rule and without a model
// (spec, Behaviours). The person changes it afterwards with the picker.
//
// The rule, in order:
//   1. A word of the folder's name says it: "Photos", "Mes documents",
//      "Projets", "Musique"... (French and English words).
//   2. The folder holds a repository: a .git folder, a solution, a project or
//      a package manifest at its top.
//   3. One kind of file makes at least half of the files at its top: images,
//      documents, music or videos.
//   4. Otherwise the plain folder.
// Only the folder's own top level is read, and at most MaxEntries of it, so
// adding a drive or a huge folder costs one short listing.
public static class FolderIconRule
{
    internal const int MaxEntries = 500;

    private static readonly (IconSymbol Symbol, string[] Words)[] NameWords =
    [
        (IconSymbol.Photos, ["photo", "photos", "image", "images", "picture", "pictures", "dcim", "camera"]),
        (IconSymbol.Documents, ["document", "documents", "docs", "papiers", "factures", "administratif"]),
        (IconSymbol.Code, ["code", "projet", "projets", "project", "projects", "src", "source", "sources", "repos", "dev", "git"]),
        (IconSymbol.Music, ["musique", "musiques", "music", "audio", "sons"]),
        (IconSymbol.Videos, ["video", "videos", "film", "films", "movies", "rushes"]),
        (IconSymbol.Downloads, ["telechargement", "telechargements", "download", "downloads"]),
        (IconSymbol.Archive, ["archive", "archives", "backup", "backups", "sauvegarde", "sauvegardes"]),
        (IconSymbol.Work, ["travail", "work", "boulot", "client", "clients"]),
        (IconSymbol.Home, ["maison", "home", "perso", "personnel"]),
        (IconSymbol.People, ["famille", "family", "amis", "friends"]),
    ];

    private static readonly string[] RepositoryMarkers =
        [".git", "package.json", "pyproject.toml", "Cargo.toml", "go.mod", "pom.xml", "build.gradle"];

    private static readonly string[] RepositoryExtensions = [".sln", ".slnx", ".csproj", ".vcxproj"];

    private static readonly (IconSymbol Symbol, HashSet<string> Extensions)[] FileKinds =
    [
        (IconSymbol.Photos, Set(".jpg", ".jpeg", ".png", ".heic", ".heif", ".gif", ".webp", ".tif", ".tiff", ".bmp",
            ".nef", ".cr2", ".cr3", ".arw", ".raf", ".orf", ".rw2", ".dng")),
        (IconSymbol.Documents, Set(".pdf", ".doc", ".docx", ".odt", ".rtf", ".txt", ".md", ".xls", ".xlsx", ".ods",
            ".ppt", ".pptx", ".odp", ".csv")),
        (IconSymbol.Music, Set(".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".aiff")),
        (IconSymbol.Videos, Set(".mp4", ".mov", ".mkv", ".avi", ".wmv", ".m4v", ".webm", ".mts")),
    ];

    // Reads the folder's top level and applies the rule. A folder that cannot
    // be read is judged on its name alone.
    public static IconSymbol Pick(string folderPath)
    {
        var entries = new List<FolderIconEntry>();
        try
        {
            var info = new DirectoryInfo(folderPath);
            foreach (FileSystemInfo entry in info.EnumerateFileSystemInfos().Take(MaxEntries))
                entries.Add(new FolderIconEntry(entry.Name, entry is DirectoryInfo));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            entries.Clear();
        }
        return Pick(FolderPaths.DisplayName(folderPath), entries);
    }

    internal static IconSymbol Pick(string name, IReadOnlyCollection<FolderIconEntry> entries)
    {
        if (FromName(name) is { } named) return named;

        if (entries.Any(IsRepositoryMarker)) return IconSymbol.Code;

        int files = entries.Count(e => !e.IsFolder);
        if (files == 0) return IconSymbol.Folder;
        foreach ((IconSymbol symbol, HashSet<string> extensions) in FileKinds)
        {
            int ofKind = entries.Count(e => !e.IsFolder && extensions.Contains(System.IO.Path.GetExtension(e.Name)));
            if (ofKind * 2 >= files) return symbol;
        }
        return IconSymbol.Folder;
    }

    private static IconSymbol? FromName(string name)
    {
        string folded = Fold(name);
        var separators = folded.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray();
        string[] words = folded.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        foreach ((IconSymbol symbol, string[] keywords) in NameWords)
            if (words.Any(w => keywords.Contains(w))) return symbol;
        return null;
    }

    private static bool IsRepositoryMarker(FolderIconEntry entry) =>
        RepositoryMarkers.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)
        || (!entry.IsFolder && RepositoryExtensions.Contains(System.IO.Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase));

    // Lower case without accents, so "Vidéos" and "Téléchargements" match.
    private static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (char c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                folded.Append(char.ToLowerInvariant(c));
        return folded.ToString();
    }

    private static HashSet<string> Set(params string[] extensions) => new(extensions, StringComparer.OrdinalIgnoreCase);
}

// One entry at the top of a folder, as the rule sees it.
internal readonly record struct FolderIconEntry(string Name, bool IsFolder);
