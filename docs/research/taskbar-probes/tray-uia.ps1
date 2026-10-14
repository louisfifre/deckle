# Enumerate the buttons the taskbar exposes through UI Automation: task buttons, tray icons, clock.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace W -Name T -MemberDefinition '[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string n);'
$h = [W.T]::FindWindowW('Shell_TrayWnd', $null)
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
"== $($buttons.Count) buttons in Shell_TrayWnd"
foreach ($b in $buttons) {
    $r = $b.Current.BoundingRectangle
    # An element without a visible rectangle reports infinite coordinates; print it as "offscreen" instead of failing the int cast.
    if ([double]::IsInfinity($r.X) -or [double]::IsInfinity($r.Width)) {
        "{0,-60} offscreen id={1}" -f $b.Current.Name, $b.Current.AutomationId
        continue
    }
    "{0,-60} x={1,5} y={2,5} w={3,3} h={4,3} id={5}" -f $b.Current.Name, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height, $b.Current.AutomationId
}
