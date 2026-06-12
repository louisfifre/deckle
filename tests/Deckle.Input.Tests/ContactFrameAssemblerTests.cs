using Deckle.Input;
using Xunit;

namespace Deckle.Input.Tests;

// Tests de comportement sur ContactFrameAssembler — le contrat de
// réassemblage hybrid-mode du Precision Touchpad, pas son implémentation.
// On n'assert que ce qu'un appelant observe : la frame émise (ou son
// absence) et les compteurs d'anomalie. La règle load-bearing tient en
// trois points : le premier report d'une frame déclare le total, les
// suivants déclarent 0, et tous partagent le même scan time.
//
// Les contacts sont construits directement (records publics). On marque
// chaque contact Tip=true par défaut ici : l'assembleur ne lit pas l'état
// tip, seulement le nombre déclaré — la valeur est neutre pour ces tests.
[Trait("Category", "unit")]
public class ContactFrameAssemblerTests
{
    private static TouchpadContact Contact(int id, int x = 0, int y = 0) =>
        new(id, x, y, Tip: true, Confidence: true);

    [Fact]
    public void CompleteReportEmitsFrameImmediatelyClampedToDeclaredCount()
    {
        var assembler = new ContactFrameAssembler();
        // Trois slots dans le report, mais le device n'en déclare que deux —
        // le troisième est du padding et doit être ignoré.
        var report = new TouchpadReport(
            ScanTime: 100,
            ContactCount: 2,
            ButtonDown: false,
            Contacts: new[] { Contact(1), Contact(2), Contact(99) });

        var frame = assembler.Add(report, timestampMs: 1.0);

        Assert.NotNull(frame);
        Assert.Equal(1, frame!.ReportCount);
        Assert.Equal(2, frame.ContactCount);
        Assert.Equal(2, frame.Contacts.Length);
        Assert.Equal(1, frame.Contacts[0].Id);
        Assert.Equal(2, frame.Contacts[1].Id);
    }

    [Fact]
    public void FragmentedFrameAccumulatesAcrossContinuationsThenEmitsOnce()
    {
        var assembler = new ContactFrameAssembler();
        // Le report d'ouverture déclare 3 mais n'en porte qu'un : frame
        // pendante. Deux continuations (count 0, même scan time) apportent
        // le reste.
        var opening = new TouchpadReport(200, ContactCount: 3, ButtonDown: false,
            Contacts: new[] { Contact(1) });
        var cont1 = new TouchpadReport(200, ContactCount: 0, ButtonDown: false,
            Contacts: new[] { Contact(2) });
        var cont2 = new TouchpadReport(200, ContactCount: 0, ButtonDown: false,
            Contacts: new[] { Contact(3) });

        Assert.Null(assembler.Add(opening, 1.0));
        Assert.Null(assembler.Add(cont1, 2.0));
        var frame = assembler.Add(cont2, 3.0);

        Assert.NotNull(frame);
        Assert.Equal(3, frame!.ReportCount);
        Assert.Equal(3, frame.ContactCount);
        // Contacts dans l'ordre d'arrivée.
        Assert.Equal(new[] { 1, 2, 3 }, frame.Contacts.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void OrphanContinuationIsCountedAndEmitsNothing()
    {
        var assembler = new ContactFrameAssembler();
        var orphan = new TouchpadReport(300, ContactCount: 0, ButtonDown: false,
            Contacts: new[] { Contact(1) });

        var frame = assembler.Add(orphan, 1.0);

        Assert.Null(frame);
        Assert.Equal(1, assembler.OrphanContinuations);
    }

    [Fact]
    public void ContinuationWithMismatchedScanTimeDropsPendingAndIsCounted()
    {
        var assembler = new ContactFrameAssembler();
        var opening = new TouchpadReport(400, ContactCount: 2, ButtonDown: false,
            Contacts: new[] { Contact(1) });
        // Continuation au mauvais scan time : ce n'est pas la même frame,
        // la pendante est perdue, rien n'est émis.
        var stray = new TouchpadReport(401, ContactCount: 0, ButtonDown: false,
            Contacts: new[] { Contact(2) });

        Assert.Null(assembler.Add(opening, 1.0));
        var frame = assembler.Add(stray, 2.0);

        Assert.Null(frame);
        Assert.Equal(1, assembler.ScanTimeMismatches);
    }

    [Fact]
    public void NewOpeningWhilePendingFlushesTheLostFrameAndProceeds()
    {
        var assembler = new ContactFrameAssembler();
        var firstOpening = new TouchpadReport(500, ContactCount: 3, ButtonDown: false,
            Contacts: new[] { Contact(1) });
        // Nouvelle ouverture alors qu'une frame était encore pendante :
        // l'ancienne est flushée (comptée), la nouvelle suit son cours.
        var secondOpening = new TouchpadReport(600, ContactCount: 1, ButtonDown: false,
            Contacts: new[] { Contact(7) });

        Assert.Null(assembler.Add(firstOpening, 1.0));
        var frame = assembler.Add(secondOpening, 2.0);

        Assert.Equal(1, assembler.IncompleteFlushes);
        Assert.NotNull(frame);
        Assert.Equal(7, frame!.Contacts[0].Id);
        Assert.Equal(600u, frame.ScanTime);
    }

    [Fact]
    public void ContinuationTakesOnlyWhatIsStillNeeded()
    {
        var assembler = new ContactFrameAssembler();
        // Déclare 2, en porte 1 ; la continuation en porte 2 mais un seul
        // manque — le surplus est ignoré.
        var opening = new TouchpadReport(700, ContactCount: 2, ButtonDown: false,
            Contacts: new[] { Contact(1) });
        var cont = new TouchpadReport(700, ContactCount: 0, ButtonDown: false,
            Contacts: new[] { Contact(2), Contact(3) });

        Assert.Null(assembler.Add(opening, 1.0));
        var frame = assembler.Add(cont, 2.0);

        Assert.NotNull(frame);
        Assert.Equal(2, frame!.Contacts.Length);
        Assert.Equal(new[] { 1, 2 }, frame.Contacts.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void ButtonDownIsTrueIfAnyReportOfTheFrameHadIt()
    {
        var assembler = new ContactFrameAssembler();
        // Bouton enfoncé sur la continuation seulement : la frame assemblée
        // doit porter ButtonDown=true (OU logique sur tous les reports).
        var opening = new TouchpadReport(800, ContactCount: 2, ButtonDown: false,
            Contacts: new[] { Contact(1) });
        var cont = new TouchpadReport(800, ContactCount: 0, ButtonDown: true,
            Contacts: new[] { Contact(2) });

        Assert.Null(assembler.Add(opening, 1.0));
        var frame = assembler.Add(cont, 2.0);

        Assert.NotNull(frame);
        Assert.True(frame!.ButtonDown);
    }
}
