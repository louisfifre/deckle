using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

// The GPU switch was bound on the settings page while the backend hard-coded
// use_gpu = 1, so turning it off changed nothing.
[Trait("Category", "regression")]
public sealed class WhisperContextParamsMapperTests
{
    [Fact]
    public void DisabledGpuSettingTurnsOffUseGpu()
    {
        var settings = new TranscriptionSettings();
        settings.Engine.UseGpu = false;
        var native = new WhisperContextParams { use_gpu = 1 };

        WhisperContextParamsMapper.Apply(ref native, settings);

        Assert.Equal(0, native.use_gpu);
    }

    [Fact]
    public void EnabledGpuSettingTurnsOnUseGpu()
    {
        var settings = new TranscriptionSettings();
        settings.Engine.UseGpu = true;
        var native = new WhisperContextParams { use_gpu = 0 };

        WhisperContextParamsMapper.Apply(ref native, settings);

        Assert.Equal(1, native.use_gpu);
    }

    [Fact]
    public void MappingKeepsWhisperDefaultsForTheOtherFields()
    {
        var native = new WhisperContextParams { gpu_device = 2, flash_attn = 1 };

        WhisperContextParamsMapper.Apply(ref native, new TranscriptionSettings());

        Assert.Equal(2, native.gpu_device);
        Assert.Equal(1, native.flash_attn);
    }
}
