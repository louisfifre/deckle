using System.Runtime.InteropServices;

namespace Deckle.Core;

public static partial class UIAutomation
{
    private const int UIA_NativeWindowHandlePropertyId = 30020;
    private const int UIA_TextPatternId = 10014;
    private const int UIA_TextPattern2Id = 10024;

    private const int TextPatternRangeEndpointStart = 0;
    private const int TextPatternRangeEndpointEnd = 1;
    private const int TextUnitCharacter = 0;

    private static readonly Guid IID_IUIAutomationTextPattern =
        new("32EBA289-3583-42C9-9C59-3B6D9A1E9B6A");
    private static readonly Guid IID_IUIAutomationTextPattern2 =
        new("506A921A-FCC9-409F-B23B-37EB74106872");

    /// <summary>Reads a bounded span immediately before the active caret in the
    /// focused text provider. This is a read-only diagnostic seam: it never
    /// selects, scrolls, edits, or logs the returned text.</summary>
    public static bool TryReadFocusedTextBeforeCaret(
        int maxCharacters,
        out FocusedCaretText text,
        out string diagnostic)
    {
        text = default;
        if (maxCharacters is < 1 or > 4096)
        {
            diagnostic = "max_characters_out_of_range";
            return false;
        }

        try
        {
            IUIAutomation automation = GetInstance();
            int hr = automation.GetFocusedElement(out IUIAutomationElement? element);
            if (hr != 0 || element is null)
            {
                diagnostic = $"focused_element_unavailable hr=0x{hr:X}";
                return false;
            }

            // Ignore UIA's default FALSE: unsupported password metadata is not
            // evidence that a surface is safe to read.
            hr = element.GetCurrentPropertyValueEx(
                UIA_IsPasswordPropertyId,
                ignoreDefaultValue: true,
                out object passwordValue);
            if (hr != 0 || passwordValue is not bool isPassword)
            {
                diagnostic = $"password_status_unknown hr=0x{hr:X}";
                return false;
            }
            if (isPassword)
            {
                diagnostic = "password_surface";
                return false;
            }

            IUIAutomationTextRange? caret = null;
            string pattern;
            IUIAutomationTextPattern2? textPattern2 = GetPattern<IUIAutomationTextPattern2>(
                element,
                UIA_TextPattern2Id,
                IID_IUIAutomationTextPattern2);
            if (textPattern2 is not null)
            {
                // GetCaretRange reports the active caret even while the control
                // owns a non-empty selection. Recovery is allowed only when
                // GetSelection proves the insertion range itself is degenerate.
                hr = textPattern2.GetSelection(out IUIAutomationTextRangeArray? ranges);
                if (hr != 0 || ranges is null || ranges.GetLength(out int count) != 0 || count != 1
                    || ranges.GetElement(0, out IUIAutomationTextRange? selection) != 0
                    || selection is null)
                {
                    diagnostic = $"single_selection_unavailable hr=0x{hr:X}";
                    return false;
                }
                hr = selection.CompareEndpoints(
                    TextPatternRangeEndpointStart,
                    selection,
                    TextPatternRangeEndpointEnd,
                    out int selectionEndpointDifference);
                if (hr != 0 || selectionEndpointDifference != 0)
                {
                    diagnostic =
                        $"selection_not_degenerate hr=0x{hr:X} delta={selectionEndpointDifference}";
                    return false;
                }

                hr = textPattern2.GetCaretRange(out int isActive, out caret);
                if (hr != 0 || isActive == 0 || caret is null)
                {
                    diagnostic = $"active_caret_unavailable hr=0x{hr:X} active={isActive}";
                    return false;
                }
                pattern = "text2_caret";
            }
            else
            {
                IUIAutomationTextPattern? textPattern = GetPattern<IUIAutomationTextPattern>(
                    element,
                    UIA_TextPatternId,
                    IID_IUIAutomationTextPattern);
                if (textPattern is null)
                {
                    diagnostic =
                        $"text_pattern_unavailable pid={GetInt(element, UIA_ProcessIdPropertyId)} "
                        + $"ctrl={GetInt(element, UIA_ControlTypePropertyId)} "
                        + $"hwnd={GetInt(element, UIA_NativeWindowHandlePropertyId)} "
                        + $"text={GetBool(element, UIA_IsTextPatternAvailablePropertyId)} "
                        + $"textedit={GetBool(element, UIA_IsTextEditPatternAvailablePropertyId)}";
                    return false;
                }

                hr = textPattern.GetSelection(out IUIAutomationTextRangeArray? ranges);
                if (hr != 0 || ranges is null || ranges.GetLength(out int count) != 0 || count != 1
                    || ranges.GetElement(0, out caret) != 0 || caret is null)
                {
                    diagnostic = $"single_selection_unavailable hr=0x{hr:X}";
                    return false;
                }
                pattern = "text_selection";
            }

            hr = caret.CompareEndpoints(
                TextPatternRangeEndpointStart,
                caret,
                TextPatternRangeEndpointEnd,
                out int endpointDifference);
            if (hr != 0 || endpointDifference != 0)
            {
                diagnostic = $"selection_not_degenerate hr=0x{hr:X} delta={endpointDifference}";
                return false;
            }

            hr = caret.Clone(out IUIAutomationTextRange? preceding);
            if (hr != 0 || preceding is null)
            {
                diagnostic = $"caret_clone_failed hr=0x{hr:X}";
                return false;
            }

            hr = preceding.MoveEndpointByUnit(
                TextPatternRangeEndpointStart,
                TextUnitCharacter,
                -maxCharacters,
                out int moved);
            if (hr != 0)
            {
                diagnostic = $"caret_expand_failed hr=0x{hr:X}";
                return false;
            }

            hr = preceding.GetText(maxCharacters, out string? value);
            if (hr != 0 || value is null)
            {
                diagnostic = $"caret_text_failed hr=0x{hr:X}";
                return false;
            }

            int processId = GetInt(element, UIA_ProcessIdPropertyId);
            int controlType = GetInt(element, UIA_ControlTypePropertyId);
            int nativeWindowHandle = GetInt(element, UIA_NativeWindowHandlePropertyId);
            long foregroundWindow = NativeMethods.GetForegroundWindow().ToInt64();
            string runtimeId = element.GetRuntimeId(out int[] ids) == 0
                ? string.Join('.', ids)
                : string.Empty;

            text = new FocusedCaretText(
                value,
                ReachedDocumentStart: Math.Abs(moved) < maxCharacters,
                MovedCharacters: Math.Abs(moved),
                processId,
                controlType,
                nativeWindowHandle,
                foregroundWindow,
                runtimeId,
                pattern);
            diagnostic = "ok";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"uia_exception type={ex.GetType().Name} message={ex.Message}";
            return false;
        }
    }

