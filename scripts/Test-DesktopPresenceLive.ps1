[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExecutablePath,

    [string]$DotnetRoot = '',

    [int]$TimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$executable = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Control Center executable not found: $executable"
}
$originalDotnetRoot = $env:DOTNET_ROOT
$originalDotnetRootX64 = $env:DOTNET_ROOT_X64
if (-not [string]::IsNullOrWhiteSpace($DotnetRoot)) {
    $resolvedDotnetRoot = [IO.Path]::GetFullPath($DotnetRoot)
    if (-not (Test-Path -LiteralPath (
            Join-Path $resolvedDotnetRoot 'host\fxr') -PathType Container)) {
        throw "The supplied .NET root has no host\\fxr: $resolvedDotnetRoot"
    }
    $env:DOTNET_ROOT = $resolvedDotnetRoot
    $env:DOTNET_ROOT_X64 = $resolvedDotnetRoot
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class JarvisDesktopPresenceNative
{
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(
        EnumWindowsProc callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(
        IntPtr window,
        StringBuilder value,
        int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(
        IntPtr window,
        int identifier,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(
        IntPtr window,
        int identifier);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

}
'@

$failures = [Collections.Generic.List[string]]::new()
$launched = $null
$windowHandle = [IntPtr]::Zero
$initiallyHidden = $false
$productionChordRegistered = $false
$summonStatus = ''
$summonHelpText = ''
$attentionStatus = ''
$attentionDeliveryStatus = ''
$hotKeyMessageRestoredWindow = $false
$hotKeyRestoredForeground = $false
$keyboardFocusReturnedToPrimary = $false
$foregroundVerification = 'not-attempted'
$summonRestoredMaximizedState = $false
$closeHidWithoutExit = $false
$secondaryActivatedPrimary = $false
$orderlyExitPassed = $false
$forcedCleanup = $false

function Get-OwnedWindow {
    param([int]$ProcessId)

    $matches = [Collections.Generic.List[object]]::new()
    $callback = [JarvisDesktopPresenceNative+EnumWindowsProc]{
        param([IntPtr]$candidate, [IntPtr]$parameter)
        $candidateProcessId = [uint32]0
        [void][JarvisDesktopPresenceNative]::GetWindowThreadProcessId(
            $candidate,
            [ref]$candidateProcessId)
        if ($candidateProcessId -eq [uint32]$ProcessId) {
            $text = [Text.StringBuilder]::new(256)
            [void][JarvisDesktopPresenceNative]::GetWindowText(
                $candidate,
                $text,
                $text.Capacity)
            if ($text.ToString().StartsWith(
                    'JarvisV2',
                    [StringComparison]::Ordinal)) {
                $matches.Add([pscustomobject]@{
                    handle = $candidate
                    title = $text.ToString()
                    visible = [JarvisDesktopPresenceNative]::IsWindowVisible(
                        $candidate)
                })
            }
        }
        return $true
    }
    [void][JarvisDesktopPresenceNative]::EnumWindows(
        $callback,
        [IntPtr]::Zero)
    return $matches | Select-Object -First 1
}

function Wait-Until {
    param(
        [Parameter(Mandatory)] [scriptblock]$Condition,
        [Parameter(Mandatory)] [int]$Seconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    do {
        if (& $Condition) {
            return $true
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    return $false
}

try {
    $launched = Start-Process `
        -FilePath $executable `
        -ArgumentList @('--resume-latest', '--minimized') `
        -WindowStyle Hidden `
        -PassThru

    $windowFound = Wait-Until -Seconds $TimeoutSeconds -Condition {
        if ($launched.HasExited) {
            return $false
        }
        $script:ownedWindow = Get-OwnedWindow -ProcessId $launched.Id
        $null -ne $script:ownedWindow
    }
    if (-not $windowFound) {
        if ($launched.HasExited) {
            throw "The launched Control Center exited with code $($launched.ExitCode)."
        }
        throw 'The launched Control Center did not create its owned window.'
    }
    $windowHandle = $script:ownedWindow.handle
    $initiallyHidden = -not [JarvisDesktopPresenceNative]::IsWindowVisible(
        $windowHandle)
    if (-not $initiallyHidden) {
        $failures.Add('minimized-launch-was-visible')
    }

    if (-not [JarvisDesktopPresenceNative]::PostMessage(
            $windowHandle,
            0x0312,
            [IntPtr]0x4A32,
            [IntPtr]0x004A0003)) {
        $failures.Add('wm-hotkey-post-failed')
    }
    $hotKeyMessageRestoredWindow = Wait-Until `
        -Seconds 8 `
        -Condition {
            [JarvisDesktopPresenceNative]::IsWindowVisible($windowHandle)
        }
    if (-not $hotKeyMessageRestoredWindow) {
        $failures.Add('hot-key-message-did-not-restore-window')
    }

    $helperIdentifier = 0x4A33
    $helperRegistered = [JarvisDesktopPresenceNative]::RegisterHotKey(
        [IntPtr]::Zero,
        $helperIdentifier,
        0x4003,
        0x4A)
    if ($helperRegistered) {
        [void][JarvisDesktopPresenceNative]::UnregisterHotKey(
            [IntPtr]::Zero,
            $helperIdentifier)
        $failures.Add('production-ctrl-alt-j-was-not-registered')
    }
    else {
        $productionChordRegistered = [Runtime.InteropServices.Marshal]::GetLastWin32Error() -eq 1409
        if (-not $productionChordRegistered) {
            $failures.Add('production-hot-key-registration-state-unknown')
        }
    }

    $automationRoot = [System.Windows.Automation.AutomationElement]::FromHandle(
        $windowHandle)
    $summonCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        'SummonHotKeyStatus')
    $summonElement = $automationRoot.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $summonCondition)
    if ($null -ne $summonElement) {
        $summonStatus = $summonElement.Current.Name
        $summonHelpText = $summonElement.Current.HelpText
    }
    $attentionCondition =
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            'AttentionStatus')
    $attentionElement = $automationRoot.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $attentionCondition)
    if ($null -eq $attentionElement) {
        $failures.Add('attention-status-not-found-by-automation')
    }
    else {
        $attentionStatus = $attentionElement.Current.Name
        if ([string]::IsNullOrWhiteSpace($attentionStatus)) {
            $failures.Add('attention-status-was-empty')
        }
    }
    $attentionDeliveryCondition =
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            'AttentionDeliveryStatus')
    $attentionDeliveryElement = $automationRoot.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $attentionDeliveryCondition)
    if ($null -eq $attentionDeliveryElement) {
        $failures.Add('attention-delivery-not-found-by-automation')
    }
    else {
        $attentionDeliveryStatus = $attentionDeliveryElement.Current.Name
        if ($attentionDeliveryStatus -notin @(
                'LAST HIDDEN SIGNAL // NONE',
                '最近隐藏信号 // 无')) {
            $failures.Add('attention-delivery-replayed-stale-signal')
        }
    }
    [void][JarvisDesktopPresenceNative]::PostMessage(
        $windowHandle,
        0x0112,
        [IntPtr]0xF030,
        [IntPtr]::Zero)
    $maximized = Wait-Until -Seconds 5 -Condition {
        [JarvisDesktopPresenceNative]::IsZoomed($windowHandle)
    }
    [void][JarvisDesktopPresenceNative]::PostMessage(
        $windowHandle,
        0x0112,
        [IntPtr]0xF020,
        [IntPtr]::Zero)
    $minimized = Wait-Until -Seconds 5 -Condition {
        [JarvisDesktopPresenceNative]::IsIconic($windowHandle)
    }
    [void][JarvisDesktopPresenceNative]::PostMessage(
        $windowHandle,
        0x0312,
        [IntPtr]0x4A32,
        [IntPtr]0x004A0003)
    $summonRestoredMaximizedState =
        $maximized -and
        $minimized -and
        (Wait-Until -Seconds 5 -Condition {
            -not [JarvisDesktopPresenceNative]::IsIconic($windowHandle) -and
            [JarvisDesktopPresenceNative]::IsZoomed($windowHandle)
        })
    if (-not $summonRestoredMaximizedState) {
        $failures.Add('summon-did-not-restore-maximized-state')
    }
    $hotKeyRestoredForeground = Wait-Until -Seconds 5 -Condition {
        [JarvisDesktopPresenceNative]::GetForegroundWindow() -eq $windowHandle
    }
    $keyboardFocusReturnedToPrimary = Wait-Until -Seconds 5 -Condition {
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        $null -ne $focused -and $focused.Current.ProcessId -eq $launched.Id
    }
    $foregroundVerification = if (
        $hotKeyRestoredForeground -and $keyboardFocusReturnedToPrimary) {
        'observed'
    }
    else {
        'not-observed-background-runner'
    }

    [void][JarvisDesktopPresenceNative]::PostMessage(
        $windowHandle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    $closeHidWithoutExit = Wait-Until -Seconds 8 -Condition {
        -not $launched.HasExited -and
        -not [JarvisDesktopPresenceNative]::IsWindowVisible($windowHandle)
    }
    if (-not $closeHidWithoutExit) {
        $failures.Add('close-did-not-hide-live-primary')
    }

    $secondary = Start-Process `
        -FilePath $executable `
        -WindowStyle Hidden `
        -PassThru
    if (-not $secondary.WaitForExit(8000)) {
        $secondary.Kill()
        $failures.Add('secondary-instance-did-not-exit')
    }
    elseif ($secondary.ExitCode -ne 0) {
        $failures.Add("secondary-instance-exit-$($secondary.ExitCode)")
    }
    $secondaryActivatedPrimary = Wait-Until -Seconds 8 -Condition {
        [JarvisDesktopPresenceNative]::IsWindowVisible($windowHandle)
    }
    if (-not $secondaryActivatedPrimary) {
        $failures.Add('secondary-instance-did-not-activate-primary')
    }
    $exitCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        'ExitJarvisButton')
    $exitElement = $automationRoot.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $exitCondition)
    if ($null -eq $exitElement) {
        $failures.Add('exit-action-not-found-by-automation')
    }
    else {
        $invoke = [System.Windows.Automation.InvokePattern]$exitElement.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)
        $invoke.Invoke()
        $orderlyExitPassed = $launched.WaitForExit($TimeoutSeconds * 1000)
        if (-not $orderlyExitPassed) {
            $failures.Add('explicit-exit-did-not-stop-primary')
        }
    }
}
catch {
    $failures.Add("live-presence-probe: $($_.Exception.Message)")
}
finally {
    if ($null -ne $launched -and -not $launched.HasExited) {
        $forcedCleanup = $true
        $launched.Kill()
        [void]$launched.WaitForExit(10000)
        $failures.Add('live-primary-required-forced-cleanup')
    }
    $env:DOTNET_ROOT = $originalDotnetRoot
    $env:DOTNET_ROOT_X64 = $originalDotnetRootX64
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-live-desktop-presence-probe'
    result = if ($passed) { 'passed' } else { 'failed' }
    executablePath = $executable
    processId = if ($null -eq $launched) { 0 } else { $launched.Id }
    processExitCode = if ($null -ne $launched -and $launched.HasExited) {
        $launched.ExitCode
    } else {
        $null
    }
    initiallyHidden = $initiallyHidden
    productionChordRegistered = $productionChordRegistered
    summonStatus = $summonStatus
    summonHelpText = $summonHelpText
    attentionStatus = $attentionStatus
    attentionDeliveryStatus = $attentionDeliveryStatus
    hotKeyMessageRestoredWindow = $hotKeyMessageRestoredWindow
    hotKeyRestoredForeground = $hotKeyRestoredForeground
    keyboardFocusReturnedToPrimary = $keyboardFocusReturnedToPrimary
    foregroundVerification = $foregroundVerification
    summonRestoredMaximizedState = $summonRestoredMaximizedState
    closeHidWithoutExit = $closeHidWithoutExit
    secondaryActivatedPrimary = $secondaryActivatedPrimary
    orderlyExitPassed = $orderlyExitPassed
    forcedCleanup = $forcedCleanup
    failures = $failures
} | ConvertTo-Json -Depth 4

if (-not $passed) {
    exit 1
}
