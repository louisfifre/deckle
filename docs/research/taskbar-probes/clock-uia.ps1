# Walk the raw UI Automation tree under the taskbar clock: every text block with
# its text and rectangle. The clock button of tray-uia.ps1 is one element; this
# shows the lines inside it, which is where a format change or a wrap shows.
# Physical pixels, like tray-uia.ps1.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace W -Name T -MemberDefinition '[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string n);'
$h = [W.T]::FindWindowW('Shell_TrayWnd', $null)
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$clock = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) | Where-Object { $_.Current.Name -like 'Clock*' } | Select-Object -First 1
if (-not $clock) { 'no clock button found'; exit 1 }

function Format-Rect($r) {
    if ([double]::IsInfinity($r.X) -or [double]::IsInfinity($r.Width)) { return 'offscreen' }
    return "x={0,5} y={1,5} w={2,3} h={3,3}" -f [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height
}

$walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
$parent = $walker.GetParent($clock)
"parent {0} [{1}] class={2} {3}" -f $parent.Current.ControlType.ProgrammaticName, $parent.Current.Name, $parent.Current.ClassName, (Format-Rect $parent.Current.BoundingRectangle)
"clock  {0} [{1}] class={2} {3}" -f $clock.Current.ControlType.ProgrammaticName, ($clock.Current.Name -replace "`n", ' / '), $clock.Current.ClassName, (Format-Rect $clock.Current.BoundingRectangle)

function Walk($e, $depth) {
    $c = $walker.GetFirstChild($e)
    while ($c) {
        "{0}{1} [{2}] class={3} id={4} {5}" -f ('  ' * $depth), $c.Current.ControlType.ProgrammaticName, ($c.Current.Name -replace "`n", ' / '), $c.Current.ClassName, $c.Current.AutomationId, (Format-Rect $c.Current.BoundingRectangle)
        Walk $c ($depth + 1)
        $c = $walker.GetNextSibling($c)
    }
}
Walk $clock 1
