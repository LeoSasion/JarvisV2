[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.RgbThemeModel')
$projectPath = Join-Path $sourceRoot (
    'Jarvis.Win10.RgbThemeModel.csproj')
$themePath = Join-Path $root (
    'config\windows10-neural-void-rgb-theme.json')
$schemaPath = Join-Path $root (
    'config\windows10-neural-void-rgb-theme.schema.json')

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
        ConvertFrom-Json -Depth 50
$schema =
    Get-Content -LiteralPath $schemaPath -Raw |
        ConvertFrom-Json -Depth 50
$sourceText = @(
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.csproj') |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
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
                ConvertFrom-Json -Depth 50
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
