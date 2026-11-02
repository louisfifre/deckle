using Xunit;

namespace Deckle.Hud.Tests;

// A failed pipeline raises its message and then its "finished with nothing"
// signal, which the host answers with Hide(). The Hide must not wipe the
// warning or error before the person has read it (see HudHidePolicy).
//
// MessageKind is internal, so the cases name it as text: xunit only discovers
// public classes, and a public test method cannot take an internal type.
[Trait("Category", "regression")]
public sealed class HudHidePolicyTests
{
    [Theory]
    [InlineData("Warning")]
    [InlineData("Critical")]
    public void Hide_KeepsAWarningOrErrorMessageOnScreen(string kind)
    {
        Assert.False(HudHidePolicy.ShouldHonorHide(HudState.Message, Enum.Parse<MessageKind>(kind)));
    }

    [Theory]
    [InlineData("Success")]
    [InlineData("Informational")]
    public void Hide_ClearsASuccessOrInformationMessage(string kind)
    {
        Assert.True(HudHidePolicy.ShouldHonorHide(HudState.Message, Enum.Parse<MessageKind>(kind)));
    }

    [Theory]
    [InlineData(HudState.Hidden)]
    [InlineData(HudState.Charging)]
    [InlineData(HudState.Recording)]
    [InlineData(HudState.Transcribing)]
    [InlineData(HudState.Rewriting)]
    public void Hide_AppliesToEveryStateThatIsNotAMessage(HudState state)
    {
        // The message kind left over from an earlier message must not protect a
        // state that shows no message.
        Assert.True(HudHidePolicy.ShouldHonorHide(state, MessageKind.Critical));
        Assert.True(HudHidePolicy.ShouldHonorHide(state, null));
    }
}
