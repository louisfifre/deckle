namespace Deckle.Folders;

// A small folder tree made from nothing, for the self-check to open
// Deckle Folders on: a few folders, a few files of different sizes, fixed dates so
// two runs photograph the same thing. Nothing comes from the person's disk;
// the caller places it under a data root of its own.
public static class SampleFolder
{
    public const string Name = "Exemple";

    // A folder with nothing in it, for the file tool's "Ce dossier est vide."
    public const string EmptyFolderName = "Brouillons";

    // A name far longer than any column, for the ellipsis and its tooltip in
    // each display and the panel's title. Kept near 110 characters so the
    // full path stays under Windows' 260 in the self-check's data root.
    public const string LongFileName =
        "Compte-rendu de l'assemblée générale de la copropriété du 14 mars 2026 et devis de ravalement de la façade.pdf";

    // A real PDF and a Markdown file of several lines, for the gallery's
    // previews: the first page of one, the beginning of the other's text. The
    // other PDFs of the sample hold zeroes, so they show the type icon that a
    // PDF Windows cannot read keeps.
    public const string PdfFileName = "Plan-du-jardin.pdf";
    public const string MarkdownFileName = "Lisez-moi.md";

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
        Folder(root, EmptyFolderName, -5);

        Text(root, MarkdownFileName,
            "# Lisez-moi\n\nDossier d'exemple créé par le self-check de Deckle.\n\n" +
            "## Ce qu'il contient\n\n- Documents : des notes et deux contrats.\n- Photos : deux années de sorties.\n" +
            "- Projets : vide pour l'instant.\n\nRien ici ne vient de vos dossiers.\n", -2);
        Pdf(root, PdfFileName, -16);
        Sized(root, LongFileName, 86 * 1024, -244);
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

    private static void Pdf(string parent, string name, int days)
    {
        string path = System.IO.Path.Combine(parent, name);
        File.WriteAllBytes(path, SamplePdf.Create("Plan du jardin",
            ["Potager au nord, verger au sud.", "Allée centrale de 1,20 m.", "Récupérateur d'eau près de l'abri."]));
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
