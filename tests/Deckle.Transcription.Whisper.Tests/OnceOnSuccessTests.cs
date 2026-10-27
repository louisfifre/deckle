using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

// The gate in front of ggml_backend_load_all_from_path. It once set its flag
// before the native call, so a call that threw left the backends marked
// loaded for the rest of the process, and no repair could bring them back
// without a restart.
[Trait("Category", "regression")]
public sealed class OnceOnSuccessTests
{
    [Fact]
    public void AnAttemptThatThrowsIsTriedAgain()
    {
        var gate = new OnceOnSuccess();
        int attempts = 0;

        Assert.Throws<EntryPointNotFoundException>(() =>
            gate.Run(() => { attempts++; throw new EntryPointNotFoundException("ggml_backend_load_all_from_path"); }));
        Assert.False(gate.Done);

        gate.Run(() => { attempts++; return true; });

        Assert.Equal(2, attempts);
        Assert.True(gate.Done);
    }

    [Fact]
    public void AnAttemptThatDidNothingIsTriedAgain()
    {
        var gate = new OnceOnSuccess();
        int attempts = 0;

        gate.Run(() => { attempts++; return false; });
        gate.Run(() => { attempts++; return true; });

        Assert.Equal(2, attempts);
    }

    // ggml registers what it loads: a second successful run would register
    // every backend twice.
    [Fact]
    public void ASuccessIsNeverRepeated()
    {
        var gate = new OnceOnSuccess();
        int attempts = 0;

        gate.Run(() => { attempts++; return true; });
        gate.Run(() => { attempts++; return true; });

        Assert.Equal(1, attempts);
    }
}
