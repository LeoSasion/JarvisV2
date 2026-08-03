[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExecutablePath,

    [string]$DotnetRoot = '',

    [string]$NodePath = '',

    [string]$SidecarPath = '',

    [string]$ScreenshotPath = '',

    [int]$TimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$executable = [IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Control Center executable not found: $executable"
}

$repositoryRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts'))
$workspaceRoot = [IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot (
        'live-attention-routing-' + [Guid]::NewGuid().ToString('N'))))
if (-not $workspaceRoot.StartsWith(
        $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The live attention fixture escaped the repository artifacts root.'
}
[IO.Directory]::CreateDirectory($workspaceRoot) | Out-Null
[IO.File]::WriteAllText(
    (Join-Path $workspaceRoot 'attention-probe.txt'),
    "JARVIS2 attention router live fixture.`r`n")

$workspaceCanonical = [IO.Path]::TrimEndingDirectorySeparator(
    [IO.Path]::GetFullPath($workspaceRoot)).ToUpperInvariant()
$workspaceBytes = [Text.Encoding]::UTF8.GetBytes($workspaceCanonical)
try {
    $workspaceId = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($workspaceBytes)).ToLowerInvariant()
}
finally {
    [Array]::Clear($workspaceBytes, 0, $workspaceBytes.Length)
}
$checkpointRoot = Join-Path $env:LOCALAPPDATA 'JARVIS2\PiAgent\conversations'
$checkpointPath = Join-Path $checkpointRoot "$workspaceId.j2checkpoint"
if (Test-Path -LiteralPath $checkpointPath) {
    throw "Unexpected pre-existing attention fixture checkpoint: $checkpointPath"
}

$capturePath = if ([string]::IsNullOrWhiteSpace($ScreenshotPath)) {
    ''
} else {
    [IO.Path]::GetFullPath($ScreenshotPath)
}

$launchArguments = @(
    '--conversation',
    '--workspace',
    $workspaceRoot,
    '--provider',
    'local')
if (-not [string]::IsNullOrWhiteSpace($NodePath) -or
    -not [string]::IsNullOrWhiteSpace($SidecarPath)) {
    if ([string]::IsNullOrWhiteSpace($NodePath) -or
        [string]::IsNullOrWhiteSpace($SidecarPath)) {
        throw 'NodePath and SidecarPath must be supplied together.'
    }
    $node = [IO.Path]::GetFullPath($NodePath)
    $sidecar = [IO.Path]::GetFullPath($SidecarPath)
    if (-not (Test-Path -LiteralPath $node -PathType Leaf) -or
        -not (Test-Path -LiteralPath $sidecar -PathType Leaf)) {
        throw 'The diagnostic Node or sidecar path does not exist.'
    }
    $launchArguments = @(
        '--diagnostic-conversation',
        '--node',
        $node,
        '--sidecar',
        $sidecar,
        '--workspace',
        $workspaceRoot)
}
if (-not [string]::IsNullOrWhiteSpace($capturePath)) {
    [IO.Directory]::CreateDirectory(
        [IO.Path]::GetDirectoryName($capturePath)) | Out-Null
}

$originalDotnetRoot = $env:DOTNET_ROOT
$originalDotnetRootX64 = $env:DOTNET_ROOT_X64
if (-not [string]::IsNullOrWhiteSpace($DotnetRoot)) {
    $resolvedDotnetRoot = [IO.Path]::GetFullPath($DotnetRoot)
    if (-not (Test-Path -LiteralPath (
            Join-Path $resolvedDotnetRoot 'host\fxr') -PathType Container)) {
        throw "The supplied .NET root has no host\fxr: $resolvedDotnetRoot"
    }
    $env:DOTNET_ROOT = $resolvedDotnetRoot
    $env:DOTNET_ROOT_X64 = $resolvedDotnetRoot
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class JarvisAttentionRoutingNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(
        IntPtr window,
        IntPtr deviceContext,
        uint flags);
}
'@

