using System.Runtime.InteropServices;

namespace Deckle.Core;

// ── UI Automation — focused-element probes ───────────────────────────────────
//
// Every probe reads the system-focused element and returns false when UIA
// cannot answer (UIA refuses, COM exception, protected process). The paste
// and autocorrect gates treat that "can't tell" as not safe.
//
// COM interop via classic ComImport (no AOT in this project). Only the vtable
// slots actually needed are declared; earlier slots are placeholders with
// opaque IntPtr types to preserve ordering.

public static partial class UIAutomation
{
    // https://learn.microsoft.com/windows/win32/winauto/uiauto-automation-element-propids
    private const int UIA_ControlTypePropertyId = 30003;
    private const int UIA_BoundingRectanglePropertyId = 30001;
    private const int UIA_ProcessIdPropertyId   = 30002;
    private const int UIA_IsPasswordPropertyId  = 30019;

    // https://learn.microsoft.com/windows/win32/winauto/uiauto-controltype-ids
    private const int UIA_EditControlTypeId     = 50004;
    private const int UIA_DocumentControlTypeId = 50030;

    // Control-pattern availability + Value read-only state, used to recognise
    // editable surfaces a bare ControlType misses (a Chromium/Electron
    // contenteditable reports a non-Edit ControlType yet exposes the Text
    // pattern). ValueIsReadOnly defaults to TRUE when the Value pattern is
    // absent, so it is only meaningful alongside IsValuePatternAvailable.
    // https://learn.microsoft.com/windows/win32/winauto/uiauto-control-pattern-availability-propids
    // https://learn.microsoft.com/windows/win32/winauto/uiauto-control-pattern-propids
    private const int UIA_IsTextPatternAvailablePropertyId     = 30040;
    private const int UIA_IsValuePatternAvailablePropertyId    = 30043;
    private const int UIA_ValueIsReadOnlyPropertyId            = 30046;
    private const int UIA_IsTextEditPatternAvailablePropertyId = 30149;

    private static readonly Guid CLSID_CUIAutomation =
        new("FF48DBA4-60EF-4201-AA87-54103EEF594E");

    // The UIAutomation client object is thread-safe and meant to be reused
    // across the life of the process.
    private static IUIAutomation? _instance;
    private static readonly object _lock = new();

