[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview')
$modelRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.RgbThemeModel')
$visualEffectsRoot = Join-Path $root (
    'src\common\Jarvis.VisualEffects')
$projectPath = Join-Path $sourceRoot (
    'Jarvis.Win10.NeuralVoidPreview.csproj')
$surfacePath = Join-Path $sourceRoot (
    'DesktopShellSurface.xaml')
$surfaceCodePath = Join-Path $sourceRoot (
    'DesktopShellSurface.xaml.cs')
$layoutCatalogPath = Join-Path $sourceRoot 'LayoutCatalog.cs'
$layoutGlyphPath = Join-Path $sourceRoot 'LayoutGlyph.cs'
$vectorLayerPath = Join-Path $sourceRoot (
    'NeuralVectorLayer.cs')
$vectorSceneFactoryPath = Join-Path $sourceRoot (
    'Win10NeuralVectorSceneFactory.cs')
$apertureVectorFactoryPath = Join-Path $sourceRoot (
    'Win10ApertureVectorSceneFactory.cs')
$wpfVectorRendererPath = Join-Path $sourceRoot (
    'WpfRetainedVectorSceneRenderer.cs')
$wpfVectorScenariosPath = Join-Path $sourceRoot (
    'WpfVectorAdapterScenarios.cs')
$apertureFramePath = Join-Path $sourceRoot (
    'ApertureFrame.cs')
$windowPath = Join-Path $sourceRoot 'MainWindow.xaml'
$themePath = Join-Path $root (
    'config\windows10-neural-void-rgb-theme.json')
$renderRoot = Join-Path $root (
    'artifacts\win10-neural-void-preview-tests')
$currentFeatherBaselinePath = Join-Path $root (
    'docs\screenshots\jarvis-win10-current-ui-baseline.png')
$approvedFeatherTargetSha256 =
    '42AD07963D7BA732F5FBC3EABC3B15E28F1E38AACFA13C0243FA69870D6168E2'

$checks = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()

function Add-Check {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [bool]$Passed,
        [Parameter(Mandatory)] [string]$Detail
    )

    $checks.Add([pscustomobject]@{
        name = $Name
        passed = $Passed
        detail = $Detail
    })
    if (-not $Passed) {
        $failures.Add("${Name}: ${Detail}")
    }
}

$sourceText = @(
    foreach ($currentSourceRoot in @(
        $sourceRoot,
        $modelRoot,
        $visualEffectsRoot
    )) {
        Get-ChildItem -LiteralPath $currentSourceRoot -File -Recurse |
            Where-Object Extension -In @('.cs', '.csproj', '.xaml') |
            Sort-Object FullName |
            ForEach-Object {
                [IO.File]::ReadAllText($_.FullName)
            }
    }
) -join [Environment]::NewLine
$surfaceText = [IO.File]::ReadAllText($surfacePath)
$surfaceCodeText = [IO.File]::ReadAllText($surfaceCodePath)
$layoutCatalogText = [IO.File]::ReadAllText($layoutCatalogPath)
$layoutGlyphText = [IO.File]::ReadAllText($layoutGlyphPath)
$vectorLayerText = [IO.File]::ReadAllText($vectorLayerPath)
$vectorSceneFactoryText =
    [IO.File]::ReadAllText($vectorSceneFactoryPath)
$apertureVectorFactoryText =
    [IO.File]::ReadAllText($apertureVectorFactoryPath)
$wpfVectorRendererText =
    [IO.File]::ReadAllText($wpfVectorRendererPath)
$wpfVectorScenariosText =
    [IO.File]::ReadAllText($wpfVectorScenariosPath)
$apertureFrameText = [IO.File]::ReadAllText($apertureFramePath)
$windowText = [IO.File]::ReadAllText($windowPath)
$themeText = [IO.File]::ReadAllText($themePath)

$forbiddenMutationPattern = (
    '(?i)\b(?:DllImport|LibraryImport|GetProcessesByName|Process\.|' +
    'Registry|SendMessage|PostMessage|SetWindowLong|SetWindowPos|' +
    'DwmSetWindowAttribute|SetWindowsHookEx|OpenProcess|' +
    'CreateRemoteThread|WriteProcessMemory|HidD_|SetupDi|' +
    'DeviceIoControl|Windhawk|Start-Process|Stop-Process)\b'
)
Add-Check `
    -Name 'source.owned-process-render-only' `
    -Passed (-not [regex]::IsMatch(
        $sourceText,
        $forbiddenMutationPattern)) `
    -Detail (
        'The preview may render WPF controls and PNGs only; native, ' +
        'process, registry, Shell and device APIs are forbidden.')

Add-Check `
    -Name 'project.shared-rgb-frame-engine' `
    -Passed (
        $sourceText.Contains(
            'Jarvis.Win10.RgbThemeModel.csproj') -and
        $sourceText.Contains(
            'Jarvis.VisualEffects.csproj') -and
        $sourceText.Contains('RgbEffectEngine.Sample(') -and
        $sourceText.Contains('public static class RgbEffectEngine') -and
        $sourceText.Contains('public sealed record RgbFrame')) `
    -Detail (
        'Interactive and deterministic renders must consume the reviewed ' +
        'RGB frame engine instead of duplicating color math.')

