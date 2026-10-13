using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// The gesture rule of a magnitude's slider: the model is written once, when the
// pointer is released, never on every detent crossed while dragging. A step with
// no press of its own (keyboard, wheel) writes at once. The hold is pure state,
// so the contract is exercised without a control.
[Trait("Category", "unit")]
public class GestureHoldTests
{
    [Fact]
    public void AValueOfferedOutsideAGestureIsWrittenAtOnce()
    {
        var hold = new GestureHold();

        Assert.True(hold.Offer(3));
        Assert.Null(hold.End());
    }

    [Fact]
    public void ValuesOfferedDuringAGestureAreHeldAndTheLastOneComesOutAtTheRelease()
    {
        var hold = new GestureHold();
        hold.Begin();

        Assert.False(hold.Offer(1));
        Assert.False(hold.Offer(2));
        Assert.False(hold.Offer(3));

        Assert.Equal(3, hold.End());
    }

    [Fact]
    public void AGestureThatCrossedNoDetentWritesNothing()
    {
        var hold = new GestureHold();
        hold.Begin();

        Assert.Null(hold.End());
    }

    [Fact]
    public void EndingTwiceWritesTheHeldValueOnlyOnce()
    {
        // Release and capture loss both end the gesture; the second must find
        // nothing to write.
        var hold = new GestureHold();
        hold.Begin();
        hold.Offer(7);

        Assert.Equal(7, hold.End());
        Assert.Null(hold.End());
    }

    [Fact]
    public void ANewPressDropsWhatAnEarlierGestureLeftBehind()
    {
        var hold = new GestureHold();
        hold.Begin();
        hold.Offer(1);
        hold.Begin();

        Assert.Null(hold.End());
    }

    [Fact]
    public void AfterTheReleaseValuesAreWrittenAtOnceAgain()
    {
        var hold = new GestureHold();
        hold.Begin();
        hold.Offer(1);
        hold.End();

        Assert.True(hold.Offer(5));
    }
}
