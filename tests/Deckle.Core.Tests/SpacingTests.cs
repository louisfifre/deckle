using Xunit;

namespace Deckle.Core.Tests;

// The scale is the promise (Louis, 2026-10-16): seven named steps, 4 to 256,
// each twice the one before. The numbers are pinned here so a change is a
// decision and not a slip; every consumer compares against the step by name.
[Trait("Category", "unit")]
public sealed class SpacingTests
{
    [Fact]
    public void EachStepHasItsValue()
    {
        Assert.Equal(4,   Spacing.XXSmall);
        Assert.Equal(8,   Spacing.XSmall);
        Assert.Equal(16,  Spacing.Small);
        Assert.Equal(32,  Spacing.Medium);
        Assert.Equal(64,  Spacing.Large);
        Assert.Equal(128, Spacing.XLarge);
        Assert.Equal(256, Spacing.XXLarge);
    }

    [Fact]
    public void EachStepDoublesThePrevious()
    {
        int[] steps =
        [
            Spacing.XXSmall, Spacing.XSmall, Spacing.Small, Spacing.Medium,
            Spacing.Large, Spacing.XLarge, Spacing.XXLarge,
        ];
        for (int i = 1; i < steps.Length; i++)
            Assert.Equal(steps[i - 1] * 2, steps[i]);
    }
}
