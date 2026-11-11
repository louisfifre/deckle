namespace Deckle.Atelier;

// A small folder tree made from nothing, for the self-check to open the
// Atelier on: a few folders, a few files of different sizes, fixed dates so
// two runs photograph the same thing. Nothing comes from the person's disk;
// the caller places it under a data root of its own.
public static class SampleFolder
{
    public const string Name = "Exemple";

    private static readonly DateTime Base = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Local);

    // Builds <parent>\Exemple, replacing what a previous run left, and returns
    // its path.
    public static string Create(string parent)
    {
        string root = System.IO.Path.Combine(parent, Name);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);

        // Writing into a folder moves its date, so folder dates are set last,
        // deepest first.
        var folderDates = new List<(string Path, DateTime Date)>();
        string Folder(string parentPath, string name, int days)
        {
            string path = System.IO.Path.Combine(parentPath, name);
            Directory.CreateDirectory(path);
            folderDates.Add((path, Base.AddDays(days)));
            return path;
        }

        Directory.CreateDirectory(root);
        string documents = Folder(root, "Documents", -3);
        string notes = Folder(documents, "Notes", -8);
        Text(notes, "Recette-pain.md", "# Pain\n\nFarine, eau, sel, levain.\n", -8);
        Text(notes, "Voyage-printemps.md", "# Voyage\n\nItinéraire à préparer.\n", -40);
        Sized(documents, "Contrat-location.pdf", 412 * 1024, -200);
        Sized(documents, "Mode-emploi-four.pdf", 2_500_000, -38);

        string photos = Folder(root, "Photos", -12);
        string y2026 = Folder(photos, "2026", -12);
        Folder(y2026, "2026-06-14 Mariage", -140);
        Folder(y2026, "2026-06-21 Atelier poterie", -133);
        Folder(photos, "2025", -320);

        Folder(root, "Projets", -1);

        Text(root, "Lisez-moi.md", "Dossier d'exemple créé par le self-check de Deckle.\n", -2);
        Sized(root, "Tarifs-2026.pdf", 120 * 1024, -298);
        Sized(root, "notes-2023.zip", 3_200_000, -680);

        foreach ((string path, DateTime date) in folderDates.OrderByDescending(f => f.Path.Length))
            Directory.SetLastWriteTime(path, date);
        Directory.SetLastWriteTime(root, Base);
        return root;
    }

    private static void Text(string parent, string name, string content, int days)
    {
        string path = System.IO.Path.Combine(parent, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, Base.AddDays(days));
    }

    // The bytes are zeroes: only the size matters to the list.
    private static void Sized(string parent, string name, long bytes, int days)
    {
        string path = System.IO.Path.Combine(parent, name);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            stream.SetLength(bytes);
        File.SetLastWriteTime(path, Base.AddDays(days));
    }
}
