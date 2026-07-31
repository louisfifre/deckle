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
