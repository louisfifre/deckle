using System.IO;
using Deckle.Input.Autocorrect.Cli.Commands;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// The Morphalou CSV reader must read the INFLECTED form (the second GRAPHIE
// column), not the lemma (the first), and find that column past a preamble of
// arbitrary length — without hard-coding an index.
public sealed class MorphalouReaderTests
{
    // Mirrors the real file shape: free-text preamble, a group line, the column
    // header with two GRAPHIE columns, then lemma-grouped data rows (the lemma
    // columns are filled only on the first form of each lemma).
    private const string Sample =
        "Morphalou3.1 : Lexique morphologique\n" +
        "Total : … lemmes\n" +
        "LEMME;;;;;;;;;FLEXION;;;;;;;;\n" +
        "GRAPHIE;ID;CATÉGORIE;SOUS CATÉGORIE;LOCUTION;GENRE;AUTRES LEMMES LIÉS;PHONÉTIQUE;ORIGINES;GRAPHIE;ID;NOMBRE;MODE;GENRE;TEMPS;PERSONNE\n" +
        "capter;1;Verbe;;;;;k a p t e;src;capte;1;;indicative;;present;firstPerson\n" +
        ";;;;;;;;;captes;2;;indicative;;present;secondPerson\n" +
        "modèle;1;Nom commun;;;masculine;;m;src;modèle;1;singular;-;-;-;-\n" +
        ";;;;;;;;;modèles;2;plural;-;-;-;-\n" +
        "short;line\n"; // too few columns — skipped, not a crash

    [Fact]
    public void ReadsTheInflectedFormColumnNotTheLemma()
    {
        var forms = MorphalouReader.ReadInflectedForms(new StringReader(Sample)).ToList();

        // The second GRAPHIE (inflected), in file order: "capte" not "capter".
        Assert.Equal(new[] { "capte", "captes", "modèle", "modèles" }, forms);
    }

    [Fact]
    public void ThrowsWhenNoHeaderIsPresent()
    {
        const string headerless = "just;some;preamble\nno columns here\n";
        Assert.Throws<InvalidOperationException>(
            () => MorphalouReader.ReadInflectedForms(new StringReader(headerless)).ToList());
    }
}
