[CmdletBinding()]
param(
    [switch]$StaticOnly,
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.NativeStyleProbe')
$admissionRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.HostAdmission')
$rgbModelRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.RgbThemeModel')
$projectPath = Join-Path $sourceRoot (
    'Jarvis.Win10.NativeStyleProbe.csproj')
$profilePath = Join-Path $root 'config\windows10-host-profiles.json'
$schemaPath = Join-Path $root 'config\windows10-host-profiles.schema.json'
$windowPath = Join-Path $sourceRoot 'MainWindow.xaml'
$stylerPath = Join-Path $sourceRoot 'OwnedWindowStyler.cs'
$dwmPath = Join-Path $sourceRoot 'Win10DwmApi.cs'

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
        $admissionRoot,
        $rgbModelRoot
    )) {
        Get-ChildItem -LiteralPath $currentSourceRoot -File -Recurse |
            Where-Object Extension -In @('.cs', '.xaml', '.csproj') |
            Sort-Object FullName |
            ForEach-Object {
                [IO.File]::ReadAllText($_.FullName)
            }
    }
) -join [Environment]::NewLine
$windowText = [IO.File]::ReadAllText($windowPath)
$stylerText = [IO.File]::ReadAllText($stylerPath)
$dwmText = [IO.File]::ReadAllText($dwmPath)
$profiles =
    Get-Content -LiteralPath $profilePath -Raw |
        ConvertFrom-Json -Depth 30
$schema =
    Get-Content -LiteralPath $schemaPath -Raw |
        ConvertFrom-Json -Depth 30

$forbiddenExternalMutationPattern = (
    '(?i)\b(?:EnumWindows|FindWindow|GetShellWindow|SendMessage|' +
    'PostMessage|SetWindowLong|SetWindowPos|MoveWindow|ShowWindow|' +
    'DestroyWindow|OpenProcess|CreateRemoteThread|VirtualAllocEx|' +
    'WriteProcessMemory|SetWindowsHookEx|TerminateProcess|' +
    'ServiceController|RegistryKey\.SetValue|Registry\.SetValue|' +
    'RegistryKey\.Delete|Start-Process|Stop-Process)\b'
)
Add-Check `
    -Name 'source.no-external-window-process-or-system-mutation-api' `
    -Passed (-not [regex]::IsMatch(
        $sourceText,
        $forbiddenExternalMutationPattern)) `
    -Detail (
        'The probe may read host identity and style only a Window instance ' +
        'created by this process.')

$imports = @(
    [regex]::Matches(
        $sourceText,
        '(?s)\[DllImport\((?<body>.*?)\)\]\s*' +
        'private static extern \w+\s+(?<name>\w+)') |
        ForEach-Object {
            $_.Groups['name'].Value
        }
)
$allowedImports = @(
    'DwmGetColorizationColor',
    'DwmIsCompositionEnabled',
    'DwmSetWindowAttribute'
)
Add-Check `
    -Name 'source.reviewed-win10-dwm-allowlist' `
    -Passed (
        $imports.Count -eq 3 -and
        (@($imports | Sort-Object) -join '|') -eq
            (($allowedImports | Sort-Object) -join '|')) `
    -Detail (
        'Exactly three reviewed dwmapi entry points are allowed. Observed: ' +
        "$($imports -join ', ').")

Add-Check `
    -Name 'source.owned-hwnd-only' `
    -Passed (
        $stylerText.Contains(
            'new WindowInteropHelper(ownedWindow).Handle') -and
        $stylerText.Contains(
            'The Win10 style probe could not obtain its own HWND.') -and
        -not $sourceText.Contains('user32.dll')) `
    -Detail (
        'DWM writes must receive only the HWND of the supplied owned Window; ' +
        'external HWND discovery is forbidden.')

Add-Check `
    -Name 'source.win10-capability-boundary' `
    -Passed (
        $dwmText.Contains('UseImmersiveDarkMode = 20') -and
        -not $sourceText.Contains('WindowCornerPreference') -and
        -not $sourceText.Contains('SystemBackdropType') -and
        -not $sourceText.Contains('Taskbar.View.dll') -and
        -not $sourceText.Contains('FileExplorerExtensions.')) `
    -Detail (
        'The first Win10 slice uses only the reviewed dark-caption attribute ' +
        'and must not copy Win11 corners, backdrops, symbols or selectors.')

