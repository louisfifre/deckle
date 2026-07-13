using Deckle.Input;
using Deckle.Input.Trackpad;
using Xunit;

namespace Deckle.Input.Trackpad.Tests;

// Tests de comportement sur ThreeFingerDragRecognizer — la machine à états
// qui décide tap-vs-drag, le rejeu du déplacement au démarrage, et le délai
// de grâce sur lift. On l'exerce en pur : les frames portent le temps, Tick
// porte l'horloge, les effets sortent en événements. Aucune injection, aucun
// thread — donc tout est observable frame par frame.
//
// Les seuils sont fixés explicitement dans chaque test : le contrat dépend de
// valeurs (seuil de démarrage, délai de grâce, clamp anti-saut) qui sont
// tunables jusqu'au gel ; un test ne doit pas pendre du défaut.
[Trait("Category", "unit")]
public class ThreeFingerDragRecognizerTests
{
    // Construit une frame depuis une liste de contacts (id, x, y, tip).
    // ContactCount = nombre de contacts, ButtonDown false, ScanTime 0,
    // ReportCount 1 — seuls le temps et les tips comptent pour le recognizer.
    private static ContactFrame Frame(double t, params (int id, int x, int y, bool tip)[] contacts)
    {
        var array = contacts
            .Select(c => new TouchpadContact(c.id, c.x, c.y, c.tip, Confidence: true))
            .ToArray();
        return new ContactFrame(array, array.Length, ButtonDown: false, ScanTime: 0, TimestampMs: t, ReportCount: 1);
    }

    // Collecteur d'événements partagé par les tests, monté sur un recognizer.
    private sealed class Recorder
    {
        public int Started;
        public int Tapped;
        public readonly List<(double dx, double dy)> Moves = new();
        public readonly List<string> Ended = new();

        public Recorder(ThreeFingerDragRecognizer r)
        {
            r.DragStarted += () => Started++;
            r.TapIgnored += () => Tapped++;
            r.DragMoved += (dx, dy) => Moves.Add((dx, dy));
            r.DragEnded += reason => Ended.Add(reason);
        }
    }

    [Fact]
    public void ThreeFingerTapBelowThresholdIsIgnoredWithNoDrag()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        // Petit frémissement sous le seuil.
        r.ProcessFrame(Frame(2, (1, 5, 0, true), (2, 5, 0, true), (3, 5, 0, true)));
        // Lift complet.
        r.ProcessFrame(Frame(3));