    // Strict paste gate: true only when the focused element's ControlType is
    // Edit or Document. `diagnostic` carries a short line for the Paste log.
    public static bool IsFocusedElementTextEditable(out string diagnostic)
    {
        try
        {
            var ua = GetInstance();
            int hr = ua.GetFocusedElement(out var el);
            if (hr != 0 || el is null)
            {
                diagnostic = $"GetFocusedElement hr=0x{hr:X} el={(el is null ? "null" : "ok")}";
                return false;
            }

            hr = el.GetCurrentPropertyValue(UIA_ControlTypePropertyId, out var value);
            if (hr != 0 || value is null)
            {
                diagnostic = $"GetCurrentPropertyValue hr=0x{hr:X} value={(value is null ? "null" : "ok")}";
                return false;
            }

            int controlType = Convert.ToInt32(value);
            bool ok = controlType == UIA_EditControlTypeId
                   || controlType == UIA_DocumentControlTypeId;
            diagnostic = $"ControlType={controlType} (editable={ok})";
            return ok;
        }
        catch (Exception ex)
        {
            diagnostic = $"UIA exception: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    // Describes the system-focused element for the autocorrect surface gate:
    // IsPassword (UIA's own flag — required before the surface is trusted),
    // text-editability, and the owning process. `diagnostic` carries the raw
    // editability signature (ControlType + pattern availability) so the gate's
    // verdict is auditable in the logs. Returns false when UIA cannot answer at
    // all — the caller treats that as an unknown surface (observe, never correct).
    //
    // Editability is wider than the strict Edit/Document ControlType: a
    // contenteditable surface in Chromium/Electron apps reports a non-Edit
    // ControlType yet exposes the Text pattern, so the strict test would
    // withhold every correction there. Any editable-text signal counts: the
    // TextEdit pattern, a writable Value, or a Text provider. The bare
    // Text-provider clause is deliberately broad (a read-only document also
    // exposes it) and stays safe because corrections are gated by explicit
    // per-app enrollment.
    public static bool TryDescribeFocusedElement(
        out bool isPassword, out bool isTextEditable, out int processId, out string diagnostic)
    {
        isPassword = false;
        isTextEditable = false;
        processId = 0;
        try
        {
            var ua = GetInstance();
            int hr = ua.GetFocusedElement(out var el);
            if (hr != 0 || el is null)
            {
                diagnostic = $"GetFocusedElement hr=0x{hr:X} el={(el is null ? "null" : "ok")}";
                return false;
            }

            int passwordHr = el.GetCurrentPropertyValue(UIA_IsPasswordPropertyId, out var pw);
            if (passwordHr != 0 || pw is not bool password)
            {
                diagnostic = $"IsPassword unavailable hr=0x{passwordHr:X} type={pw?.GetType().Name ?? "null"}";
                return false;
            }
            isPassword = password;

            int controlType = 0;
            if (el.GetCurrentPropertyValue(UIA_ControlTypePropertyId, out var ct) == 0 && ct is not null)
                controlType = Convert.ToInt32(ct);

            bool hasTextPattern     = GetBool(el, UIA_IsTextPatternAvailablePropertyId);
            bool hasValuePattern    = GetBool(el, UIA_IsValuePatternAvailablePropertyId);
            bool hasTextEditPattern = GetBool(el, UIA_IsTextEditPatternAvailablePropertyId);
            bool valueReadOnly      = !hasValuePattern || GetBool(el, UIA_ValueIsReadOnlyPropertyId);

            isTextEditable = controlType == UIA_EditControlTypeId
                          || controlType == UIA_DocumentControlTypeId
                          || hasTextEditPattern
                          || (hasValuePattern && !valueReadOnly)
                          || hasTextPattern;

            if (el.GetCurrentPropertyValue(UIA_ProcessIdPropertyId, out var pid) == 0 && pid is not null)
                processId = Convert.ToInt32(pid);

            diagnostic =
                $"ctrl={controlType} text={hasTextPattern} value={hasValuePattern} "
                + $"value_ro={valueReadOnly} textedit={hasTextEditPattern}";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"UIA exception: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>Returns the focused element's physical screen rectangle. UIA
    /// exposes the value as four doubles (left, top, width, height).</summary>
    public static bool TryGetFocusedElementBounds(out ScreenRect bounds)
    {
        bounds = default;
        try
        {
            var ua = GetInstance();
            if (ua.GetFocusedElement(out var el) != 0 || el is null) return false;
            if (el.GetCurrentPropertyValue(UIA_BoundingRectanglePropertyId, out var value) != 0)
                return false;
            if (value is not double[] rect || rect.Length != 4) return false;
            if (rect[2] <= 0 || rect[3] <= 0) return false;

            bounds = new ScreenRect(
                (int)Math.Round(rect[0]),
                (int)Math.Round(rect[1]),
                (int)Math.Round(rect[2]),
                (int)Math.Round(rect[3]));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // True only when UIA returns a VT_BOOL TRUE for the property. A "not
    // supported" sentinel (a COM object, not a bool) or any failure reads as
    // false — the conservative default for a pattern-availability flag.
    private static bool GetBool(IUIAutomationElement el, int propertyId)
        => el.GetCurrentPropertyValue(propertyId, out var v) == 0 && v is bool b && b;

    private static IUIAutomation GetInstance()
    {
        if (_instance is not null) return _instance;
        lock (_lock)
        {
            if (_instance is not null) return _instance;
            Type? t = Type.GetTypeFromCLSID(CLSID_CUIAutomation, throwOnError: true);
            _instance = (IUIAutomation)Activator.CreateInstance(t!)!;
            return _instance;
        }
    }

    // Declared up to GetFocusedElement (vtable slot 5); earlier slots are
    // opaque placeholders because we never call them.
    [ComImport]
    [Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        [PreserveSig] int CompareElements(IntPtr el1, IntPtr el2, out int areSame);
        [PreserveSig] int CompareRuntimeIds(IntPtr ra1, IntPtr ra2, out int areSame);
        [PreserveSig] int GetRootElement(out IUIAutomationElement? root);
        [PreserveSig] int ElementFromHandle(IntPtr hwnd, out IUIAutomationElement? el);
        [PreserveSig] int ElementFromPoint(POINT pt, out IUIAutomationElement? el);
        [PreserveSig] int GetFocusedElement(out IUIAutomationElement? el);
    }

    // Declared in vtable order up to GetCurrentPatternAs (slot 11).
    [ComImport]
    [Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        [PreserveSig] int SetFocus();
        [PreserveSig] int GetRuntimeId(
            [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)]
            out int[] runtimeId);
        [PreserveSig] int FindFirst(int scope, IntPtr condition, out IUIAutomationElement? found);
        [PreserveSig] int FindAll(int scope, IntPtr condition, out IntPtr found);
        [PreserveSig] int FindFirstBuildCache(int scope, IntPtr condition, IntPtr cacheRequest, out IUIAutomationElement? found);
        [PreserveSig] int FindAllBuildCache(int scope, IntPtr condition, IntPtr cacheRequest, out IntPtr found);
        [PreserveSig] int BuildUpdatedCache(IntPtr cacheRequest, out IUIAutomationElement? updated);
        [PreserveSig] int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object value);
        [PreserveSig] int GetCurrentPropertyValueEx(
            int propertyId,
            [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue,
            [MarshalAs(UnmanagedType.Struct)] out object value);
        [PreserveSig] int GetCachedPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object value);
        [PreserveSig] int GetCachedPropertyValueEx(
            int propertyId,
            [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue,
            [MarshalAs(UnmanagedType.Struct)] out object value);
        [PreserveSig] int GetCurrentPatternAs(
            int patternId,
            [In] ref Guid riid,
            out IntPtr patternObject);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
}

public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}
