using Deckle.Chrono;
using Xunit;

namespace Deckle.Chrono.Tests;

// Tests unit purs sur ChronoFormatter. Fonctions déterministes sans
// dépendance externe — le filet idéal pour la première strate de tests
// du projet. Couvre la décomposition d'un TimeSpan en (minutes, seconds,
// centiseconds), le wrapping des minutes à 100, le comportement du cap,
// et le format MM:SS.cc.
[Trait("Category", "unit")]
public class ChronoFormatterTests
{
    [Fact]
    public void DecomposeReturnsZeroForTimeSpanZero()
    {
        var d = ChronoFormatter.Decompose(TimeSpan.Zero);

        Assert.Equal(0, d.Minutes);
        Assert.Equal(0, d.Seconds);
        Assert.Equal(0, d.Centiseconds);
    }

    [Fact]
    public void DecomposeReturnsExpectedComponentsForMixedDuration()
    {
        // 1h 23m 45s 670ms — couvre minutes au-dessus de 60, seconds non
        // nuls, centiseconds = millisecondes / 10 (670ms → 67cs).
        var elapsed = new TimeSpan(0, 1, 23, 45, 670);

        var d = ChronoFormatter.Decompose(elapsed);

        Assert.Equal(83, d.Minutes);
        Assert.Equal(45, d.Seconds);
        Assert.Equal(67, d.Centiseconds);
    }

    [Fact]
    public void DecomposeWrapsMinutesPastOneHundred()
    {
        // 105 min wrappées via % 100 → 5 min. Garde le slot deux digits
        // stable pour les sessions longues sans changer la largeur du
        // visuel HUD.
        var elapsed = TimeSpan.FromMinutes(105);

        var d = ChronoFormatter.Decompose(elapsed);

        Assert.Equal(5, d.Minutes);
    }

    [Fact]
    public void DecomposeWithCapZeroAppliesNoCap()
    {
        // capSeconds = 0 → aucun cap, l'elapsed remonte tel quel.
        var elapsed = TimeSpan.FromSeconds(120);

        var d = ChronoFormatter.Decompose(elapsed, capSeconds: 0);

        Assert.Equal(2, d.Minutes);
        Assert.Equal(0, d.Seconds);
    }

    [Fact]
    public void DecomposeClampsElapsedWhenAboveCap()
    {
        // elapsed = 120s, cap = 60s → tronqué à 60s = 1min 00s.
        var elapsed = TimeSpan.FromSeconds(120);

        var d = ChronoFormatter.Decompose(elapsed, capSeconds: 60);

        Assert.Equal(1, d.Minutes);
        Assert.Equal(0, d.Seconds);
    }

    [Fact]
    public void DecomposeLeavesElapsedUnchangedWhenBelowCap()
    {
        // elapsed = 30s, cap = 60s → identique au cas sans cap.
        var elapsed = TimeSpan.FromSeconds(30);

        var d = ChronoFormatter.Decompose(elapsed, capSeconds: 60);

        Assert.Equal(0, d.Minutes);
        Assert.Equal(30, d.Seconds);
    }

    [Fact]
    public void FormatMmSsCsPadsAllComponentsToTwoDigits()
    {
        // 3min 4s 50ms → "03:04.05" — chaque champ padding D2.
        var elapsed = new TimeSpan(0, 0, 3, 4, 50);

        Assert.Equal("03:04.05", ChronoFormatter.FormatMmSsCs(elapsed));
    }

    [Fact]
    public void FormatMmSsCsReturnsZeroFormattedForZero()
    {
        Assert.Equal("00:00.00", ChronoFormatter.FormatMmSsCs(TimeSpan.Zero));
    }
}
