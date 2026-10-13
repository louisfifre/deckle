namespace Deckle.Vision;

public readonly struct CapturedFrame
{
    /// <summary>ID3D11Texture2D borrowed for the
    /// <see cref="ScreenCaptureService.FrameArrived"/> handler only: never
    /// retain it past the handler and never release it; the capture
    /// service releases it after the handler returns.</summary>
    public nint TexturePtr { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary><see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>
    /// ticks at acquisition, not wall-clock time.</summary>
    public long TimestampTicks { get; }

    public CapturedFrame(nint texturePtr, int width, int height, long timestampTicks)
    {
        TexturePtr     = texturePtr;
        Width          = width;
        Height         = height;
        TimestampTicks = timestampTicks;
    }
}
