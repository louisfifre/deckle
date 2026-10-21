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
        // The path travels with the rejection so its log can name the file.
        Action<string, AudioFileDecodeResult> reject,
        Action<string, Exception> decodeException,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(decode);
        ArgumentNullException.ThrowIfNull(reject);
        ArgumentNullException.ThrowIfNull(decodeException);

        Exception? completionError = null;
        int index = 0;
        try
        {
            foreach (string path in paths)
            {
                // 1-based position in the selection, so a file's events read "2 of 5".
                index++;
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
                    reject(path, decoded);
                    continue;
                }

                if (!writer.TryWrite(
                        new PreparedFileTranscription(path, decoded.Pcm, index)))
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
    ReadOnlyMemory<float> Audio,
    int Index);
