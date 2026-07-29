using System.Runtime.InteropServices;

namespace Deckle.Input;

// Win32 Precision Touchpad injection surfaced in April 2026. The installed
// 26100 SDK does not declare CreateSyntheticPointerDevice2 yet, even though
// current Windows builds export it from user32. Resolve that optional entry by
// name so older systems fail closed instead of throwing EntryPointNotFoundException.
internal static unsafe class SyntheticTouchpadInterop
{
    public const uint PT_TOUCHPAD = 5;

    public const int POINTER_FEEDBACK_NONE = 3;

    public const uint SDCO_PHYSICAL_SIZE = 0x1;
    public const uint SDCO_TOUCHPAD_GESTURE_ONLY = 0x2;

    public const uint POINTER_FLAG_INRANGE = 0x00000002;
    public const uint POINTER_FLAG_INCONTACT = 0x00000004;
    public const uint POINTER_FLAG_CONFIDENCE = 0x00004000;

    private static readonly CreateSyntheticPointerDevice2Delegate? _create = ResolveCreate();

    [StructLayout(LayoutKind.Sequential)]
    internal struct SyntheticDeviceCreationParams
    {
        public uint PointerType;
        public uint MaxCount;
        public int FeedbackMode;
        public IntPtr Monitor;
        public uint DeviceWidth;
        public uint DeviceHeight;
        public uint Options;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PointerInfo
    {
        public uint PointerType;
        public uint PointerId;
        public uint FrameId;
        public uint PointerFlags;
        public IntPtr SourceDevice;
        public IntPtr TargetWindow;
        public Point PixelLocation;
        public Point HimetricLocation;
        public Point PixelLocationRaw;
        public Point HimetricLocationRaw;
        public uint Time;
        public uint HistoryCount;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PointerTouchInfo
    {
        public PointerInfo PointerInfo;
        public uint TouchFlags;
        public uint TouchMask;
        public Rect Contact;
        public Rect ContactRaw;
        public uint Orientation;
        public uint Pressure;
    }

    // POINTER_TYPE_INFO is a DWORD followed by an 8-byte-aligned union on x64.
    // Deckle is x64-only; the explicit offset mirrors winuser.h exactly.
    [StructLayout(LayoutKind.Explicit, Size = 152)]
    internal struct PointerTypeInfo
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public PointerTouchInfo TouchInfo;
    }

    public static bool IsSupported => _create is not null;

    public static IntPtr Create(ref SyntheticDeviceCreationParams parameters) =>
        _create is null ? IntPtr.Zero : _create(ref parameters);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InjectSyntheticPointerInput(
        IntPtr device,
        PointerTypeInfo* pointerInfo,
        uint count);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern void DestroySyntheticPointerDevice(IntPtr device);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", ExactSpelling = true,
        CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    private delegate IntPtr CreateSyntheticPointerDevice2Delegate(
        ref SyntheticDeviceCreationParams parameters);

    private static CreateSyntheticPointerDevice2Delegate? ResolveCreate()
    {
        IntPtr user32 = GetModuleHandle("user32.dll");
        if (user32 == IntPtr.Zero) return null;

        IntPtr address = GetProcAddress(user32, "CreateSyntheticPointerDevice2");
        return address == IntPtr.Zero
            ? null
            : Marshal.GetDelegateForFunctionPointer<CreateSyntheticPointerDevice2Delegate>(address);
    }
}
