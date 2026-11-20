using Deckle.Inference.Onnx;
using Deckle.Transcription.Parakeet.Tokenization;

namespace Deckle.Transcription.Parakeet;

// Opens the four files of a model folder. Placement follows the measured spike: the mel
// front end and decoder_joint are small and run on the CPU; the encoder is the heavy part and
// goes to DirectML (about 3.5x faster than the CPU in steady state), falling back to the CPU
// when the device cannot be used. Encoder precision is the caller's choice (FP32 by default).

/// <summary>Builds a <see cref="ParakeetLoadedModel"/> on ONNX sessions.</summary>
internal sealed class OnnxParakeetModelLoader : IParakeetModelLoader
{
    public ParakeetLoadOutcome Load(ParakeetModelFiles files, ParakeetEncoderPrecision precision, bool useGpu)
    {
        string encoderPath = precision == ParakeetEncoderPrecision.Fp16 ? files.EncoderFp16 : files.EncoderFp32;
        foreach (string required in new[] { files.Vocabulary, files.Preprocessor, encoderPath, files.DecoderJoint })
        {
            if (!File.Exists(required)) return ParakeetLoadOutcome.Failed("file_not_found", required);
        }

        var opened = new List<IDisposable>();
        try
        {
            ParakeetVocabulary vocabulary = ParakeetVocabulary.Load(files.Vocabulary);

            var preprocessor = Track(opened, new OnnxModelSession(files.Preprocessor));
            (OnnxModelSession encoderSession, string accelerator, string? fallbackType) = OpenEncoder(encoderPath, useGpu);
            opened.Add(encoderSession);
            var decoderJoint = Track(opened, new OnnxModelSession(files.DecoderJoint));

            OnnxTdtJoint.Validate(decoderJoint, vocabulary.Count);
            var joint = new OnnxTdtJoint(decoderJoint, vocabulary.Count);
            var encoder = new OnnxParakeetEncoder(preprocessor, encoderSession);

            return ParakeetLoadOutcome.Loaded(new ParakeetLoadedModel(
                vocabulary, encoder, joint, accelerator, fallbackType, [.. opened]));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing half-open stays behind: the next attempt starts from a clean process.
            foreach (IDisposable session in opened) session.Dispose();
            return ParakeetLoadOutcome.Failed("init_failed", files.Directory, ex.GetType().Name, ex.Message);
        }
    }

    // DirectML first when asked; any device refusal is a fallback to the CPU, reported to the
    // caller so it can log what was asked and what was obtained.
    private static (OnnxModelSession Session, string Accelerator, string? FallbackType) OpenEncoder(string path, bool useGpu)
    {
        if (useGpu)
        {
            try
            {
                return (new OnnxModelSession(path, OnnxSessionOptions.DirectMl()), ParakeetAccelerators.DirectMl, null);
            }
            catch (OnnxDeviceUnavailableException ex)
            {
                // The encoder is big: leave a core or two to the rest of the app.
                int threads = Math.Max(1, Environment.ProcessorCount / 2);
                return (new OnnxModelSession(path, OnnxSessionOptions.Cpu(threads)), ParakeetAccelerators.Cpu, ex.GetType().Name);
            }
        }

        int cpuThreads = Math.Max(1, Environment.ProcessorCount / 2);
        return (new OnnxModelSession(path, OnnxSessionOptions.Cpu(cpuThreads)), ParakeetAccelerators.Cpu, null);
    }

    private static T Track<T>(List<IDisposable> opened, T session) where T : IDisposable
    {
        opened.Add(session);
        return session;
    }
}
