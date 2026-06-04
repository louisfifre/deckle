using Deckle.Hud;
using Xunit;

namespace Deckle.Hud.Tests;

// Aggregator est internal sealed dans Deckle.Hud ; l'accès depuis le projet
// de tests passe par l'InternalsVisibleTo déclaré dans Deckle.Hud.csproj.
[Trait("Category", "unit")]
public class ProximityRollupAggregatorTests
{
    [Fact]
    public void NewAggregatorHasZeroSamples()
    {
        var agg = new ProximityRollupAggregator();

        Assert.Equal(0, agg.TotalSamples);
        Assert.Equal(255, agg.MinAlpha);
        Assert.Equal(0, agg.MaxAlpha);
    }

    [Fact]
    public void ResetClearsCollectedSamples()
    {
        var agg = new ProximityRollupAggregator();
        agg.Add(distanceDip: 50, alpha: 100);
        agg.Add(distanceDip: 30, alpha: 200);

        agg.Reset();

        Assert.Equal(0, agg.TotalSamples);
        Assert.Equal(255, agg.MinAlpha);
        Assert.Equal(0, agg.MaxAlpha);
    }

    [Fact]
    public void AddFoldsMinAlphaIncrementally()
    {
        var agg = new ProximityRollupAggregator();

        agg.Add(distanceDip: 10, alpha: 200);
        agg.Add(distanceDip: 20, alpha: 150);
        agg.Add(distanceDip: 30, alpha: 220);

        Assert.Equal(150, agg.MinAlpha);
    }

    [Fact]
    public void AddFoldsMaxAlphaIncrementally()
    {
        var agg = new ProximityRollupAggregator();

        agg.Add(distanceDip: 10, alpha: 200);
        agg.Add(distanceDip: 20, alpha: 150);
        agg.Add(distanceDip: 30, alpha: 220);

        Assert.Equal(220, agg.MaxAlpha);
    }

    [Fact]
    public void AddCountsEverySample()
    {
        var agg = new ProximityRollupAggregator();

        for (int i = 0; i < 500; i++)
            agg.Add(distanceDip: i, alpha: (byte)(i % 256));

        Assert.Equal(500, agg.TotalSamples);
    }

    [Fact]
    public void ComputePercentilesReturnsP50AndP95FromSortedDistances()
    {
        var agg = new ProximityRollupAggregator();

        // Distances 1..100 → trié, p50 = 51 (index 50), p95 = 96 (index 95).
        for (int i = 1; i <= 100; i++)
            agg.Add(distanceDip: i, alpha: 128);

        var (p50, p95) = agg.ComputePercentiles();

        Assert.Equal(51, p50);
        Assert.Equal(96, p95);
    }

    [Fact]
    public void ComputePercentilesIsOrderIndependent()
    {
        var aggSorted = new ProximityRollupAggregator();
        var aggShuffled = new ProximityRollupAggregator();

        for (int i = 1; i <= 100; i++)
            aggSorted.Add(distanceDip: i, alpha: 128);

        var shuffled = new[] { 50, 12, 100, 1, 73, 25, 88, 4, 60, 33, 99, 17 };
        // Compléter à 100 samples avec les valeurs restantes pour aligner
        // la distribution.
        var seen = new HashSet<int>(shuffled);
        foreach (var v in shuffled) aggShuffled.Add(distanceDip: v, alpha: 128);
        for (int i = 1; i <= 100; i++)
            if (seen.Add(i)) aggShuffled.Add(distanceDip: i, alpha: 128);

        Assert.Equal(aggSorted.ComputePercentiles(), aggShuffled.ComputePercentiles());
    }

    [Fact]
    public void ComputePercentilesThrowsWhenNoSamples()
    {
        var agg = new ProximityRollupAggregator();

        Assert.Throws<InvalidOperationException>(() => agg.ComputePercentiles());
    }

    [Fact]
    public void RingBufferRetainsLastCapacitySamplesWhenOverflowing()
    {
        var agg = new ProximityRollupAggregator();

        // Première vague : Capacity samples à distance 9999 (qu'on veut
        // voir écrasés).
        for (int i = 0; i < ProximityRollupAggregator.Capacity; i++)
            agg.Add(distanceDip: 9999, alpha: 128);

        // Seconde vague : Capacity samples à distance 1..Capacity. Le
        // ring doit avoir entièrement écrasé la première vague.
        for (int i = 1; i <= ProximityRollupAggregator.Capacity; i++)
            agg.Add(distanceDip: i, alpha: 128);

        // TotalSamples reflète la vraie count cumulative, pas la
        // capacité du buffer.
        Assert.Equal(ProximityRollupAggregator.Capacity * 2, agg.TotalSamples);

        var (p50, p95) = agg.ComputePercentiles();

        // Si la première vague avait survécu, le p95 serait 9999. Les
        // percentiles doivent refléter strictement la seconde vague.
        Assert.NotEqual(9999, p50);
        Assert.NotEqual(9999, p95);
        Assert.InRange(p50, 1, ProximityRollupAggregator.Capacity);
        Assert.InRange(p95, 1, ProximityRollupAggregator.Capacity);
    }
}
