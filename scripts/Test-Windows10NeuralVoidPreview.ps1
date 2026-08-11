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
        $surfaceText.Contains('x:Name="LayoutScrollUpHotZone"') -and
        $surfaceText.Contains('x:Name="LayoutScrollDownHotZone"') -and
        $surfaceText.Contains('ScrollViewer.CanContentScroll="False"') -and
        $surfaceText.Contains(
            'Height="{x:Static local:DesktopShellSurface.LayoutViewportHeight}"') -and
        $surfaceText.Contains('x:Name="ExplorerMinimizeButton"') -and
        $surfaceText.Contains('x:Name="ExplorerMaximizeButton"') -and
        $surfaceText.Contains('x:Name="ExplorerCloseButton"') -and
        $surfaceText.Contains('x:Name="TaskbarExplorerButton"') -and
        $surfaceCodeText.Contains('LayoutRailRegion_OnMouseEnter(') -and
        $surfaceCodeText.Contains('ScheduleLayoutRailClose()') -and
        $surfaceCodeText.Contains('SelectLayout(') -and
        $surfaceCodeText.Contains('StartLayoutRailAutoScroll(') -and
        $surfaceCodeText.Contains('AdvanceLayoutRailAutoScroll(') -and
        $surfaceCodeText.Contains('StopLayoutRailAutoScroll(') -and
        $surfaceCodeText.Contains('RailScrollDwell') -and
        $surfaceCodeText.Contains('LayoutItemHeight = 54.0') -and
        $surfaceCodeText.Contains('LayoutViewportHeight = 556.0') -and
        $surfaceCodeText.Contains('ExpandExplorerBounds()') -and
        $surfaceCodeText.Contains('_lastTiledLayout') -and
        $surfaceCodeText.Contains('ExplorerTitleBar_OnMouseMove(') -and
        $surfaceCodeText.Contains('RestoreExplorer()') -and
        $surfaceCodeText.Contains('DispatcherTimer')) `
    -Detail (
        'The compact data-driven rail must hover-open, auto-scroll from its ' +
        'upper and lower edge zones, select one of sixteen layouts, ' +
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
        'tiers, square geometry and no gradients or glow.')

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
            $edgeReceipt.scenarioCount -eq 19 -and
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
            expectedTargetSha256 =
                '2158EEA1184EFD22CBE3B630D662F3562A02DFC27955922F4321E2D1957AD9E0'
        }
    )
    $renderPassed = $true
    $renderDetails = [Collections.Generic.List[string]]::new()
    $renderHashes = [Collections.Generic.List[string]]::new()
    foreach ($renderCase in $renderCases) {
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

        $casePassed =
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
        if ($pngValid) {
            $actualSha256 =
                (Get-FileHash `
                    -LiteralPath $outputPath `
                    -Algorithm SHA256).Hash
            $renderHashes.Add($actualSha256)
            $pixelIdentical =
                -not $targetPixelBaselineEnforced -or
                $actualSha256 -ceq
                    $renderCase.expectedTargetSha256
            $casePassed =
                $casePassed -and $pixelIdentical
        }
        else {
            $pixelIdentical = $false
        }
        $renderPassed = $renderPassed -and $casePassed
        $renderDetails.Add(
            "$($renderCase.name)=$casePassed/" +
            "$($receipt.accentHex)/pixel=$pixelIdentical")
    }

    Add-Check `
        -Name 'render.layout-rail-yellow-evidence' `
        -Passed (
            $renderPassed -and
            $renderHashes.Count -eq 1) `
        -Detail (
            'The approved 1600x900 render must be safe, single-yellow and ' +
            'byte-identical on the 96-DPI software-rendered Windows 10 ' +
            '19045.6466 baseline. ' +
            ($renderDetails -join '; '))
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-win10-neural-void-owned-preview-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    desktopContainsDeviceUi = $false
    ownProcessOnly = $true
    targetPixelBaselineEnforced = $targetPixelBaselineEnforced
    pixelBaselineProfile = [ordered]@{
        build = $targetPixelBaselineBuild
        ubr = $targetPixelBaselineUbr
        dpiX = 96
        dpiY = 96
        renderMode = 'software-only'
    }
    readyForVisualReview = $passed
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
