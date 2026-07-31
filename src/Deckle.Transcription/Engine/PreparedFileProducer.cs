using System.Threading.Channels;
using Deckle.Audio;

namespace Deckle.Transcription;

// Bounded decode producer for one immutable picker selection. It acquires the
// single N+1 slot before decoding, so an N+2 PCM buffer can never be retained
// while the consumer is still transcribing N.
internal static class PreparedFileProducer
{
    public const int Capacity = 1;

    public static Channel<PreparedFileTranscription> CreateChannel() =>
        Channel.CreateBounded<PreparedFileTranscription>(
            new BoundedChannelOptions(Capacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });

    public static async Task ProduceAsync(
        IReadOnlyList<string> paths,
        ChannelWriter<PreparedFileTranscription> writer,
        Func<string, AudioFileDecodeResult> decode,
        Action<AudioFileDecodeResult> reject,
        Action<string, Exception> decodeException,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(decode);
        ArgumentNullException.ThrowIfNull(reject);
        ArgumentNullException.ThrowIfNull(decodeException);

        Exception? completionError = null;
        try
        {
            foreach (string path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!await writer.WaitToWriteAsync(cancellationToken)
                        .ConfigureAwait(false))
                    break;

                AudioFileDecodeResult decoded;
                try
                {
                    decoded = decode(path);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    decodeException(path, ex);
                    continue;
                }
                if (decoded.Status != AudioFileDecodeStatus.Decoded)
                {
                    reject(decoded);
                    continue;
                }

                if (!writer.TryWrite(
                        new PreparedFileTranscription(path, decoded.Pcm)))
                {
                    throw new InvalidOperationException(
                        "The single prepared-file producer lost its reserved slot.");
                }
            }
        }
        catch (Exception ex)
        {
            completionError = ex;
            throw;
        }
        finally
        {
            writer.TryComplete(completionError);
        }
    }
}

internal sealed record PreparedFileTranscription(
    string SourcePath,
    ReadOnlyMemory<float> Audio);
