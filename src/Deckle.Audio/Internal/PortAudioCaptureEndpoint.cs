namespace Deckle.Audio.Internal;

// One open PortAudio input stream, in blocking mode (no callback): the capture
// loop pulls the frames on its own thread. A callback stream would run managed
// code from a native real-time thread, where a garbage collection pause stalls
// the device; pulling keeps the managed code on a thread Deckle owns.
//
// Start, Stop and the release go through PortAudioHost's thread, like every
// control call; Available and Read run on the caller's thread, directly.
internal sealed class PortAudioCaptureEndpoint : ICaptureEndpoint
{
    private nint _stream;

    public PortAudioCaptureEndpoint(nint stream, CaptureEndpointInfo info)
    {
        _stream = stream;
        Info = info;
    }

    public CaptureEndpointInfo Info { get; }

    public int Start() => PortAudioHost.Invoke(() =>
        PortAudioHost.ReasonOf(PortAudioInterop.Pa_StartStream(_stream)));

    public int Stop() => PortAudioHost.Invoke(() =>
        PortAudioHost.ReasonOf(PortAudioInterop.Pa_StopStream(_stream)));

    public int Available()
    {
        int available = PortAudioInterop.Pa_GetStreamReadAvailable(_stream);
        return available >= 0 ? available : PortAudioHost.ReasonOf(available);
    }

    public unsafe int Read(Span<float> interleaved, int frames)
    {
        if (interleaved.Length < frames * Info.Format.Channels)
            throw new ArgumentException("The destination is shorter than the frames to read.", nameof(interleaved));

        fixed (float* destination = interleaved)
        {
            int error = PortAudioInterop.Pa_ReadStream(_stream, destination, (uint)frames);
            return error == 0 ? 0 : PortAudioHost.ReasonOf(error);
        }
    }

    // Closing a stream that is still running stops it first (Pa_CloseStream
    // aborts it), so a take that threw before its stop still lets the device go.
    public void Dispose()
    {
        nint stream = Interlocked.Exchange(ref _stream, 0);
        if (stream == 0) return;
        PortAudioHost.Invoke(() =>
        {
            PortAudioInterop.Pa_CloseStream(stream);
            PortAudioHost.StreamClosed();
            return 0;
        });
    }
}
