using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// SurfaceChanged follows the focus, so it fires as often as the person moves
// between fields. It is counted whether or not a listener is attached.
// The class joins the observability collection: the event it emits is heard by
// any listener of a parallel test on the process-wide Deckle-Autocorrect provider.
[Collection(AutocorrectObservabilityCollection.Name)]
[Trait("Category", "observability")]
public sealed class AutocorrectHotLoopCountTests
{
    [Fact]
    public void ASurfaceChangedEventIsCountedWithoutAListener()
    {
        long before = HotLoops.AutocorrectSurfaceChanged.Total;

        DeckleAutocorrectSource.Log.SurfaceChanged("notepad", editable: true, password: false, enrolled: true, probe: "uia");

        Assert.True(HotLoops.AutocorrectSurfaceChanged.Total >= before + 1);
    }
}