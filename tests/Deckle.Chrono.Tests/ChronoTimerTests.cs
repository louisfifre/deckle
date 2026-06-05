using System;
using System.Threading;
using Deckle.Chrono;
using Xunit;

namespace Deckle.Chrono.Tests;

// Tests de comportement sur ChronoTimer — le contrat du chronomètre, pas son
// implémentation (il enveloppe un Stopwatch, mais on n'assert que ce qu'un
// appelant observe : état marche/arrêt et valeur écoulée). Le découplage
// chrono/peinture du HUD repose sur ce contrat, en particulier sur
// `Start == Restart` : c'est lui qui garantit qu'une nouvelle session ne
// reparte jamais du temps de la précédente, au niveau du timer.
//
// Les cas dépendants du temps réel utilisent un court Thread.Sleep pour rendre
// l'écoulement mesurable ; les assertions sont relatives (avant/après) plutôt
// que sur un seuil absolu, donc robustes au jitter de l'ordonnanceur.
[Trait("Category", "unit")]
public class ChronoTimerTests
{
    [Fact]
    public void FreshTimerIsNotRunningAndReadsZero()
    {
        var t = new ChronoTimer();

        Assert.False(t.IsRunning);
        Assert.Equal(TimeSpan.Zero, t.Elapsed);
    }

    [Fact]
    public void StartBeginsRunning()
    {
        var t = new ChronoTimer();

        t.Start();

        Assert.True(t.IsRunning);
    }

    [Fact]
    public void StopFreezesTheElapsedValue()
    {
        var t = new ChronoTimer();
        t.Start();
        t.Stop();

        Assert.False(t.IsRunning);
        // Une fois arrêté, deux lectures successives donnent la même valeur —
        // le chrono est gelé, il n'avance plus.
        var first = t.Elapsed;
        var second = t.Elapsed;
        Assert.Equal(first, second);
    }

    [Fact]
    public void ResetZeroesAndStops()
    {
        var t = new ChronoTimer();
        t.Start();
        Thread.Sleep(10);

        t.Reset();

        Assert.False(t.IsRunning);
        Assert.Equal(TimeSpan.Zero, t.Elapsed);
    }

    [Fact]
    public void StartZeroesAnyPriorAccumulation()
    {
        // Le contrat load-bearing du découplage : relancer le chrono efface
        // le temps déjà accumulé (sémantique Restart), donc StartClock ne
        // peut pas hériter de la valeur figée de la session précédente.
        var t = new ChronoTimer();

        t.Start();
        Thread.Sleep(20);
        t.Stop();
        var afterAccumulation = t.Elapsed;
        Assert.True(afterAccumulation > TimeSpan.Zero);

        t.Start();   // Restart : remet à zéro puis repart
        t.Stop();
        var afterRestart = t.Elapsed;

        Assert.True(
            afterRestart < afterAccumulation,
            $"Start aurait dû repartir de zéro : {afterRestart} >= {afterAccumulation}");
    }

    [Fact]
    public void ResumeContinuesFromTheCurrentValueWithoutZeroing()
    {
        // Resume reprend sans reset — la valeur déjà accumulée reste un
        // plancher. C'est la distinction qui sépare Resume de Start.
        var t = new ChronoTimer();

        t.Start();
        Thread.Sleep(20);
        t.Stop();
        var afterStop = t.Elapsed;

        t.Resume();
        t.Stop();
        var afterResume = t.Elapsed;

        Assert.True(
            afterResume >= afterStop,
            $"Resume aurait dû conserver l'accumulé : {afterResume} < {afterStop}");
    }
}
