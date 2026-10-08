# Measure the taskbar window, dump the taskbar registry values and take a screenshot of the primary screen.
param([string]$Label = 'probe')
$ErrorActionPreference = 'Continue'
Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string n);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
public struct RECT { public int L, T, R, B; }
'@
foreach ($cls in 'Shell_TrayWnd', 'Shell_SecondaryTrayWnd') {
    $h = [W.U]::FindWindowW($cls, $null)
    if ($h -eq [IntPtr]::Zero) { "$cls : not found"; continue }
    $r = New-Object W.U+RECT
    [void][W.U]::GetWindowRect($h, [ref]$r)
    "$cls : L=$($r.L) T=$($r.T) R=$($r.R) B=$($r.B) width=$($r.R - $r.L) height=$($r.B - $r.T) visible=$([W.U]::IsWindowVisible($h))"
}
$adv = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
"Advanced: " + (($adv.PSObject.Properties | Where-Object { $_.Name -like 'Taskbar*' } | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ' ')
$sr = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3' -ErrorAction SilentlyContinue).Settings
if ($sr) { "StuckRects3 edge=$($sr[12]) flags=$($sr[8])" }
"explorer: " + ((Get-Process explorer -ErrorAction SilentlyContinue | ForEach-Object { "pid=$($_.Id) started=$($_.StartTime.ToString('HH:mm:ss'))" }) -join ' ')
$wh = Get-Process explorer -ErrorAction SilentlyContinue | ForEach-Object { $_.Modules | Where-Object { $_.FileName -like '*windhawk*' } } | Measure-Object
"windhawk modules in explorer: $($wh.Count)"
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$g.Dispose()
$path = Join-Path $PSScriptRoot "screen-$Label.png"
$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"screenshot: $path ($($bounds.Width)x$($bounds.Height))"