Add-Check `
    -Name 'xaml.normal-system-framed-window' `
    -Passed (
        $windowText.Contains('WindowStyle="SingleBorderWindow"') -and
        $windowText.Contains('ResizeMode="CanResizeWithGrip"') -and
        $windowText.Contains('OWN PROCESS ONLY') -and
        $windowText.Contains('EXPLORER UNTOUCHED') -and
        -not $windowText.Contains('AllowsTransparency="True"') -and
        -not $windowText.Contains('Topmost="True"')) `
    -Detail (
        'The visible probe must remain an ordinary system-framed Win10 ' +
        'window with an explicit own-process boundary.')

Add-Check `
    -Name 'source.shared-neural-void-client-frame' `
    -Passed (
        $sourceText.Contains(
            'Jarvis.Win10.RgbThemeModel.csproj') -and
        $sourceText.Contains('RgbEffectEngine.Sample(') -and
        $windowText.Contains('x:Name="RgbHueSlider"') -and
        $windowText.Contains('Content="A"') -and
        $windowText.Contains('Content="C"') -and
        $windowText.Contains('Content="D"') -and
        $windowText.Contains('CLIENT ONLY') -and
        $dwmText.Contains('UseImmersiveDarkMode = 20') -and
        -not $dwmText.Contains('BorderColor') -and
        -not $dwmText.Contains('CaptionColor') -and
        -not $dwmText.Contains('TextColor')) `
    -Detail (
        'The shared RGB frame may color only this owned WPF client surface; ' +
        'the real Win10 caption remains on its one reviewed dark-mode attribute.')

$profile = @($profiles.profiles)
$explorer = if ($profile.Count -eq 1) {
    $profile[0].explorer
}
else {
    $null
}
Add-Check `
    -Name 'profile.exact-observed-host' `
    -Passed (
        $profiles.schemaVersion -eq 1 -and
        $profiles.platform -eq 'windows10' -and
        $profile.Count -eq 1 -and
        $profile[0].profileId -eq 'win10-22h2-19045.6466-x64' -and
        $profile[0].build -eq 19045 -and
        $profile[0].ubr -eq 6466 -and
        $profile[0].architecture -eq 'X64' -and
        $profile[0].installationType -eq 'Client' -and
        $explorer.size -eq 6089584 -and
        $explorer.sha256 -eq
            '988A56D897915315EEF9CA679B3BC8ADFCECF5E227AEA99AAA1817620520E97E') `
    -Detail (
        'Admission is pinned to the reviewed build, UBR, architecture and ' +
        'Explorer size/SHA-256 from this Win10 host.')

Add-Check `
    -Name 'profile.fail-closed-safety' `
    -Passed (
        -not $profile[0].activationPermitted -and
        $profile[0].liveExplorer -eq 'not-run' -and
        @($profile[0].allowedCapabilities).Count -eq 3 -and
        @($profile[0].allowedCapabilities) -contains
            'read-system-dwm-state' -and
        @($profile[0].allowedCapabilities) -contains
            'read-shell-window-topology' -and
        @($profile[0].allowedCapabilities) -contains
            'set-owned-window-dark-caption') `
    -Detail (
        'The profile grants only system-DWM reads and owned-caption writes; ' +
        'Explorer activation remains denied.')

