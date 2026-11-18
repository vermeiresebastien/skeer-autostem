# Can Live's startup modal be answered without touching the mouse?
#
# Two candidates, each verified by re-listing the windows Live owns:
#   A. UIA InvokePattern on the button - what the product does today, measured as a no-op
#      on this dialog, so it is re-tested here rather than assumed.
#   B. A posted WM_KEYDOWN/WM_KEYUP - Live ignores synthetic *mouse* messages (measured),
#      but keyboard accelerators travel through a different path in its message loop.
# Nothing in this script moves the cursor, activates a window or clicks.
param([int]$LivePid = 0)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -Namespace Skeer -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true)]
public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
'@ -Name Post

function Get-LiveWindows([int]$pid_) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pid_)
    @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $cond))
}

function Test-HandleAlive([int]$handle, [int]$pid_) {
    if ($handle -eq 0) { return $false }
    (@(Get-LiveWindows $pid_ | Where-Object { $_.Current.NativeWindowHandle -eq $handle })).Count -gt 0
}

if ($LivePid -eq 0) {
    $procs = Get-Process | Where-Object { $_.ProcessName -eq 'Ableton Live 12 Suite' }
    if (-not $procs) { 'NO LIVE PROCESS'; exit 1 }
    $LivePid = $procs[0].Id
}
"live pid = $LivePid"

# Preference order, not tree order: the modal's title-bar "Close" comes before the answer.
$targets = @("Don't Recover", "Don’t Recover", "No", "OK", "Cancel")

$modal = $null
$wanted = $null
foreach ($w in (Get-LiveWindows $LivePid)) {
    $buttons = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button))))
    "window handle=$($w.Current.NativeWindowHandle) '$($w.Current.Name)' " +
        "rect=$($w.Current.BoundingRectangle) buttons=$(($buttons | ForEach-Object { $_.Current.Name }) -join '|')"
    foreach ($t in $targets) {
        $hit = $buttons | Where-Object { $_.Current.Name.Trim() -eq $t } | Select-Object -First 1
        if ($hit) { $wanted = $hit; break }
    }
    if ($wanted) { $modal = $w; break }
}

if (-not $wanted) { 'VERDICT: no answerable modal is up'; exit 0 }

$handle = $modal.Current.NativeWindowHandle
$label = $wanted.Current.Name.Trim()
"target: '$label' on modal handle $handle"

# A. InvokePattern
try {
    $inv = $wanted.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $inv.Invoke()
    "A. Invoke() returned without error"
} catch {
    "A. Invoke() failed: $($_.Exception.Message.Split("`n")[0])"
}
Start-Sleep -Milliseconds 1500
$aliveAfterInvoke = Test-HandleAlive $handle $LivePid
"A. read-back: $(if ($aliveAfterInvoke) { 'modal still there' } { 'modal GONE' })"
if (-not $aliveAfterInvoke) { 'VERDICT: InvokePattern answers it, no mouse'; exit 0 }

# B. A posted key. Escape is the conventional "no" on a two-button question; VK 78 is 'N',
# which is the literal accelerator on "No", so it is tried second and only if Escape left
# the modal up.
foreach ($vk in @(27, 78)) {
    [void][Skeer.Post]::PostMessage([IntPtr]$handle, 0x0100, [IntPtr]$vk, [IntPtr]0)
    [void][Skeer.Post]::PostMessage([IntPtr]$handle, 0x0101, [IntPtr]$vk, [IntPtr]0)
    "B. posted VK $vk to the modal"
    Start-Sleep -Milliseconds 1500
    if (-not (Test-HandleAlive $handle $LivePid)) {
        "VERDICT: a posted key (VK $vk) answers it, no mouse"
        exit 0
    }
    "B. read-back after VK ${vk}: modal still there"
}

'C. re-listing windows to see what the posted keys did to Live'
foreach ($w in (Get-LiveWindows $LivePid)) {
    "  handle=$($w.Current.NativeWindowHandle) '$($w.Current.Name)' rect=$($w.Current.BoundingRectangle)"
}
'VERDICT: neither Invoke nor a posted key dismissed the modal'
