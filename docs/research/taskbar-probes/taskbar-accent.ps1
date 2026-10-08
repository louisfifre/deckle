# Apply or clear a DWM accent policy on the taskbar window, the way TranslucentTB does (no injection).
# Usage: taskbar-accent.ps1 -Mode clear | blur | acrylic | normal
param([ValidateSet('clear', 'blur', 'acrylic', 'normal')] [string]$Mode = 'clear', [uint32]$Color = 0)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Accent {
    [StructLayout(LayoutKind.Sequential)] public struct ACCENT_POLICY { public int AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)] public struct WCA_DATA { public int Attribute; public IntPtr Data; public int Size; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string n);
    [DllImport("user32.dll")] public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WCA_DATA data);
    public static int Apply(IntPtr hwnd, int state, uint color) {
        var policy = new ACCENT_POLICY { AccentState = state, AccentFlags = 0, GradientColor = color, AnimationId = 0 };
        int size = Marshal.SizeOf(policy);
        IntPtr p = Marshal.AllocHGlobal(size);
        try {
            Marshal.StructureToPtr(policy, p, false);
            var data = new WCA_DATA { Attribute = 19, Data = p, Size = size };   // WCA_ACCENT_POLICY
            return SetWindowCompositionAttribute(hwnd, ref data);
        } finally { Marshal.FreeHGlobal(p); }
    }
}
'@
$state = switch ($Mode) { 'normal' { 0 } 'clear' { 2 } 'blur' { 3 } 'acrylic' { 4 } }
$h = [Accent]::FindWindowW('Shell_TrayWnd', $null)
if ($h -eq [IntPtr]::Zero) { throw 'Shell_TrayWnd not found' }
$r = [Accent]::Apply($h, $state, $Color)
"SetWindowCompositionAttribute(Shell_TrayWnd, state=$state '$Mode', color=0x$($Color.ToString('X8'))) -> $r (nonzero = success)"