Add-Check `
    -Name 'profile.schema-identity' `
    -Passed (
        $schema.'$schema' -eq
            'https://json-schema.org/draft/2020-12/schema' -and
        $schema.title -eq
            'JarvisV2 Windows 10 exact host profiles') `
    -Detail 'The exact-host profile schema must remain explicit and versioned.'

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
    -Detail (($buildOutput | Select-Object -Last 10) -join
        [Environment]::NewLine)

if ($buildExitCode -eq 0 -and -not $StaticOnly) {
    $assemblyPath = Join-Path $sourceRoot (
        'bin\Release\net8.0-windows\' +
        'jarvis-win10-native-style-probe.dll')
    $explorerBefore = @(
        Get-Process -Name explorer -ErrorAction Stop |
            ForEach-Object {
                "$($_.Id):$($_.StartTime.ToUniversalTime().Ticks)"
            } |
            Sort-Object
    )

    $inspectOutput = @(
        & $DotnetPath $assemblyPath inspect 2>&1
    )
    $inspectExitCode = $LASTEXITCODE
    $inspectReceipt = $null
    try {
        $inspectReceipt =
            ($inspectOutput -join [Environment]::NewLine) |
                ConvertFrom-Json -Depth 30
    }
    catch {
        $inspectReceipt = $null
    }
    Add-Check `
        -Name 'live.exact-host-readonly-inspection' `
        -Passed (
            $inspectExitCode -eq 0 -and
            $null -ne $inspectReceipt -and
            $inspectReceipt.result -eq
                'passed-exact-own-process-candidate' -and
            $inspectReceipt.matchedProfileId -eq
                'win10-22h2-19045.6466-x64' -and
            $inspectReceipt.scope -eq 'own-process-hwnd-only' -and
            $inspectReceipt.ownProcessWindowExecutionSupported -and
            -not $inspectReceipt.explorerMutationSupported -and
            -not $inspectReceipt.activationPermitted -and
            -not $inspectReceipt.mutationPerformed -and
            $inspectReceipt.liveExplorer -eq 'not-run') `
        -Detail (
            "Inspect exit $inspectExitCode; result " +
            "$($inspectReceipt.result); profile " +
            "$($inspectReceipt.matchedProfileId).")

    $verifyOutput = @(
        & $DotnetPath $assemblyPath verify-owned-window 2>&1
    )
    $verifyExitCode = $LASTEXITCODE
    $verifyReceipt = $null
    try {
        $verifyReceipt =
            ($verifyOutput -join [Environment]::NewLine) |
                ConvertFrom-Json -Depth 30
    }
    catch {
        $verifyReceipt = $null
    }
    Add-Check `
        -Name 'live.owned-window-dwm-roundtrip' `
        -Passed (
            $verifyExitCode -eq 0 -and
            $null -ne $verifyReceipt -and
            $verifyReceipt.result -eq 'passed-own-window-only' -and
            $verifyReceipt.scope -eq 'own-process-hwnd-only' -and
            $verifyReceipt.ownWindowMutationPerformed -and
            -not $verifyReceipt.explorerMutationSupported -and
            -not $verifyReceipt.activationPermitted -and
            -not $verifyReceipt.systemMutationPerformed -and
            $verifyReceipt.liveExplorer -eq 'not-run' -and
            @($verifyReceipt.calls).Count -eq 2 -and
            @($verifyReceipt.calls |
                Where-Object hResult -lt 0).Count -eq 0) `
        -Detail (
            "Verify exit $verifyExitCode; result " +
            "$($verifyReceipt.result); calls " +
            "$(@($verifyReceipt.calls).Count).")

    $explorerAfter = @(
        Get-Process -Name explorer -ErrorAction Stop |
            ForEach-Object {
                "$($_.Id):$($_.StartTime.ToUniversalTime().Ticks)"
            } |
            Sort-Object
    )
    Add-Check `
        -Name 'live.explorer-process-identity-unchanged' `
        -Passed (($explorerBefore -join '|') -eq ($explorerAfter -join '|')) `
        -Detail (
            'Explorer PID/start-time identities must remain unchanged across ' +
            'the owned-window verification.')
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-win10-native-style-probe-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    scope = 'own-process-hwnd-only'
    explorerMutationSupported = $false
    activationPermitted = $false
    systemMutationPerformed = $false
    liveExplorer = 'not-run'
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 10

if (-not $passed) {
    exit 1
}