$requiredRoleNames = @(
    'DesktopIconList',
    'ExplorerWindow',
    'LayoutRailPanel',
    'CurrentLayoutButton',
    'TaskbarSearchButton',
    'TaskbarTaskViewButton',
    'TaskbarExplorerButton',
    'TaskbarNetworkButton',
    'TaskbarClockButton'
)
Add-Check `
    -Name 'surface.desktop-shell-role-map' `
    -Passed (
        @($requiredRoleNames | Where-Object {
            -not $surfaceText.Contains("x:Name=`"$_`"")
        }).Count -eq 0) `
    -Detail (
        'The owned desktop must expose its desktop, Explorer, layout rail, ' +
        'current-layout slot, realistic task buttons and minimal tray.')

Add-Check `
    -Name 'surface.orthogonal-cross-and-current-layout-placement' `
    -Passed (
        $surfaceText.Contains('x:Name="LayoutAxisUpper"') -and
        $surfaceText.Contains('x:Name="LayoutAxisLower"') -and
        $surfaceText.Contains('x:Name="TaskbarChrome"') -and
        $surfaceText.Contains('x:Name="CurrentLayoutButton"') -and
        $surfaceText.Contains('Width="126"') -and
        $surfaceText.Contains('PERMANENT LAYOUT-AXIS INVARIANT') -and
        $surfaceText.Contains('x:Key="LayoutRailListStyle"') -and
        $surfaceText.Contains('<GroupStyle.ContainerStyle>') -and
        $surfaceText.Contains('Padding="0"') -and
        $surfaceCodeText.Contains('LayoutAxisX = 126.0') -and
        $surfaceCodeText.Contains('TaskbarTop = 800.0') -and
        $surfaceCodeText.Contains('LayoutColumnCenterX = 63.0') -and
        $surfaceCodeText.Contains('SelectedRailLayoutGlyphBounds') -and
        $surfaceCodeText.Contains('LayoutRailGlyphBounds') -and
        $sourceText.Contains(
            'layout-glyph-drawn-bounds-share-permanent-x-axis') -and
        -not $surfaceText.Contains('TaskbarStartButton') -and
        -not $surfaceText.Contains('StartFlyout')) `
    -Detail (
        'The 126px layout column must terminate in the same current-layout ' +
        'glyph while its 2px axis crosses the 2px taskbar rule.')

Add-Check `
    -Name 'surface.layout-rail-and-inner-window-are-operable' `
    -Passed (
        $surfaceText.Contains(
            'Source="{x:Static local:LayoutCatalog.All}"') -and
        $surfaceText.Contains(
            'ItemsSource="{Binding Source={StaticResource LayoutDefinitionsView}}"') -and
        $surfaceText.Contains('SelectedValuePath="Preset"') -and
        $surfaceText.Contains('Preset="{Binding Preset}"') -and
        $surfaceText.Contains(
            'PreviewMouseLeftButtonUp="LayoutRailPanel_OnPreviewMouseLeftButtonUp"') -and
        $surfaceText.Contains(
            'PreviewKeyDown="LayoutRailPanel_OnPreviewKeyDown"') -and
        $surfaceText.Contains('x:Name="LayoutRailViewport"') -and
        $surfaceText.Contains('ClipToBounds="True"') -and
        -not $surfaceText.Contains(
            'MouseMove="LayoutRailRegion_OnMouseMove"') -and
        $surfaceText.Contains('ScrollViewer.CanContentScroll="False"') -and
        $surfaceText.Contains('<ScaleTransform x:Name="LayoutAxisScale" ScaleY="1"') -and
        $surfaceText.Contains(
            'Height="{x:Static local:DesktopShellSurface.LayoutViewportHeight}"') -and
        $surfaceText.Contains('x:Name="ExplorerMinimizeButton"') -and
        $surfaceText.Contains('x:Name="ExplorerMaximizeButton"') -and
        $surfaceText.Contains('x:Name="ExplorerCloseButton"') -and
        $surfaceText.Contains('x:Name="TaskbarExplorerButton"') -and
        $surfaceCodeText.Contains('PrepareLayoutRailPresentation(') -and
        $surfaceCodeText.Contains('UpdateLayoutRailEdgeFeather(') -and
        $surfaceCodeText.Contains('CreateLayoutRailFeatherMask(') -and
        $surfaceCodeText.Contains('RailFeatherDepth = 256.0') -and
        $surfaceCodeText.Contains('RailFeatherInnerOffset') -and
        $surfaceCodeText.Contains('RailFeatherMiddleOffset') -and
        $surfaceCodeText.Contains('RailFeatherOuterOffset') -and
        -not $surfaceCodeText.Contains('ScheduleLayoutRailClose(') -and
        -not $surfaceCodeText.Contains('SetLayoutRailOpen(') -and
        $surfaceCodeText.Contains('SelectLayout(') -and
        $surfaceCodeText.Contains('StartLayoutRailAutoScroll(') -and
        $surfaceCodeText.Contains('AdvanceLayoutRailAutoScroll(') -and
        $surfaceCodeText.Contains('StopLayoutRailAutoScroll(') -and
        $surfaceCodeText.Contains('EvaluateLayoutRailVelocity(') -and
        $surfaceCodeText.Contains('RailCreepDistance') -and
        $surfaceCodeText.Contains('RailCreepVelocity') -and
        $surfaceCodeText.Contains('RailLinearPressureWeight') -and
        $surfaceCodeText.Contains('SmoothStep(') -and
        -not $surfaceCodeText.Contains('RailNeutralHalfHeight') -and
        $surfaceCodeText.Contains('RailResponseHalfExtent') -and
        $surfaceCodeText.Contains('RailMaxVelocity') -and
        $surfaceCodeText -match
            'LayoutRailHitRegion\.AddHandler\(\s*Mouse\.PreviewMouseMoveEvent,\s*new MouseEventHandler\(LayoutRailRegion_OnPreviewMouseMove\),\s*true\);' -and
        $surfaceCodeText.Contains(
            'RaiseHandledLayoutRailMouseMoveFromWhiteGlyphForTest(') -and
        $surfaceCodeText.Contains('RenderingEventArgs') -and
        $surfaceCodeText.Contains('_layoutRailRequestedOffset') -and
        -not $surfaceCodeText.Contains('RailScrollDwell') -and
        $surfaceCodeText.Contains('RailReducedMotionInterval') -and
        $surfaceCodeText.Contains('RailReducedMotionMinimumStep') -and
        $surfaceCodeText.Contains('RailReducedMotionMaximumStep') -and
        $surfaceCodeText.Contains('GetReducedMotionStep(') -and
        $surfaceCodeText.Contains('GetCenteredAdjacentRailItemsForTest(') -and
        $surfaceCodeText.Contains(
            'CompositionTarget.Rendering += LayoutRailRendering_OnRendering') -and
        $surfaceCodeText.Contains(
            'CompositionTarget.Rendering -= LayoutRailRendering_OnRendering') -and
        $surfaceCodeText.Contains('_layoutRailRenderingSubscribed') -and
        $surfaceCodeText.Contains('_layoutRailSmoothMotion') -and
        $surfaceCodeText.Contains('HostWindow_OnDeactivated(') -and
        $surfaceCodeText.Contains('HostWindow_OnStateChanged(') -and
        -not $surfaceCodeText.Contains('_layoutRailGlyphCache') -and
        -not $surfaceCodeText.Contains('SetLayoutGlyphOpacity(') -and
        $surfaceCodeText.Contains('_pendingLayoutReveal') -and
        $surfaceCodeText.Contains('CancelPendingLayoutReveal(') -and
        -not $surfaceCodeText.Contains('RailScrollFrameInterval') -and
        -not $surfaceCodeText.Contains('SnapLayoutRailToNearestItem(') -and
        -not $surfaceCodeText.Contains('StopLayoutRailAutoScroll(true)') -and
        $surfaceCodeText -notmatch
            'ScrollToVerticalOffset\(target\);\s*LayoutRailList\.UpdateLayout\(\);' -and
        $surfaceCodeText.Contains('LayoutItemHeight = 64.0') -and
        $surfaceCodeText.Contains('LayoutViewportHeight = 556.0') -and
        $surfaceCodeText.Contains('ExpandExplorerBounds()') -and
        $surfaceCodeText.Contains('_lastTiledLayout') -and
        $surfaceCodeText.Contains('ExplorerTitleBar_OnMouseMove(') -and
        $surfaceCodeText.Contains('MinimizeExplorer()') -and
        $surfaceCodeText.Contains('RestoreExplorer()') -and
        $surfaceCodeText.Contains('DispatcherTimer')) `
    -Detail (
        'The permanent data-driven rail must signal scroll capability through ' +
        'a continuous boundary-aware feather, then map pointer distance from ' +
        'its center through a nonlinear display-frame-synchronized velocity ' +
        'curve without dwell, snap or post-leave offset changes, ' +
        'select one of sixteen layouts, ' +
        'synchronize maximize/restore state, and keep the inner Explorer ' +
        'controls operational.')

Add-Check `
    -Name 'surface.vector-only-visual-grammar' `
    -Passed (
        $surfaceText.Contains('<Path ') -and
        $surfaceText.Contains('<Rectangle') -and
        $surfaceText.Contains('<local:LayoutGlyph') -and
        $layoutGlyphText.Contains('DrawingContext') -and
        $layoutGlyphText.Contains('DrawTopology(') -and
        $vectorLayerText.Contains('DrawingVisual') -and
        $vectorLayerText.Contains(
            'WpfRetainedVectorSceneRenderer') -and
        $vectorSceneFactoryText.Contains(
            'VectorPlaneCommand') -and
        $vectorSceneFactoryText.Contains(
            'VectorLineCommand') -and
        $vectorSceneFactoryText.Contains(
            'VectorPolylineCommand') -and
        $vectorSceneFactoryText.Contains('AddSplitLine(') -and
        $wpfVectorRendererText.Contains(
            'VectorPointCommand') -and
        $wpfVectorRendererText.Contains(
            'VectorLineCommand') -and
        $wpfVectorRendererText.Contains(
            'VectorPolylineCommand') -and
        $wpfVectorRendererText.Contains(
            'VectorArcCommand') -and
        $wpfVectorRendererText.Contains(
            'VectorPathCommand') -and
        $wpfVectorRendererText.Contains(
            'VectorRectangleCommand') -and
        $wpfVectorRendererText.Contains(
            'VectorEllipseCommand') -and
        $wpfVectorRendererText.Contains(
            'VectorPlaneCommand') -and
        $wpfVectorRendererText.Contains('StreamGeometry') -and
        $wpfVectorRendererText.Contains('CreatePolyline(') -and
        $wpfVectorRendererText.Contains('CreatePolygon(') -and
        $wpfVectorRendererText.Contains('CreateArc(') -and
        $wpfVectorRendererText.Contains('CreatePath(') -and
        $sourceText.Contains(
            'public sealed record RetainedVectorScene') -and
        $sourceText.Contains(
            'public static class RetainedVectorSceneCompiler') -and
        -not $surfaceText.Contains('<Image ') -and
        -not $vectorLayerText.Contains('Bitmap') -and
        -not $apertureFrameText.Contains('Bitmap')) `
    -Detail (
        'Horizon Membrane must use native line and path geometry while the ' +
        'shared renderer retains its broader primitive support and no ' +
        'bitmaps are required.')

Add-Check `
    -Name 'surface.retained-static-vector-layer' `
    -Passed (
        $vectorLayerText.Contains(
            'private readonly DrawingVisual _staticVisual') -and
        $vectorLayerText.Contains(
            'private readonly DrawingVisual _signalVisual') -and
        $vectorLayerText.Contains(
            '_staticRenderer.Render(context, _staticScene)') -and
        $apertureFrameText.Contains(
            'private readonly DrawingVisual _staticVisual') -and
        $apertureFrameText.Contains(
            'private readonly DrawingVisual _focusVisual') -and
        $vectorLayerText.Contains('bool signalChanged') -and
        $vectorLayerText.Contains('OnRenderSizeChanged(') -and
        $vectorLayerText.Contains('RedrawStatic();') -and
        $vectorLayerText.Contains('RedrawSignal();') -and
        $apertureFrameText.Contains('RedrawStatic();') -and
        $apertureFrameText.Contains('RedrawFocus();') -and
        $apertureFrameText.Contains(
            'renderer.Render(context, inputs.Scene)') -and
        $apertureFrameText.Contains(
            'Win10ApertureVectorSceneFactory.TryCreateFocus(') -and
        $wpfVectorRendererText.Contains('DrawingGroup staged') -and
        $wpfVectorRendererText.Contains('staged.Freeze();') -and
        $wpfVectorRendererText.Contains(
            'foreach (Drawing drawing in staged.Children)') -and
        $wpfVectorRendererText.Contains('geometry.Freeze();') -and
        $wpfVectorScenariosText.Contains(
            'missing-semantic-color-fails-closed') -and
        $wpfVectorScenariosText.Contains(
            'invalid-common-scene-fails-closed') -and
        $wpfVectorScenariosText.Contains(
            'palette-is-snapshotted')) `
    -Detail (
        'Static vector geometry must remain retained while RGB work stays ' +
        'isolated to small signal and focus visuals.')

Add-Check `
    -Name 'surface.orthogonal-desktop-grammar' `
    -Passed (
        $surfaceText.Contains('JARVIS2 / FILE EXPLORER') -and
        $surfaceText.Contains('PROJECT_BRIEF_0826.DOCX') -and
        $surfaceText.Contains('x:Name="LayoutRailPanel"') -and
        $surfaceText.Contains('x:Name="CurrentLayoutGlyph"') -and
        $surfaceText.Contains('x:Key="PrimaryRuleBrush" Color="#C2C2BE"') -and
        $surfaceText.Contains('x:Key="SecondaryRuleBrush" Color="#626562"') -and
        $surfaceText.Contains('x:Key="QuietRuleBrush" Color="#303230"') -and
        $layoutCatalogText.Contains('LayoutPreset.Maximized') -and
        $layoutCatalogText.Contains('LayoutPreset.NarrowLeftWideRight') -and
        $layoutCatalogText.Contains('LayoutPreset.NarrowTopWideBottom') -and
        $layoutCatalogText.Contains('LayoutPreset.CenterMainColumns') -and
        $layoutCatalogText.Contains('LayoutPreset.CenterMainRows') -and
        $layoutCatalogText.Contains('LayoutPreset.TopSplitBottomMain') -and
        $layoutCatalogText.Contains('LayoutPreset.FourQuadrants') -and
        $layoutCatalogText.Contains('HasOrthogonalClosure()') -and
        $layoutCatalogText.Contains('IsExactCover(') -and
        -not $surfaceText.Contains('Click="LayoutButton_OnClick"') -and
        $surfaceText.Contains('./Assets/Fonts/#Barlow Condensed') -and
        $sourceText.Contains('BarlowCondensed-Regular.ttf') -and
        $sourceText.Contains('BarlowCondensed-Medium.ttf') -and
        $surfaceText.Contains(
            '{Binding AccentBrush, ElementName=Root}') -and
        -not $surfaceText.Contains('CornerRadius=') -and
        -not $surfaceText.Contains('<DropShadowEffect') -and
        -not $sourceText.Contains('RadialGradientBrush') -and
        -not $sourceText.Contains('DropShadowEffect') -and
        -not $surfaceText.Contains('LinearGradientBrush') -and
        -not $surfaceText.Contains('RadialGradientBrush')) `
    -Detail (
        'The selected direction requires a black desktop, one yellow state ' +
        'accent, sixteen complete layouts, a floating Explorer, three rule ' +
        'tiers, square geometry, no chromatic or fill gradients, and no ' +
        'glow; the opacity feather is the sole gradient exception.')

$forbiddenDesktopContentPattern =
    '(?i)\b(?:keyboard|mouse|linked devices|rgb sync|peripheral)\b'
Add-Check `
    -Name 'surface.no-device-ui-or-illustration' `
    -Passed (-not [regex]::IsMatch(
        $surfaceText,
        $forbiddenDesktopContentPattern)) `
    -Detail (
        'The desktop render must not show physical peripherals or a device ' +
        'control panel.')

Add-Check `
    -Name 'window.single-yellow-desktop-host' `
    -Passed (
        $windowText.Contains('WindowStyle="None"') -and
        $windowText.Contains(
            '<local:DesktopShellSurface') -and
        $sourceText.Contains('HorizonYellowHue') -and
        $windowText.Contains('SnapsToDevicePixels="True"') -and
        $windowText.Contains('UseLayoutRounding="True"') -and
        -not $windowText.Contains('<Viewbox') -and
        -not $windowText.Contains('HueSlider') -and
        -not $windowText.Contains('CYAN') -and
        -not $windowText.Contains('EMERALD') -and
        -not $surfaceText.Contains('x:Name="HueSlider"')) `
    -Detail (
        'The selected preview host must open directly in the committed ' +
        'single-yellow desktop world without scaling or theme controls.')

Add-Check `
    -Name 'render.deterministic-png-boundary' `
    -Passed (
        $sourceText.Contains('RenderTargetBitmap') -and
        $sourceText.Contains('RenderMode.SoftwareOnly') -and
        $sourceText.Contains('96.0') -and
        $sourceText.Contains('PngBitmapEncoder') -and
        $sourceText.Contains(
            '"own-process-offscreen-wpf-surface"') -and
        $sourceText.Contains('DesktopContainsDeviceUi') -and
        $sourceText.Contains('ShellMutationSupported') -and
        $sourceText.Contains('DeviceIntegrationSupported')) `
    -Detail (
        'The project must support deterministic offscreen evidence with ' +
        'explicit non-Shell and non-device receipts.')

$targetPixelBaselineBuild = 19045
$targetPixelBaselineUbr = 6466
$observedUbr = try {
    [int](Get-ItemProperty `
        -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' `
        -Name UBR `
        -ErrorAction Stop).UBR
}
catch {
    -1
}
$targetPixelBaselineEnforced =
    [Environment]::OSVersion.Version.Build -eq $targetPixelBaselineBuild -and
    $observedUbr -eq $targetPixelBaselineUbr

$approvedFeatherHashConfigured =
    -not [string]::IsNullOrWhiteSpace($approvedFeatherTargetSha256)
$approvedFeatherHashWellFormed =
    -not $approvedFeatherHashConfigured -or
    $approvedFeatherTargetSha256 -cmatch '^[0-9A-F]{64}$'
Add-Check `
    -Name 'evidence.approved-feather-hash-contract' `
    -Passed $approvedFeatherHashWellFormed `
    -Detail (
        'The optional approved feather hash must be one exact uppercase ' +
        'SHA-256. An absent hash keeps the locked profile pending but does ' +
        'not fail a non-comparable structural run. ' +
        "configured=$approvedFeatherHashConfigured")

$currentFeatherBaselineHash = $null
if (Test-Path `
        -LiteralPath $currentFeatherBaselinePath `
        -PathType Leaf) {
    $currentFeatherBaselineHash =
        (Get-FileHash `
            -LiteralPath $currentFeatherBaselinePath `
            -Algorithm SHA256).Hash
}
$currentFeatherBaselineState =
    if (-not $approvedFeatherHashConfigured) {
        'approval-not-configured'
    }
    elseif ($currentFeatherBaselineHash -ceq
            $approvedFeatherTargetSha256) {
        'approved-hash-match'
    }
    else {
        'approved-hash-mismatch'
    }
Add-Check `
    -Name 'evidence.current-feather-canonical-integrity' `
    -Passed (
        -not $approvedFeatherHashConfigured -or
        ($approvedFeatherHashWellFormed -and
            $currentFeatherBaselineHash -ceq
                $approvedFeatherTargetSha256)) `
    -Detail (
        'When a feather hash is approved, the checked-in current canonical ' +
        'PNG must match it byte-for-byte on every host. ' +
        "state=$currentFeatherBaselineState/" +
        "sha256=$currentFeatherBaselineHash/" +
        "approved=$approvedFeatherTargetSha256")

$featherRenderAttempted = $false
$featherStructuralRenderPassed = $false
$featherPixelComparable = $false
$featherPixelApproved = $false
$observedFeatherSha256 = $null
$featherEvidenceState =
    if ($targetPixelBaselineEnforced) {
        'locked-profile-not-run'
    }
    else {
        'non-comparable-not-run'
    }

$buildOutput = @(
    & $DotnetPath build `
        $projectPath `
        --configuration Release `
        --nologo `
        --warnaserror 2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 12) -join
        [Environment]::NewLine)

if ($buildExitCode -eq 0) {
    $assemblyPath = Join-Path $sourceRoot (
        'bin\Release\net8.0-windows\' +
        'jarvis-win10-neural-void-preview.dll')
    $adapterOutput = @(
        & $DotnetPath $assemblyPath test-vector-adapter 2>&1
    )
    $adapterExitCode = $LASTEXITCODE
    $adapterReceipt = $null
    try {
        $adapterReceipt =
            ($adapterOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $adapterReceipt = $null
    }
    Add-Check `
        -Name 'vector.wpf-adapter-fail-closed-scenarios' `
        -Passed (
            $adapterExitCode -eq 0 -and
            $null -ne $adapterReceipt -and
            $adapterReceipt.result -eq 'passed' -and
            $adapterReceipt.scenarioCount -eq 13 -and
            $adapterReceipt.passedCount -eq
                $adapterReceipt.scenarioCount -and
            -not $adapterReceipt.shellMutationSupported -and
            -not $adapterReceipt.deviceIntegrationSupported -and
            -not $adapterReceipt.activationPermitted -and
            $adapterReceipt.liveExplorer -eq 'not-run' -and
            -not $adapterReceipt.mutationPerformed) `
        -Detail (
            "WPF vector adapter exit $adapterExitCode; scenarios " +
            "$($adapterReceipt.passedCount)/" +
            "$($adapterReceipt.scenarioCount).")

    $edgeOutput = @(
        & $DotnetPath $assemblyPath test-edge-bars 2>&1
    )
    $edgeExitCode = $LASTEXITCODE
    $edgeReceipt = $null
    try {
        $edgeReceipt =
            ($edgeOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $edgeReceipt = $null
    }
    Add-Check `
        -Name 'interaction.edge-bar-scenarios' `
        -Passed (
            $edgeExitCode -eq 0 -and
            $null -ne $edgeReceipt -and
            $edgeReceipt.result -eq 'passed' -and
            $edgeReceipt.schemaVersion -eq 8 -and
            $edgeReceipt.scenarioCount -eq 25 -and
            $edgeReceipt.passedCount -eq
                $edgeReceipt.scenarioCount -and
            $edgeReceipt.ownProcessOnly -and
            -not $edgeReceipt.shellMutationSupported -and
            $edgeReceipt.liveExplorer -eq 'not-run') `
        -Detail (
            "Edge-bar test exit $edgeExitCode; scenarios " +
            "$($edgeReceipt.passedCount)/" +
            "$($edgeReceipt.scenarioCount).")

    $null = New-Item `
        -ItemType Directory `
        -Path $renderRoot `
        -Force
    $renderCases = @(
        [pscustomobject]@{
            name = 'layout-rail-scroll'
            hue = '56.470588'
            effect = 'static'
            phase = '0'
            expectedHex = '#FFF000'
            approvedFeatherTargetSha256 =
                $approvedFeatherTargetSha256
        }
    )
    $renderPassed = $true
    $renderDetails = [Collections.Generic.List[string]]::new()
    $renderHashes = [Collections.Generic.List[string]]::new()
    foreach ($renderCase in $renderCases) {
        $featherRenderAttempted = $true
        $outputPath =
            Join-Path $renderRoot "$($renderCase.name).png"
        $renderOutput = @(
            & $DotnetPath `
                $assemblyPath `
                render `
                $outputPath `
                $renderCase.hue `
                $renderCase.effect `
                $renderCase.phase 2>&1
        )
        $renderExitCode = $LASTEXITCODE
        $receipt = $null
        try {
            $receipt =
                ($renderOutput -join [Environment]::NewLine) |
                    ConvertFrom-Json
        }
        catch {
            $receipt = $null
        }

        $pngValid = $false
        if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
            $bytes = [IO.File]::ReadAllBytes($outputPath)
            if ($bytes.Length -gt 4096 -and
                $bytes[0] -eq 0x89 -and
                $bytes[1] -eq 0x50 -and
                $bytes[2] -eq 0x4E -and
                $bytes[3] -eq 0x47) {
                $width =
                    ([int]$bytes[16] * 16777216) +
                    ([int]$bytes[17] * 65536) +
                    ([int]$bytes[18] * 256) +
                    [int]$bytes[19]
                $height =
                    ([int]$bytes[20] * 16777216) +
                    ([int]$bytes[21] * 65536) +
                    ([int]$bytes[22] * 256) +
                    [int]$bytes[23]
                $pngValid =
                    $width -eq 1600 -and
                    $height -eq 900
            }
        }

        $structuralCasePassed =
            $renderExitCode -eq 0 -and
            $null -ne $receipt -and
            $receipt.result -eq
                'rendered-own-process-preview' -and
            $receipt.accentHex -eq $renderCase.expectedHex -and
            -not $receipt.desktopContainsDeviceUi -and
            $receipt.ownProcessOnly -and
            -not $receipt.shellMutationSupported -and
            -not $receipt.deviceIntegrationSupported -and
            -not $receipt.activationPermitted -and
            $receipt.liveExplorer -eq 'not-run' -and
            -not $receipt.mutationPerformed -and
            $pngValid
        $actualSha256 = $null
        if ($pngValid) {
            $actualSha256 =
                (Get-FileHash `
                    -LiteralPath $outputPath `
                    -Algorithm SHA256).Hash
            $renderHashes.Add($actualSha256)
        }

        $hasApprovedFeatherHash =
            -not [string]::IsNullOrWhiteSpace(
                $renderCase.approvedFeatherTargetSha256)
        $caseFeatherPixelComparable =
            $structuralCasePassed -and $targetPixelBaselineEnforced
        $caseFeatherPixelApproved =
            $caseFeatherPixelComparable -and
            $approvedFeatherHashWellFormed -and
            $hasApprovedFeatherHash -and
            $actualSha256 -ceq
                $renderCase.approvedFeatherTargetSha256
        $caseEvidenceState =
            if (-not $structuralCasePassed) {
                'structural-failed'
            }
            elseif (-not $targetPixelBaselineEnforced) {
                'non-comparable-structural-pass'
            }
            elseif (-not $hasApprovedFeatherHash) {
                'locked-profile-pending-approval'
            }
            elseif (-not $approvedFeatherHashWellFormed) {
                'locked-profile-invalid-approved-hash'
            }
            elseif ($caseFeatherPixelApproved) {
                'locked-profile-approved'
            }
            else {
                'locked-profile-hash-mismatch'
            }
        $publicationBoundaryPassed =
            -not $targetPixelBaselineEnforced -or
            $caseFeatherPixelApproved
        $casePassed =
            $structuralCasePassed -and $publicationBoundaryPassed

        $featherStructuralRenderPassed = $structuralCasePassed
        $featherPixelComparable = $caseFeatherPixelComparable
        $featherPixelApproved = $caseFeatherPixelApproved
        $observedFeatherSha256 = $actualSha256
        $featherEvidenceState = $caseEvidenceState
        $renderPassed = $renderPassed -and $casePassed
        $renderDetails.Add(
            "$($renderCase.name)=$casePassed/" +
            "$($receipt.accentHex)/" +
            "structural=$structuralCasePassed/" +
            "comparable=$caseFeatherPixelComparable/" +
            "featherPixelApproved=$caseFeatherPixelApproved/" +
            "state=$caseEvidenceState/" +
            "actualHash=$actualSha256/" +
            "approvedHash=$($renderCase.approvedFeatherTargetSha256)")
    }

    Add-Check `
        -Name 'render.feather-current-evidence' `
        -Passed (
            $renderPassed -and
            $renderHashes.Count -eq 1) `
        -Detail (
            'The current 1600x900 feather render must be safe and ' +
            'single-yellow. A non-locked host may pass only as a ' +
            'non-comparable structural result. The locked 96-DPI Windows 10 ' +
            '19045.6466 profile passes only when the actual SHA-256 strictly ' +
            'matches the explicitly approved current canonical. ' +
            ($renderDetails -join '; '))
}

$featherEvidenceStateConsistent =
    switch ($featherEvidenceState) {
        'locked-profile-not-run' {
            -not $featherRenderAttempted -and
            $targetPixelBaselineEnforced -and
            -not $featherStructuralRenderPassed -and
            -not $featherPixelComparable -and
            -not $featherPixelApproved
        }
        'non-comparable-not-run' {
            -not $featherRenderAttempted -and
            -not $targetPixelBaselineEnforced -and
            -not $featherStructuralRenderPassed -and
            -not $featherPixelComparable -and
            -not $featherPixelApproved
        }
        'structural-failed' {
            $featherRenderAttempted -and
            -not $featherStructuralRenderPassed -and
            -not $featherPixelComparable -and
            -not $featherPixelApproved
        }
        'non-comparable-structural-pass' {
            $featherRenderAttempted -and
            $featherStructuralRenderPassed -and
            -not $targetPixelBaselineEnforced -and
            -not $featherPixelComparable -and
            -not $featherPixelApproved
        }
        'locked-profile-pending-approval' {
            $featherRenderAttempted -and
            $featherStructuralRenderPassed -and
            $targetPixelBaselineEnforced -and
            $featherPixelComparable -and
            -not $approvedFeatherHashConfigured -and
            -not $featherPixelApproved
        }
        'locked-profile-invalid-approved-hash' {
            $featherRenderAttempted -and
            $featherStructuralRenderPassed -and
            $targetPixelBaselineEnforced -and
            $featherPixelComparable -and
            $approvedFeatherHashConfigured -and
            -not $approvedFeatherHashWellFormed -and
            -not $featherPixelApproved
        }
        'locked-profile-hash-mismatch' {
            $featherRenderAttempted -and
            $featherStructuralRenderPassed -and
            $targetPixelBaselineEnforced -and
            $featherPixelComparable -and
            $approvedFeatherHashConfigured -and
            $approvedFeatherHashWellFormed -and
            -not $featherPixelApproved -and
            $observedFeatherSha256 -cne
                $approvedFeatherTargetSha256
        }
        'locked-profile-approved' {
            $featherRenderAttempted -and
            $featherStructuralRenderPassed -and
            $targetPixelBaselineEnforced -and
            $featherPixelComparable -and
            $approvedFeatherHashConfigured -and
            $approvedFeatherHashWellFormed -and
            $featherPixelApproved -and
            $observedFeatherSha256 -ceq
                $approvedFeatherTargetSha256
        }
        default {
            $false
        }
    }
Add-Check `
    -Name 'evidence.feather-publication-state-is-consistent' `
    -Passed $featherEvidenceStateConsistent `
    -Detail (
        'Pixel approval is possible only for a structurally valid render on ' +
        'the locked profile with an exact approved SHA-256 match. ' +
        "state=$featherEvidenceState/" +
        "structural=$featherStructuralRenderPassed/" +
        "comparable=$featherPixelComparable/" +
        "approved=$featherPixelApproved")

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 2
    receiptType = 'jarvisv2-win10-neural-void-owned-preview-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    desktopContainsDeviceUi = $false
    ownProcessOnly = $true
    targetPixelBaselineEnforced = $targetPixelBaselineEnforced
    currentFeatherBaselineSha256 = $currentFeatherBaselineHash
    approvedFeatherTargetSha256 = $approvedFeatherTargetSha256
    observedFeatherSha256 = $observedFeatherSha256
    featherRenderAttempted = $featherRenderAttempted
    featherStructuralRenderPassed = $featherStructuralRenderPassed
    featherPixelComparable = $featherPixelComparable
    featherPixelApproved = $featherPixelApproved
    featherEvidenceState = $featherEvidenceState
    pixelBaselineProfile = [ordered]@{
        build = $targetPixelBaselineBuild
        ubr = $targetPixelBaselineUbr
        dpiX = 96
        dpiY = 96
        renderMode = 'software-only'
    }
    readyForStructuralReview = $passed
    readyForVisualReview = $passed -and $featherPixelApproved
    readyForCanonicalPixelPublication =
        $passed -and $featherPixelApproved
    shellMutationSupported = $false
    deviceIntegrationSupported = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 12

if (-not $passed) {
    exit 1
}