    private static int GetInt(IUIAutomationElement element, int propertyId) =>
        element.GetCurrentPropertyValue(propertyId, out object value) == 0 && value is not null
            ? Convert.ToInt32(value)
            : 0;

    private static T? GetPattern<T>(IUIAutomationElement element, int patternId, Guid iid)
        where T : class
    {
        int hr = element.GetCurrentPatternAs(patternId, ref iid, out IntPtr pointer);
        if (hr != 0 || pointer == IntPtr.Zero) return null;

        try
        {
            return Marshal.GetObjectForIUnknown(pointer) as T;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [ComImport]
    [Guid("32EBA289-3583-42C9-9C59-3B6D9A1E9B6A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern
    {
        [PreserveSig] int RangeFromPoint(POINT point, out IUIAutomationTextRange? range);
        [PreserveSig] int RangeFromChild(IUIAutomationElement? child, out IUIAutomationTextRange? range);
        [PreserveSig] int GetSelection(out IUIAutomationTextRangeArray? ranges);
        [PreserveSig] int GetVisibleRanges(out IUIAutomationTextRangeArray? ranges);
        [PreserveSig] int GetDocumentRange(out IUIAutomationTextRange? range);
        [PreserveSig] int GetSupportedTextSelection(out int supportedTextSelection);
    }

    [ComImport]
    [Guid("506A921A-FCC9-409F-B23B-37EB74106872")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern2
    {
        [PreserveSig] int RangeFromPoint(POINT point, out IUIAutomationTextRange? range);
        [PreserveSig] int RangeFromChild(IUIAutomationElement? child, out IUIAutomationTextRange? range);
        [PreserveSig] int GetSelection(out IUIAutomationTextRangeArray? ranges);
        [PreserveSig] int GetVisibleRanges(out IUIAutomationTextRangeArray? ranges);
        [PreserveSig] int GetDocumentRange(out IUIAutomationTextRange? range);
        [PreserveSig] int GetSupportedTextSelection(out int supportedTextSelection);
        [PreserveSig] int RangeFromAnnotation(IUIAutomationElement? annotation, out IUIAutomationTextRange? range);
        [PreserveSig] int GetCaretRange(out int isActive, out IUIAutomationTextRange? range);
    }

    [ComImport]
    [Guid("CE4AE76A-E717-4C98-81EA-47371D028EB6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRangeArray
    {
        [PreserveSig] int GetLength(out int length);
        [PreserveSig] int GetElement(int index, out IUIAutomationTextRange? element);
    }

    [ComImport]
    [Guid("A543CC6A-F4AE-494B-8239-C814481187A8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        [PreserveSig] int Clone(out IUIAutomationTextRange? clonedRange);
        [PreserveSig] int Compare(IUIAutomationTextRange? range, out int areSame);
        [PreserveSig] int CompareEndpoints(int sourceEndpoint, IUIAutomationTextRange? range, int targetEndpoint, out int comparison);
        [PreserveSig] int ExpandToEnclosingUnit(int textUnit);
        [PreserveSig] int FindAttribute(int attribute, [MarshalAs(UnmanagedType.Struct)] object value, int backward, out IUIAutomationTextRange? found);
        [PreserveSig] int FindText([MarshalAs(UnmanagedType.BStr)] string text, int backward, int ignoreCase, out IUIAutomationTextRange? found);
        [PreserveSig] int GetAttributeValue(int attribute, [MarshalAs(UnmanagedType.Struct)] out object value);
        [PreserveSig] int GetBoundingRectangles(out IntPtr rectangles);
        [PreserveSig] int GetEnclosingElement(out IUIAutomationElement? element);
        [PreserveSig] int GetText(int maxLength, [MarshalAs(UnmanagedType.BStr)] out string? text);
        [PreserveSig] int Move(int textUnit, int count, out int moved);
        [PreserveSig] int MoveEndpointByUnit(int endpoint, int textUnit, int count, out int moved);
        [PreserveSig] int MoveEndpointByRange(int sourceEndpoint, IUIAutomationTextRange? range, int targetEndpoint);
        [PreserveSig] int Select();
        [PreserveSig] int AddToSelection();
        [PreserveSig] int RemoveFromSelection();
        [PreserveSig] int ScrollIntoView(int alignToTop);
        [PreserveSig] int GetChildren(out IntPtr children);
    }
}

public readonly record struct FocusedCaretText(
    string TextBeforeCaret,
    bool ReachedDocumentStart,
    int MovedCharacters,
    int ProcessId,
    int ControlType,
    int NativeWindowHandle,
    long ForegroundWindow,
    string RuntimeId,
    string Pattern)
{
    public string TargetIdentity =>
        $"pid={ProcessId};ctrl={ControlType};hwnd={NativeWindowHandle};"
        + $"foreground={ForegroundWindow};runtime={RuntimeId}";
}
