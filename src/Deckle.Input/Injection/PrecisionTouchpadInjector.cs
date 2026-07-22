using System.Runtime.InteropServices;

namespace Deckle.Input;

public readonly record struct TouchpadPosition(int X, int Y);

// Owns one gesture-only synthetic Precision Touchpad and submits complete
// two-contact frames. Coordinates are device-relative himetric units
// (1/100 mm), matching CreateSyntheticPointerDevice2's physical-size contract.
public sealed unsafe class PrecisionTouchpadInjector : IDisposable
{
    public const int DeviceWidth = 10_000;
    public const int DeviceHeight = 6_000;

    private readonly SyntheticTouchpadFrameBuilder _frames = new();
    private IntPtr _device;

    public bool IsContactActive => _frames.IsContactActive;

    public static bool IsSupported => SyntheticTouchpadInterop.IsSupported;

    public int LastError { get; private set; }

    private PrecisionTouchpadInjector(IntPtr device) => _device = device;

    public static bool TryCreate(out PrecisionTouchpadInjector? injector, out int error)
    {
        injector = null;
        error = 0;
        if (!SyntheticTouchpadInterop.IsSupported) return false;

        var parameters = new SyntheticTouchpadInterop.SyntheticDeviceCreationParams
        {
            PointerType = SyntheticTouchpadInterop.PT_TOUCHPAD,
            MaxCount = 2,
            FeedbackMode = SyntheticTouchpadInterop.POINTER_FEEDBACK_NONE,
            Monitor = IntPtr.Zero,
            DeviceWidth = DeviceWidth,
            DeviceHeight = DeviceHeight,
            Options = SyntheticTouchpadInterop.SDCO_PHYSICAL_SIZE
                | SyntheticTouchpadInterop.SDCO_TOUCHPAD_GESTURE_ONLY,
        };

        IntPtr device = SyntheticTouchpadInterop.Create(ref parameters);
        if (device == IntPtr.Zero)
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        injector = new PrecisionTouchpadInjector(device);
        return true;
    }

    public bool Begin(TouchpadPosition first, TouchpadPosition second)
    {
        if (_device == IntPtr.Zero) return false;
        return Inject(_frames.Begin(first, second));
    }

    public bool Move(TouchpadPosition first, TouchpadPosition second, uint elapsedMs)
    {
        if (_device == IntPtr.Zero) return false;
        return Inject(_frames.Move(first, second, elapsedMs));
    }

    public bool End(uint elapsedMs)
    {
        if (_device == IntPtr.Zero || !_frames.IsContactActive) return true;
        return Inject(_frames.End(elapsedMs));
    }

    public void Dispose()
    {
        if (_device == IntPtr.Zero) return;
        End(elapsedMs: 1);
        SyntheticTouchpadInterop.DestroySyntheticPointerDevice(_device);
        _device = IntPtr.Zero;
    }

    private bool Inject(SyntheticTouchpadInterop.PointerTypeInfo[] frame)
    {
        bool injected;
        fixed (SyntheticTouchpadInterop.PointerTypeInfo* pointer = frame)
        {
            injected = SyntheticTouchpadInterop.InjectSyntheticPointerInput(
                _device, pointer, (uint)frame.Length);
        }
        LastError = injected ? 0 : Marshal.GetLastWin32Error();
        return injected;
    }
}

internal sealed class SyntheticTouchpadFrameBuilder
{
    private const uint ContactFlags =
        SyntheticTouchpadInterop.POINTER_FLAG_INRANGE
        | SyntheticTouchpadInterop.POINTER_FLAG_INCONTACT
        | SyntheticTouchpadInterop.POINTER_FLAG_CONFIDENCE;

    private uint _time = 1;
    private readonly SyntheticTouchpadInterop.PointerTypeInfo[] _frame = new SyntheticTouchpadInterop.PointerTypeInfo[2];
    private TouchpadPosition _first;
    private TouchpadPosition _second;

    public bool IsContactActive { get; private set; }

    public SyntheticTouchpadInterop.PointerTypeInfo[] Begin(
        TouchpadPosition first,
        TouchpadPosition second)
    {
        if (IsContactActive)
            throw new InvalidOperationException("A synthetic touchpad gesture is already active.");

        _time = 1;
        _first = first;
        _second = second;
        IsContactActive = true;
        return CreateFrame(first, second, ContactFlags);
    }

    public SyntheticTouchpadInterop.PointerTypeInfo[] Move(
        TouchpadPosition first,
        TouchpadPosition second,
        uint elapsedMs)
    {
        if (!IsContactActive)
            throw new InvalidOperationException("A synthetic touchpad gesture has not begun.");

        Advance(elapsedMs);
        _first = first;
        _second = second;
        return CreateFrame(first, second, ContactFlags);
    }

    public SyntheticTouchpadInterop.PointerTypeInfo[] End(uint elapsedMs)
    {
        if (!IsContactActive)
            throw new InvalidOperationException("A synthetic touchpad gesture has not begun.");

        Advance(elapsedMs);
        IsContactActive = false;
        return CreateFrame(_first, _second, SyntheticTouchpadInterop.POINTER_FLAG_CONFIDENCE);
    }

    private void Advance(uint elapsedMs) => _time += Math.Max(elapsedMs, 1);

    private SyntheticTouchpadInterop.PointerTypeInfo[] CreateFrame(
        TouchpadPosition first,
        TouchpadPosition second,
        uint flags)
    {
        _frame[0] = CreateContact(pointerId: 0, first, flags);
        _frame[1] = CreateContact(pointerId: 1, second, flags);
        return _frame;
    }

    private SyntheticTouchpadInterop.PointerTypeInfo CreateContact(
        uint pointerId,
        TouchpadPosition position,
        uint flags) =>
        new()
        {
            Type = SyntheticTouchpadInterop.PT_TOUCHPAD,
            TouchInfo = new SyntheticTouchpadInterop.PointerTouchInfo
            {
                PointerInfo = new SyntheticTouchpadInterop.PointerInfo
                {
                    PointerId = pointerId,
                    PointerFlags = flags,
                    HimetricLocation = new SyntheticTouchpadInterop.Point
                    {
                        X = position.X,
                        Y = position.Y,
                    },
                    Time = _time,
                },
            },
        };
}
