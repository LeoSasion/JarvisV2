[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.RgbThemeModel')
$sharedSourceRoot = Join-Path $root (
    'src\common\Jarvis.VisualEffects')
$projectPath = Join-Path $sourceRoot (
    'Jarvis.Win10.RgbThemeModel.csproj')
$themePath = Join-Path $root (
    'config\windows10-neural-void-rgb-theme.json')
$schemaPath = Join-Path $root (
    'config\windows10-neural-void-rgb-theme.schema.json')
$vfxPath = Join-Path $root (
    'config\neural-void-global-vfx-contract.json')
$vfxSchemaPath = Join-Path $root (
    'config\neural-void-global-vfx-contract.schema.json')
$vfxPresetPath = Join-Path $root (
    'config\neural-void-vfx-preset.json')
$vfxPresetSchemaPath = Join-Path $root (
    'config\neural-void-vfx-preset.schema.json')

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

$theme =
    Get-Content -LiteralPath $themePath -Raw |
        ConvertFrom-Json
$schema =
    Get-Content -LiteralPath $schemaPath -Raw |
        ConvertFrom-Json
$vfx =
    Get-Content -LiteralPath $vfxPath -Raw |
        ConvertFrom-Json
$vfxSchema =
    Get-Content -LiteralPath $vfxSchemaPath -Raw |
        ConvertFrom-Json
$vfxPreset =
    Get-Content -LiteralPath $vfxPresetPath -Raw |
        ConvertFrom-Json
$vfxPresetSchema =
    Get-Content -LiteralPath $vfxPresetSchemaPath -Raw |
        ConvertFrom-Json
$sourceText = @(
    foreach ($currentSourceRoot in @($sourceRoot, $sharedSourceRoot)) {
        Get-ChildItem -LiteralPath $currentSourceRoot -File -Recurse |
            Where-Object Extension -In @('.cs', '.csproj') |
            Sort-Object FullName |
            ForEach-Object {
                [IO.File]::ReadAllText($_.FullName)
            }
    }
) -join [Environment]::NewLine

Add-Check `
    -Name 'theme.approved-neural-void-direction' `
    -Passed (
        $theme.approvedDirection -eq 'D-neural-void' -and
        $theme.approvalBasis -eq 'user-selected-2026-07-30' -and
        $theme.shellComposition.desktopVisualLanguage -eq
            'neural-void' -and
        $theme.shellComposition.neutralSurfaceSystem -eq
            'black-ceramic-and-smoked-glass') `
    -Detail (
        'The desktop composition must use the user-approved D direction.')

$expectedPresets = @(
    'orbital-cyan:A:#00E5FF',
    'reactor-amber:C:#FF6A00',
    'neural-emerald:D:#00FF9A'
)
$observedPresets = @(
    $theme.recommendedAccents |
        ForEach-Object {
            "$($_.id):$($_.sourceConcept):$($_.hex)"
        } |
        Sort-Object
)
Add-Check `
    -Name 'theme.acd-recommended-colors' `
    -Passed (
        $observedPresets.Count -eq 3 -and
        ($observedPresets -join '|') -eq
            (($expectedPresets | Sort-Object) -join '|')) `
    -Detail (
        'A cyan, C amber and D emerald are recommendations, not separate ' +
        'desktop compositions.')

Add-Check `
    -Name 'theme.continuous-rgb-accent-model' `
    -Passed (
        $theme.accentModel.colorSpace -eq 'HSV' -and
        $theme.accentModel.hueMinimum -eq 0 -and
        $theme.accentModel.hueMaximumExclusive -eq 360 -and
        $theme.accentModel.continuousHue -and
        @($theme.accentModel.semanticConsumers).Count -eq 6) `
    -Detail (
        'The approved theme must expose a continuous 0-360 degree accent ' +
        'that feeds all reviewed desktop, Explorer and taskbar roles.')

$expectedVectorPrimitives =
    @('arc', 'line', 'plane', 'point')
$observedVectorPrimitives =
    @($theme.vectorGrammar.primitiveSet | Sort-Object)
Add-Check `
    -Name 'theme.variant-four-vector-grammar' `
    -Passed (
        $theme.vectorGrammar.id -eq 'aperture-contour-v1' -and
        $theme.vectorGrammar.selection -eq
            'user-selected-variant-4' -and
        $theme.vectorGrammar.frameClosure -eq
            'subtractive-open' -and
        ($observedVectorPrimitives -join '|') -eq
            ($expectedVectorPrimitives -join '|') -and
        $theme.vectorGrammar.focusJunctionCount -eq 2 -and
        $theme.vectorGrammar.singleAccentFamily -and
        $theme.vectorGrammar.accentBinding -eq
            'shared-rgb-frame' -and
        $theme.vectorGrammar.glowPolicy -eq
            'reserved-global-not-implemented' -and
        -not $theme.vectorGrammar.bitmapResourcesRequired -and
        $schema.'$defs'.vectorGrammar.properties.id.const -eq
            'aperture-contour-v1' -and
        $schema.'$defs'.vectorGrammar.properties.glowPolicy.const -eq
            'reserved-global-not-implemented') `
    -Detail (
        'Selected variant 4 must remain an open point-line-arc-plane ' +
        'grammar with two shared-frame focus junctions, no local glow and ' +
        'no bitmap dependency.')

$expectedGlobalSystems =
    @('particle-system', 'post-processing')
$observedGlobalSystems =
    @($theme.globalEffectsIntent.plannedSystems | Sort-Object)
$expectedParameterDomains = @(
    'appearance',
    'color-over-life',
    'lifetime',
    'material',
    'motion',
    'post-process',
    'render-order',
    'size-over-life',
    'spawn'
)
$observedParameterDomains =
    @($theme.globalEffectsIntent.parameterDomains | Sort-Object)
Add-Check `
    -Name 'theme.future-global-vfx-boundary' `
    -Passed (
        $theme.globalEffectsIntent.architecture -eq
            'global-vfx-parameter-stack' -and
        $theme.globalEffectsIntent.rendererScope -eq
            'desktop-global-compositor' -and
        $theme.globalEffectsIntent.inspiration -eq
            'film-vfx-and-game-engine-particle-systems' -and
        ($observedGlobalSystems -join '|') -eq
            (($expectedGlobalSystems | Sort-Object) -join '|') -and
        ($observedParameterDomains -join '|') -eq
            ($expectedParameterDomains -join '|') -and
        $theme.globalEffectsIntent.parameterContractId -eq
            'neural-void-global-vfx-v1' -and
        $theme.globalEffectsIntent.parameterContractImplemented -and
        -not $theme.globalEffectsIntent.localGlowImplemented -and
        $theme.globalEffectsIntent.globalGlowReserved -and
        -not $theme.globalEffectsIntent.runtimeImplemented -and
        $schema.'$defs'.globalEffectsIntent.properties.architecture.const -eq
            'global-vfx-parameter-stack') `
    -Detail (
        'Particles and post effects are reserved for one future global, ' +
        'parameterized compositor; current components must stay geometry-only.')

$expectedVfxModules = @(
    'appearance',
    'emission',
    'lifetime',
    'motion',
    'trail'
)
$observedVfxModules =
    @($vfx.particleModules.id | Sort-Object)
$expectedPostEffects = @(
    'bloom',
    'chromatic-aberration',
    'color-grade',
    'displacement',
    'feedback-trails'
)
$observedPostEffects =
    @($vfx.postEffects.id | Sort-Object)
$allVfxModules =
    @($vfx.particleModules) + @($vfx.postEffects)
Add-Check `
    -Name 'vfx.cross-version-parameter-contract' `
    -Passed (
        $vfx.schemaVersion -eq 1 -and
        $vfx.contractId -eq 'neural-void-global-vfx-v1' -and
        (@($vfx.platformScope) -join '|') -eq
            'windows10|windows11' -and
        $vfx.architecture -eq
            'module-graph-plus-ordered-post-stack' -and
        $vfx.rendererScope -eq 'desktop-global-compositor' -and
        $vfx.colorBinding -eq 'shared-rgb-frame' -and
        $vfx.clock.fixedStepHz -eq 60 -and
        $vfx.clock.deterministicSeedRequired -and
        ($observedVfxModules -join '|') -eq
            (($expectedVfxModules | Sort-Object) -join '|') -and
        ($observedPostEffects -join '|') -eq
            (($expectedPostEffects | Sort-Object) -join '|')) `
    -Detail (
        'One platform-neutral contract must describe the particle module ' +
        'graph, ordered post stack, fixed clock and shared RGB binding.')

$vfxParameterCount =
    @(
        $allVfxModules |
            ForEach-Object { @($_.parameters).Count } |
            Measure-Object -Sum
    )[0].Sum
Add-Check `
    -Name 'vfx.disabled-parameter-catalog' `
    -Passed (
        -not $vfx.runtimeEnabled -and
        -not $vfx.editorImplemented -and
        @($allVfxModules | Where-Object enabledByDefault).Count -eq 0 -and
        $vfxParameterCount -eq 30 -and
        @($vfx.qualityProfiles).Count -eq 3 -and
        $vfx.capabilities.gpuBackend -eq 'unselected' -and
        $vfx.capabilities.softwareReference -eq
            'deterministic-cpu-required' -and
        -not $vfx.capabilities.componentLocalEffects -and
        -not $vfx.capabilities.liveShellIntegration -and
        -not $vfx.capabilities.physicalDeviceIo -and
        $vfxSchema.properties.runtimeEnabled.const -eq $false -and
        $vfxSchema.properties.editorImplemented.const -eq $false -and
        $vfxSchema.'$defs'.module.properties.enabledByDefault.const -eq
            $false -and
        $vfxSchema.'$defs'.capabilities.properties.liveShellIntegration.const `
            -eq $false) `
    -Detail (
        'The Galaxy View-like parameter vocabulary may be compiled now, ' +
        'while its renderer, editor, local effects, Shell and device paths ' +
        'remain disabled.')

$allPresetModules =
    @($vfxPreset.particleModules) + @($vfxPreset.postEffects)
Add-Check `
    -Name 'vfx.versioned-inert-preset' `
    -Passed (
        $vfxPreset.schemaVersion -eq 1 -and
        $vfxPreset.presetId -eq
            'neural-void-inert-foundation-v1' -and
        $vfxPreset.contractId -eq
            'neural-void-global-vfx-v1' -and
        $vfxPreset.lifecycleState -eq
            'inert-parameter-preset' -and
        $vfxPreset.visualSignalBinding -eq
            'jarvis-visual-signal-v1' -and
        -not $vfxPreset.runtimeEnabled -and
        -not $vfxPreset.physicalDeviceIo -and
        @($allPresetModules).Count -eq 10 -and
        @($allPresetModules | Where-Object enabled).Count -eq 0 -and
        $vfxPresetSchema.properties.schemaVersion.const -eq 1 -and
        $vfxPresetSchema.properties.runtimeEnabled.const -eq $false -and
        $vfxPresetSchema.properties.physicalDeviceIo.const -eq $false -and
        $vfxPresetSchema.'$defs'.module.properties.enabled.const -eq
            $false) `
    -Detail (
        'The versioned starter preset may preserve reviewed parameter ' +
        'values, but every particle, post effect and physical-device path ' +
        'must remain inert.')

Add-Check `
    -Name 'source.shared-cross-version-visual-library' `
    -Passed (
        $sourceText.Contains(
            'src\common\Jarvis.VisualEffects') -or
        ($sourceText.Contains('Jarvis.VisualEffects.csproj') -and
         $sourceText.Contains('namespace Jarvis.VisualEffects;') -and
         $sourceText.Contains('public sealed record VisualSignalFrame') -and
         $sourceText.Contains('public static class VfxPresetCompiler'))) `
    -Detail (
        'RGB sampling, visual signal frames and VFX contract/preset ' +
        'validation must live in the reviewed Win10/Win11 common library.')

Add-Check `
    -Name 'theme.no-peripherals-inside-desktop' `
    -Passed (
        -not $theme.shellComposition.deviceControlsVisible -and
        -not $theme.shellComposition.peripheralIllustrationsVisible -and
        -not $theme.shellComposition.rgbSyncPanelVisible -and
        -not $theme.syncIntent.deviceControlsVisibleInDesktop -and
        -not $theme.syncIntent.physicalDeviceIllustrationsVisible) `
    -Detail (
        'Keyboard, mouse and device controls belong outside the desktop ' +
        'composition.')

Add-Check `
    -Name 'sync.external-future-device-bridge' `
    -Passed (
        $theme.syncIntent.displayConsumer -eq
            'windows-shell-visuals' -and
        $theme.syncIntent.futureDeviceConsumer -eq
            'external-device-lighting-bridge' -and
        -not $theme.syncIntent.deviceIoImplemented -and
        -not $theme.syncIntent.providerSdkBound -and
        -not $theme.syncIntent.transportSupported -and
        -not $theme.syncIntent.shellDependsOnDeviceBridge -and
        $theme.syncIntent.failurePolicy -eq
            'display-continues-with-last-valid-local-frame') `
    -Detail (
        'The display and future physical-device bridge may consume the same ' +
        'RGB frame, but Explorer must never depend on the device bridge.')

$forbiddenSourcePattern = (
    '(?i)\b(?:DllImport|LibraryImport|Process\.|Registry|HidD_|' +
    'SetupDi|CreateFile|WriteFile|DeviceIoControl|SendMessage|' +
    'SetWindowLong|DwmSetWindowAttribute|Windhawk|' +
    'Start-Process|Stop-Process)\b'
)
Add-Check `
    -Name 'source.pure-offline-theme-model' `
    -Passed (-not [regex]::IsMatch(
        $sourceText,
        $forbiddenSourcePattern)) `
    -Detail (
        'The theme compiler and RGB sampler may perform deterministic color ' +
        'math only; native, process, registry, device and Shell APIs are ' +
        'forbidden.')

Add-Check `
    -Name 'schema.offline-capability-boundary' `
    -Passed (
        $schema.properties.styleValuesDefined.const -eq $true -and
        $schema.properties.executionSupported.const -eq $false -and
        $schema.properties.mutationSupported.const -eq $false -and
        $schema.properties.activationPermitted.const -eq $false -and
        $schema.properties.liveExplorer.const -eq 'not-run') `
    -Detail (
        'Approved style intent is allowed, while execution, mutation, ' +
        'activation and live Explorer remain impossible.')

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
        'jarvis-win10-rgb-theme-model.dll')
    $modelOutput = @(
        & $DotnetPath $assemblyPath test 2>&1
    )
    $modelExitCode = $LASTEXITCODE
    $receipt = $null
    try {
        $receipt =
            ($modelOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $receipt = $null
    }

    Add-Check `
        -Name 'model.fail-closed-and-color-scenarios' `
        -Passed (
            $modelExitCode -eq 0 -and
            $null -ne $receipt -and
            $receipt.result -eq 'passed' -and
            $receipt.scenarioCount -ge 23 -and
            $receipt.passedCount -eq $receipt.scenarioCount -and
            -not $receipt.desktopContainsDeviceUi -and
            $receipt.readyForOwnedProcessPreview -and
            -not $receipt.readyForShellMutation -and
            -not $receipt.readyForDeviceIntegration -and
            -not $receipt.executionSupported -and
            -not $receipt.mutationSupported -and
            -not $receipt.activationPermitted -and
            $receipt.liveExplorer -eq 'not-run' -and
            -not $receipt.mutationPerformed) `
        -Detail (
            "Model exit $modelExitCode; scenarios " +
            "$($receipt.passedCount)/$($receipt.scenarioCount).")

    $vfxCompileOutput = @(
        & $DotnetPath $assemblyPath compile-vfx 2>&1
    )
    $vfxCompileExitCode = $LASTEXITCODE
    $vfxCompileReceipt = $null
    try {
        $vfxCompileReceipt =
            ($vfxCompileOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $vfxCompileReceipt = $null
    }
    Add-Check `
        -Name 'vfx.parameter-contract-compilation' `
        -Passed (
            $vfxCompileExitCode -eq 0 -and
            $null -ne $vfxCompileReceipt -and
            $vfxCompileReceipt.result -eq
                'compiled-parameter-contract' -and
            $vfxCompileReceipt.renderStageCount -eq 4 -and
            $vfxCompileReceipt.qualityProfileCount -eq 3 -and
            $vfxCompileReceipt.particleModuleCount -eq 5 -and
            $vfxCompileReceipt.postEffectCount -eq 5 -and
            $vfxCompileReceipt.parameterCount -eq 30 -and
            $vfxCompileReceipt.allModulesDisabledByDefault -and
            $vfxCompileReceipt.sharedRgbBindingValidated -and
            -not $vfxCompileReceipt.runtimeEnabled -and
            -not $vfxCompileReceipt.editorImplemented -and
            -not $vfxCompileReceipt.readyForShellMutation -and
            -not $vfxCompileReceipt.activationPermitted -and
            $vfxCompileReceipt.liveExplorer -eq 'not-run' -and
            -not $vfxCompileReceipt.mutationPerformed) `
        -Detail (
            "VFX compile exit $vfxCompileExitCode; parameters " +
            "$($vfxCompileReceipt.parameterCount).")

    $vfxPresetOutput = @(
        & $DotnetPath $assemblyPath compile-vfx-preset 2>&1
    )
    $vfxPresetExitCode = $LASTEXITCODE
    $vfxPresetReceipt = $null
    try {
        $vfxPresetReceipt =
            ($vfxPresetOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $vfxPresetReceipt = $null
    }
    Add-Check `
        -Name 'vfx.inert-preset-compilation' `
        -Passed (
            $vfxPresetExitCode -eq 0 -and
            $null -ne $vfxPresetReceipt -and
            $vfxPresetReceipt.result -eq
                'compiled-inert-preset' -and
            $vfxPresetReceipt.overrideCount -eq 15 -and
            $vfxPresetReceipt.allModulesDisabled -and
            $vfxPresetReceipt.sharedVisualSignalValidated -and
            -not $vfxPresetReceipt.runtimeEnabled -and
            -not $vfxPresetReceipt.physicalDeviceIo -and
            -not $vfxPresetReceipt.readyForShellMutation -and
            -not $vfxPresetReceipt.activationPermitted -and
            $vfxPresetReceipt.liveExplorer -eq 'not-run' -and
            -not $vfxPresetReceipt.mutationPerformed) `
        -Detail (
            "VFX preset exit $vfxPresetExitCode; overrides " +
            "$($vfxPresetReceipt.overrideCount).")

    $vfxTestOutput = @(
        & $DotnetPath $assemblyPath test-vfx 2>&1
    )
    $vfxTestExitCode = $LASTEXITCODE
    $vfxTestReceipt = $null
    try {
        $vfxTestReceipt =
            ($vfxTestOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $vfxTestReceipt = $null
    }
    Add-Check `
        -Name 'vfx.fail-closed-parameter-scenarios' `
        -Passed (
            $vfxTestExitCode -eq 0 -and
            $null -ne $vfxTestReceipt -and
            $vfxTestReceipt.result -eq 'passed' -and
            $vfxTestReceipt.scenarioCount -ge 26 -and
            $vfxTestReceipt.passedCount -eq
                $vfxTestReceipt.scenarioCount -and
            -not $vfxTestReceipt.runtimeEnabled -and
            -not $vfxTestReceipt.editorImplemented -and
            -not $vfxTestReceipt.readyForShellMutation -and
            -not $vfxTestReceipt.activationPermitted -and
            $vfxTestReceipt.liveExplorer -eq 'not-run' -and
            -not $vfxTestReceipt.mutationPerformed) `
        -Detail (
            "VFX test exit $vfxTestExitCode; scenarios " +
            "$($vfxTestReceipt.passedCount)/" +
            "$($vfxTestReceipt.scenarioCount).")
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-win10-rgb-theme-model-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    desktopContainsDeviceUi = $false
    readyForOwnedProcessPreview = $passed
    globalVfxParameterContractCompiled = $passed
    sharedVisualSignalContractCompiled = $passed
    inertVfxPresetCompiled = $passed
    globalVfxRuntimeEnabled = $false
    globalVfxEditorImplemented = $false
    readyForShellMutation = $false
    readyForDeviceIntegration = $false
    executionSupported = $false
    mutationSupported = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 12

if (-not $passed) {
    exit 1
}