$failures = [Collections.Generic.List[string]]::new()
$launched = $null
$windowHandle = [IntPtr]::Zero
$attentionStatus = ''
$attentionActionName = ''
$turnTargetDiscovered = $false
$targetFocused = $false
$focusedName = ''
$focusedHelpText = ''
$focusedAutomationId = ''
$focusedControlType = ''
$checkpointCreated = $false
$checkpointRemoved = $false
$fixtureRemoved = $false
$orderlyExitPassed = $false
$forcedCleanup = $false
$screenshotCaptured = $false
$screenshotMethod = ''
$screenshotUsefulPixels = $false

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

function Find-AutomationElement {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$Root,

        [Parameter(Mandatory)]
        [string]$AutomationId
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

try {
    $launched = Start-Process `
        -FilePath $executable `
        -ArgumentList $launchArguments `
        -WindowStyle Normal `
        -PassThru

    $windowFound = Wait-Until -Seconds $TimeoutSeconds -Condition {
        if ($launched.HasExited) {
            return $false
        }
        $launched.Refresh()
        $script:windowHandle = $launched.MainWindowHandle
        return $script:windowHandle -ne [IntPtr]::Zero
    }
    if (-not $windowFound) {
        if ($launched.HasExited) {
            throw "The launched Control Center exited with code $($launched.ExitCode)."
        }
        throw 'The Control Center did not create its owned conversation window.'
    }

    $automationRoot = [System.Windows.Automation.AutomationElement]::FromHandle(
        $windowHandle)
    $prompt = Find-AutomationElement `
        -Root $automationRoot `
        -AutomationId 'PromptInput'
    $submit = Find-AutomationElement `
        -Root $automationRoot `
        -AutomationId 'SubmitButton'
    if ($null -eq $prompt -or $null -eq $submit) {
        throw 'The live composer was not discoverable through UI Automation.'
    }
    $composerReady = Wait-Until -Seconds $TimeoutSeconds -Condition {
        $prompt.Current.IsEnabled -and $submit.Current.IsEnabled
    }
    if (-not $composerReady) {
        throw 'The local Pi runtime did not enable the live composer.'
    }

    $promptValue = [System.Windows.Automation.ValuePattern]$prompt.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern)
    $promptValue.SetValue(
        'Verify the content-free attention router focus target.')
    $submitInvoke = [System.Windows.Automation.InvokePattern]$submit.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern)
    $submitInvoke.Invoke()

    $attentionReady = Wait-Until -Seconds $TimeoutSeconds -Condition {
        $script:openAttention = Find-AutomationElement `
            -Root $automationRoot `
            -AutomationId 'OpenAttentionButton'
        return $null -ne $script:openAttention -and
            $script:openAttention.Current.IsEnabled -and
            -not $script:openAttention.Current.IsOffscreen
    }
    if (-not $attentionReady) {
        $script:openAttention = Find-AutomationElement `
            -Root $automationRoot `
            -AutomationId 'OpenAttentionButton'
        if ($null -ne $script:openAttention) {
            try {
                $scrollItem = [System.Windows.Automation.ScrollItemPattern](
                    $script:openAttention.GetCurrentPattern(
                        [System.Windows.Automation.ScrollItemPattern]::Pattern))
                $scrollItem.ScrollIntoView()
            }
            catch {
                # The inspector may already expose the action without ScrollItemPattern.
            }
            $attentionReady = Wait-Until -Seconds 5 -Condition {
                -not $script:openAttention.Current.IsOffscreen
            }
        }
    }
    if (-not $attentionReady -or $null -eq $script:openAttention) {
        throw 'The completed turn did not expose a visible attention route action.'
    }

    $attentionActionName = $script:openAttention.Current.Name
    $turnTarget = Find-AutomationElement `
        -Root $automationRoot `
        -AutomationId 'AttentionTurnTarget'
    $turnTargetDiscovered = $null -ne $turnTarget
    if (-not $turnTargetDiscovered) {
        $failures.Add('attention-turn-target-not-found-by-automation')
    }
    $attentionElement = Find-AutomationElement `
        -Root $automationRoot `
        -AutomationId 'AttentionStatus'
    if ($null -ne $attentionElement) {
        $attentionStatus = $attentionElement.Current.Name
    }

    try {
        $scrollItem = [System.Windows.Automation.ScrollItemPattern](
            $script:openAttention.GetCurrentPattern(
                [System.Windows.Automation.ScrollItemPattern]::Pattern))
        $scrollItem.ScrollIntoView()
    }
    catch {
        # Visibility was already verified above.
    }

    $openInvoke = [System.Windows.Automation.InvokePattern](
        $script:openAttention.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern))
    $openInvoke.Invoke()

    $targetFocused = Wait-Until -Seconds 8 -Condition {
        if ($null -ne $turnTarget -and $turnTarget.Current.HasKeyboardFocus) {
            $script:focused = $turnTarget
            return $true
        }
        $script:focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        if ($null -eq $script:focused -or
            $script:focused.Current.ProcessId -ne $launched.Id) {
            return $false
        }
        return
            $script:focused.Current.AutomationId -eq
                'AttentionTurnTarget' -and
            $script:focused.Current.Name -in @(
                'Attention target conversation turn',
                '注意目标对话回合')
    }
    if (-not $targetFocused) {
        $failures.Add('attention-route-did-not-focus-generic-turn-target')
    }
    if ($null -ne $script:focused) {
        $focusedName = $script:focused.Current.Name
        $focusedHelpText = $script:focused.Current.HelpText
        $focusedAutomationId = $script:focused.Current.AutomationId
        $focusedControlType = $script:focused.Current.ControlType.ProgrammaticName
    }
    if ($focusedName.Contains('Verify the content-free', [StringComparison]::Ordinal)) {
        $failures.Add('focused-automation-name-exposed-prompt-content')
    }
    if ($focusedAutomationId -in @('SubmitButton', 'OpenAttentionButton')) {
        $failures.Add('attention-route-focused-an-action-control')
    }

    if (-not [string]::IsNullOrWhiteSpace($capturePath)) {
        $rect = [JarvisAttentionRoutingNative+Rect]::new()
        if (-not [JarvisAttentionRoutingNative]::GetWindowRect(
                $windowHandle,
                [ref]$rect)) {
            throw 'GetWindowRect failed for the attention routing screenshot.'
        }
        $width = $rect.Right - $rect.Left
        $height = $rect.Bottom - $rect.Top
        $bitmap = [Drawing.Bitmap]::new($width, $height)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                try {
                    $graphics.CopyFromScreen(
                        $rect.Left,
                        $rect.Top,
                        0,
                        0,
                        [Drawing.Size]::new($width, $height))
                    $screenshotMethod = 'copy-from-visible-screen'
                }
                catch {
                    $deviceContext = $graphics.GetHdc()
                    try {
                        if (-not [JarvisAttentionRoutingNative]::PrintWindow(
                                $windowHandle,
                                $deviceContext,
                                2)) {
                            throw 'PrintWindow failed for the attention routing screenshot.'
                        }
                        $screenshotMethod = 'print-window-render-full-content'
                    }
                    finally {
                        $graphics.ReleaseHdc($deviceContext)
                    }
                }
            }
            finally {
                $graphics.Dispose()
            }
            $bitmap.Save($capturePath, [Drawing.Imaging.ImageFormat]::Png)
            $screenshotCaptured = Test-Path -LiteralPath $capturePath -PathType Leaf
            $darkest = 255
            $brightest = 0
            for ($x = 0; $x -lt $bitmap.Width; $x += 16) {
                for ($y = 0; $y -lt $bitmap.Height; $y += 16) {
                    $pixel = $bitmap.GetPixel($x, $y)
                    $localDarkest = [Math]::Min(
                        $pixel.R,
                        [Math]::Min($pixel.G, $pixel.B))
                    $localBrightest = [Math]::Max(
                        $pixel.R,
                        [Math]::Max($pixel.G, $pixel.B))
                    $darkest = [Math]::Min($darkest, $localDarkest)
                    $brightest = [Math]::Max($brightest, $localBrightest)
                }
            }
            $screenshotUsefulPixels =
                $brightest -ge 64 -and
                ($brightest - $darkest) -ge 32
            if (-not $screenshotUsefulPixels) {
                $failures.Add('attention-routing-screenshot-was-blank')
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }

    $exitElement = Find-AutomationElement `
        -Root $automationRoot `
        -AutomationId 'ExitJarvisButton'
    if ($null -eq $exitElement) {
        $failures.Add('exit-action-not-found-by-automation')
    }
    else {
        $exitInvoke = [System.Windows.Automation.InvokePattern](
            $exitElement.GetCurrentPattern(
                [System.Windows.Automation.InvokePattern]::Pattern))
        $exitInvoke.Invoke()
        $orderlyExitPassed = $launched.WaitForExit($TimeoutSeconds * 1000)
        if (-not $orderlyExitPassed) {
            $failures.Add('explicit-exit-did-not-stop-primary')
        }
    }
}
catch {
    $failures.Add("live-attention-route-probe: $($_.Exception.Message)")
}
finally {
    if ($null -ne $launched -and -not $launched.HasExited) {
        $forcedCleanup = $true
        $launched.Kill()
        [void]$launched.WaitForExit(10000)
        $failures.Add('live-attention-primary-required-forced-cleanup')
    }

    $checkpointCreated = Test-Path -LiteralPath $checkpointPath -PathType Leaf
    if ($checkpointCreated) {
        Remove-Item -LiteralPath $checkpointPath -Force
    }
    $checkpointRemoved = -not (Test-Path -LiteralPath $checkpointPath)
    if (-not $checkpointRemoved) {
        $failures.Add('attention-fixture-checkpoint-was-not-removed')
    }

    $resolvedFixture = [IO.Path]::GetFullPath($workspaceRoot)
    if ($resolvedFixture.StartsWith(
            $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedFixture).StartsWith(
            'live-attention-routing-',
            [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
    $fixtureRemoved = -not (Test-Path -LiteralPath $resolvedFixture)
    if (-not $fixtureRemoved) {
        $failures.Add('attention-live-fixture-was-not-removed')
    }

    $env:DOTNET_ROOT = $originalDotnetRoot
    $env:DOTNET_ROOT_X64 = $originalDotnetRootX64
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-live-desktop-attention-routing-probe'
    result = if ($passed) { 'passed' } else { 'failed' }
    executablePath = $executable
    processId = if ($null -eq $launched) { 0 } else { $launched.Id }
    attentionStatus = $attentionStatus
    attentionActionName = $attentionActionName
    turnTargetDiscovered = $turnTargetDiscovered
    targetFocused = $targetFocused
    focusedName = $focusedName
    focusedHelpText = $focusedHelpText
    focusedAutomationId = $focusedAutomationId
    focusedControlType = $focusedControlType
    screenshotPath = $capturePath
    screenshotCaptured = $screenshotCaptured
    screenshotMethod = $screenshotMethod
    screenshotUsefulPixels = $screenshotUsefulPixels
    checkpointCreated = $checkpointCreated
    checkpointRemoved = $checkpointRemoved
    fixtureRemoved = $fixtureRemoved
    orderlyExitPassed = $orderlyExitPassed
    forcedCleanup = $forcedCleanup
    failures = $failures
} | ConvertTo-Json -Depth 4

if (-not $passed) {
    exit 1
}
