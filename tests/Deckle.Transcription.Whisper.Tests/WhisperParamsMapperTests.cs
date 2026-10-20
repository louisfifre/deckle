using System.Runtime.InteropServices;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

[Trait("Category", "regression")]
public sealed class WhisperParamsMapperTests
{
    [Fact]
    public void EmptyOverrideSuppressesConfiguredPromptAndCarry()
    {
        var settings = new TranscriptionSettings();
        settings.Engine.InitialPrompt = ".NET, Visual Studio, Python, Whisper, le shell.";
        settings.Engine.CarryInitialPrompt = true;
        var native = new WhisperFullParams();

        WhisperParamsMapper.NativeAllocations allocations =
            WhisperParamsMapper.Apply(ref native, settings, "unused", promptOverride: string.Empty);
        try
        {
            Assert.Equal(string.Empty, Marshal.PtrToStringUTF8(native.initial_prompt));
            Assert.Equal(0, native.carry_initial_prompt);
        }
        finally
        {
            allocations.Free();
        }
    }

    // whisper.cpp primes a call with what the previous call decoded unless
    // no_context is set. On 2026-10-21 the warm-up's credit line came back in
    // the first take that way.
    [Theory]
    [InlineData(true, false, 1)]  // first call of a take, warm-up, standalone call
    [InlineData(true, true, 0)]   // later call of a take, carry wanted
    [InlineData(false, true, 1)]  // carry turned off in settings
    [InlineData(false, false, 1)]
    public void OnlyACallThatContinuesThePreviousOneKeepsItsDecodedContext(
        bool useContext, bool continuesPreviousCall, byte expectedNoContext)
    {
        var settings = new TranscriptionSettings();
        settings.Context.UseContext = useContext;
        var native = new WhisperFullParams();

        WhisperParamsMapper.NativeAllocations allocations = WhisperParamsMapper.Apply(
            ref native, settings, "unused", continuesPreviousCall: continuesPreviousCall);
        try
        {
            Assert.Equal(expectedNoContext, native.no_context);
        }
        finally
        {
            allocations.Free();
        }
    }

    [Fact]
    public void NullOverrideKeepsConfiguredPromptAndCarry()
    {
        var settings = new TranscriptionSettings();
        settings.Engine.InitialPrompt = "Deckle vocabulary";
        settings.Engine.CarryInitialPrompt = true;
        var native = new WhisperFullParams();

        WhisperParamsMapper.NativeAllocations allocations =
            WhisperParamsMapper.Apply(ref native, settings, "unused", promptOverride: null);
        try
        {
            Assert.Equal("Deckle vocabulary", Marshal.PtrToStringUTF8(native.initial_prompt));
            Assert.Equal(1, native.carry_initial_prompt);
        }
        finally
        {
            allocations.Free();
        }
    }
}
