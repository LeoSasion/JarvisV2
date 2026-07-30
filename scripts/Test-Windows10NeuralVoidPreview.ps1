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
    'NeuralVoidPreviewSurface.xaml')
$vectorLayerPath = Join-Path $sourceRoot (
    'NeuralVectorLayer.cs')
$vectorSceneFactoryPath = Join-Path $sourceRoot (
    'Win10NeuralVectorSceneFactory.cs')
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
$vectorLayerText = [IO.File]::ReadAllText($vectorLayerPath)
$vectorSceneFactoryText =
    [IO.File]::ReadAllText($vectorSceneFactoryPath)
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
    'ExplorerCommandBar',
    'ExplorerContentHost',
    'ExplorerFolderView',
    'TaskbarStartButton',
    'TaskbarTaskList',
    'TaskbarNotificationArea',
    'TaskbarClock'
)
Add-Check `
    -Name 'surface.exact-eight-role-map' `
    -Passed (
        @($requiredRoleNames | Where-Object {
            -not $surfaceText.Contains("x:Name=`"$_`"")
        }).Count -eq 0) `
    -Detail (
        'The simulated desktop must visibly cover every reviewed selector ' +
        'role before any real adapter is considered.')

Add-Check `
    -Name 'surface.vector-only-visual-grammar' `
    -Passed (
        $surfaceText.Contains(
            '<local:NeuralVectorLayer') -and
        $surfaceText.Contains(
            '<local:ApertureFrame') -and
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
            'VectorPlaneCommand') -and
        $wpfVectorRendererText.Contains('StreamGeometry') -and
        $wpfVectorRendererText.Contains('CreatePolyline(') -and
        $wpfVectorRendererText.Contains('CreatePolygon(') -and
        $wpfVectorRendererText.Contains('CreateArc(') -and
        $sourceText.Contains(
            'public sealed record RetainedVectorScene') -and
        $sourceText.Contains(
            'public static class RetainedVectorSceneCompiler') -and
        $apertureFrameText.Contains('DrawingVisual') -and
        $apertureFrameText.Contains('StreamGeometry') -and
        $apertureFrameText.Contains('DrawTangentCorner(') -and
        $apertureFrameText.Contains('context.ArcTo(') -and
        $apertureFrameText.Contains('DrawRegistrationSquare(') -and
        -not $surfaceText.Contains('<Image ') -and
        -not $vectorLayerText.Contains('Bitmap') -and
        -not $apertureFrameText.Contains('Bitmap')) `
    -Detail (
        'Selected variant 4 must be drawn from mathematical points, lines, ' +
        'arcs and planes without decorative bitmap resources.')

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
        $wpfVectorRendererText.Contains('DrawingGroup staged') -and
        $wpfVectorRendererText.Contains('staged.Freeze();') -and
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

$apertureFrameCount =
    [regex]::Matches(
        $surfaceText,
        '<local:ApertureFrame\b').Count
Add-Check `
    -Name 'surface.variant-four-aperture-grammar' `
    -Passed (
        $apertureFrameCount -ge 4 -and
        $surfaceText.Contains('FocusCorner="TopLeft"') -and
        $surfaceText.Contains(
            'LineBrush="{StaticResource ApertureLineBrush}"') -and
        $surfaceText.Contains(
            'AccentBrush="{Binding AccentBrush, ElementName=Root}"') -and
        -not $surfaceText.Contains('<DropShadowEffect') -and
        $apertureFrameText.Contains('DrawSplitEdge(') -and
        $apertureFrameText.Contains('DrawEllipse(') -and
        -not $apertureFrameText.Contains('CreateGlowBrush(') -and
        -not $sourceText.Contains('RadialGradientBrush') -and
        -not $sourceText.Contains('DropShadowEffect') -and
        $themeText.Contains('"id": "aperture-contour-v1"') -and
        $themeText.Contains(
            '"selection": "user-selected-variant-4"') -and
        $themeText.Contains('"frameClosure": "subtractive-open"') -and
        $themeText.Contains('"focusJunctionCount": 2') -and
        $themeText.Contains('"accentBinding": "shared-rgb-frame"') -and
        $themeText.Contains(
            '"glowPolicy": "reserved-global-not-implemented"') -and
        $themeText.Contains(
            '"architecture": "global-vfx-parameter-stack"') -and
        $themeText.Contains('"localGlowImplemented": false') -and
        $themeText.Contains('"globalGlowReserved": true') -and
        $themeText.Contains('"runtimeImplemented": false') -and
        $themeText.Contains('"bitmapResourcesRequired": false')) `
    -Detail (
        'The selected fourth variant requires subtractive open contours, ' +
        'tangent arcs, shared-frame color binding, two local point/ring ' +
        'junctions, no component glow and a reserved global VFX boundary.')

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
    -Name 'window.preview-controls-outside-desktop-surface' `
    -Passed (
        $windowText.Contains('x:Name="HueSlider"') -and
        $windowText.Contains('A / CYAN') -and
        $windowText.Contains('C / AMBER') -and
        $windowText.Contains('D / EMERALD') -and
        $windowText.Contains(
            '<local:NeuralVoidPreviewSurface') -and
        -not $surfaceText.Contains('x:Name="HueSlider"')) `
    -Detail (
        'A/C/D and continuous hue controls belong to the preview host, ' +
        'outside the desktop composition.')

Add-Check `
    -Name 'render.deterministic-png-boundary' `
    -Passed (
        $sourceText.Contains('RenderTargetBitmap') -and
        $sourceText.Contains('PngBitmapEncoder') -and
        $sourceText.Contains(
            '"own-process-offscreen-wpf-surface"') -and
        $sourceText.Contains('DesktopContainsDeviceUi') -and
        $sourceText.Contains('ShellMutationSupported') -and
        $sourceText.Contains('DeviceIntegrationSupported')) `
    -Detail (
        'The project must support deterministic offscreen evidence with ' +
        'explicit non-Shell and non-device receipts.')

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
            $adapterReceipt.scenarioCount -eq 7 -and
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

    $null = New-Item `
        -ItemType Directory `
        -Path $renderRoot `
        -Force
    $renderCases = @(
        [pscustomobject]@{
            name = 'a-cyan'
            hue = '186.117647'
            effect = 'static'
            phase = '0'
            expectedHex = '#00E5FF'
            expectedTargetSha256 =
                '23CEA04C7471F45D01C58CE738429C1409B057BE0BBDDC88DE0BD2107199B0F6'
        },
        [pscustomobject]@{
            name = 'c-amber'
            hue = '24.941176'
            effect = 'static'
            phase = '0'
            expectedHex = '#FF6A00'
            expectedTargetSha256 =
                '2A44328EB8C4B009B523D44E109B0D1202CF0C11EF971E7EEC78252F4D0E3780'
        },
        [pscustomobject]@{
            name = 'd-emerald'
            hue = '156.235294'
            effect = 'signal-pulse'
            phase = '0.25'
            expectedHex = '#00FF9A'
            expectedTargetSha256 =
                'B9545A55C8E280F2FDFB287DC199872857E2770A1E7C6D87E9696FA19A7DB28F'
        },
        [pscustomobject]@{
            name = 'custom-magenta'
            hue = '300'
            effect = 'static'
            phase = '0'
            expectedHex = '#FF00FF'
            expectedTargetSha256 =
                '6E791CC91B844851CAC149332A35DD15711B6811D3A0305DCF811B94F3526066'
        }
    )
    $renderPassed = $true
    $renderDetails = [Collections.Generic.List[string]]::new()
    $renderHashes = [Collections.Generic.List[string]]::new()
    $targetPixelBaselineEnforced =
        [Environment]::OSVersion.Version.Build -eq 19045
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
        -Name 'render.acd-and-custom-hue-evidence' `
        -Passed (
            $renderPassed -and
            $renderHashes.Count -eq 4 -and
            @($renderHashes | Sort-Object -Unique).Count -eq 4) `
        -Detail (
            'All 1600x900 renders must be safe, color-correct and visually ' +
            'distinct. Exact Windows 10 build 19045 additionally requires ' +
            'byte-identical approved pixels. ' +
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
    targetPixelBaselineEnforced =
        [Environment]::OSVersion.Version.Build -eq 19045
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