        Assert.Equal(1, rec.Tapped);
        Assert.Equal(0, rec.Started);
        Assert.Empty(rec.Moves);
        Assert.Empty(rec.Ended);
    }

    [Fact]
    public void TravelBeyondThresholdStartsDragAndFirstMoveReplaysAccumulatedTravel()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        // Déplacement de 60 unités en X : franchit le seuil de 50.
        r.ProcessFrame(Frame(2, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));

        Assert.Equal(1, rec.Started);
        Assert.Equal(DragPhase.Dragging, r.Phase);
        // Le premier DragMoved rejoue tout le déplacement accumulé.
        var first = Assert.Single(rec.Moves);
        Assert.Equal(60, first.dx, precision: 6);
        Assert.Equal(0, first.dy, precision: 6);
    }

    [Fact]
    public void WhileDraggingSubsequentFramesEmitCentroidDelta()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(2, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        rec.Moves.Clear(); // on isole le delta post-démarrage
        // Tous les contacts bougent de +10 en X, +20 en Y : centroïde (10, 20).
        r.ProcessFrame(Frame(3, (1, 70, 20, true), (2, 70, 20, true), (3, 70, 20, true)));

        var move = Assert.Single(rec.Moves);
        Assert.Equal(10, move.dx, precision: 6);
        Assert.Equal(20, move.dy, precision: 6);
    }

    [Fact]
    public void FourthFingerMidDragEndsAndReturningToThreeDoesNotRestart()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(2, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        rec.Moves.Clear();
        // Un quatrième doigt apparaît : ce geste appartient à Windows,
        // donc le drag injecté doit être relâché immédiatement.
        r.ProcessFrame(Frame(3,
            (1, 70, 0, true), (2, 70, 0, true), (3, 70, 0, true), (4, 999, 999, true)));

        Assert.Equal(DragPhase.Idle, r.Phase);
        Assert.Equal(new[] { "fourth-finger" }, rec.Ended.ToArray());
        Assert.Empty(rec.Moves);

        // Lever seulement le quatrième doigt ne constitue pas un nouveau
        // front montant depuis moins de trois : aucun second drag ne démarre.
        r.ProcessFrame(Frame(4,
            (1, 80, 0, true), (2, 80, 0, true), (3, 80, 0, true)));
        r.ProcessFrame(Frame(5,
            (1, 140, 0, true), (2, 140, 0, true), (3, 140, 0, true)));

        Assert.Equal(DragPhase.Idle, r.Phase);
        Assert.Equal(1, rec.Started);
        Assert.Equal(new[] { "fourth-finger" }, rec.Ended.ToArray());
    }

    [Fact]
    public void EngagementRequiresARisingEdge()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        // Quatre doigts puis trois : on retombe sur trois depuis le haut,
        // pas de front montant — aucun engagement, donc aucun drag même
        // après mouvement.
        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true), (4, 0, 0, true)));
        r.ProcessFrame(Frame(2, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(3, (1, 80, 0, true), (2, 80, 0, true), (3, 80, 0, true)));

        Assert.Equal(0, rec.Started);
        Assert.Equal(DragPhase.Idle, r.Phase);

        // Lift complet puis re-touche à trois : front montant, engagement.
        r.ProcessFrame(Frame(4));
        r.ProcessFrame(Frame(5, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));

        Assert.Equal(DragPhase.Engaged, r.Phase);
    }

    [Fact]
    public void LiftingToTwoFingersEntersGraceAndReturningResumesSameDrag()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, GraceDelayMs = 100 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(10, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        Assert.Equal(DragPhase.Dragging, r.Phase);

        // Lift à deux doigts à t=20 : grâce, deadline = 20 + 100 = 120.
        r.ProcessFrame(Frame(20, (1, 60, 0, true), (2, 60, 0, true)));
        Assert.Equal(DragPhase.Grace, r.Phase);
        Assert.Equal(120, r.GraceDeadlineMs!.Value, precision: 6);

        // Trois doigts reviennent avant la deadline (t=50) : même drag, pas
        // de fin ni de second démarrage.
        r.ProcessFrame(Frame(50, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        Assert.Equal(DragPhase.Dragging, r.Phase);
        Assert.Equal(1, rec.Started);
        Assert.Empty(rec.Ended);

        // Le mouvement repart : +10 sur les trois.
        rec.Moves.Clear();
        r.ProcessFrame(Frame(60, (1, 70, 0, true), (2, 70, 0, true), (3, 70, 0, true)));
        var move = Assert.Single(rec.Moves);
        Assert.Equal(10, move.dx, precision: 6);
    }

    [Fact]
    public void GraceFrameAfterDeadlineEndsDragAsExpired()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, GraceDelayMs = 100 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(10, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        r.ProcessFrame(Frame(20, (1, 60, 0, true), (2, 60, 0, true))); // grâce, deadline 120

        // Une frame arrive après la deadline : fin avec "grace-expired".
        r.ProcessFrame(Frame(200, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));

        Assert.Equal(DragPhase.Idle, r.Phase);
        Assert.Equal(new[] { "grace-expired" }, rec.Ended.ToArray());
    }

    [Fact]
    public void TickPastDeadlineEndsDragWithNoFrames()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50, GraceDelayMs = 100 };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(10, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        r.ProcessFrame(Frame(20, (1, 60, 0, true), (2, 60, 0, true))); // grâce, deadline 120

        // Plus aucune frame n'arrive (doigts levés) ; Tick au-delà de la
        // deadline doit clôturer.
        r.Tick(120);

        Assert.Equal(DragPhase.Idle, r.Phase);
        Assert.Equal(new[] { "grace-expired" }, rec.Ended.ToArray());
    }

    [Fact]
    public void FrameDeltaBeyondClampMovesNothingButDragSurvives()
    {
        var r = new ThreeFingerDragRecognizer
        {
            StartThresholdUnits = 50,
            MaxFrameDeltaUnits = 500,
        };
        var rec = new Recorder(r);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(2, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));
        rec.Moves.Clear();
        // Saut de 1000 unités : au-delà du clamp de 500 → rien ne bouge,
        // mais le drag survit.
        r.ProcessFrame(Frame(3, (1, 1060, 0, true), (2, 1060, 0, true), (3, 1060, 0, true)));

        Assert.Empty(rec.Moves);
        Assert.Equal(DragPhase.Dragging, r.Phase);
        Assert.Empty(rec.Ended);
    }

    [Fact]
    public void CancelDuringDragEndsOnceCancelWhenIdleEndsNothing()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        // Idle : Cancel ne produit rien.
        r.Cancel("noop");
        Assert.Empty(rec.Ended);

        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(2, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));

        r.Cancel("x");

        Assert.Equal(new[] { "x" }, rec.Ended.ToArray());
        Assert.Equal(DragPhase.Idle, r.Phase);
    }

    [Fact]
    public void TapThenImmediateRetouchEngagesAgainFromCleanState()
    {
        var r = new ThreeFingerDragRecognizer { StartThresholdUnits = 50 };
        var rec = new Recorder(r);

        // Premier tap : trois doigts, frémissement, lift.
        r.ProcessFrame(Frame(1, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(2));
        Assert.Equal(1, rec.Tapped);
        Assert.Equal(DragPhase.Idle, r.Phase);

        // Re-touche immédiate à trois : l'état est propre, on ré-engage et
        // un déplacement franchit le seuil → drag.
        r.ProcessFrame(Frame(3, (1, 0, 0, true), (2, 0, 0, true), (3, 0, 0, true)));
        r.ProcessFrame(Frame(4, (1, 60, 0, true), (2, 60, 0, true), (3, 60, 0, true)));

        Assert.Equal(1, rec.Started);
        Assert.Equal(DragPhase.Dragging, r.Phase);
    }
}
