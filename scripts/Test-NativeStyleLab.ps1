[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectPath =
    Join-Path $root 'src\platforms\windows11\Jarvis.NativeStyleLab\Jarvis.NativeStyleLab.csproj'
$sourceRoot = Join-Path $root 'src\platforms\windows11\Jarvis.NativeStyleLab'
$windowPath = Join-Path $sourceRoot 'MainWindow.xaml'
$stylerPath = Join-Path $sourceRoot 'DwmWindowStyler.cs'

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
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.xaml', '.csproj') |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$windowText = [IO.File]::ReadAllText($windowPath)
$stylerText = [IO.File]::ReadAllText($stylerPath)

$forbiddenRuntimePattern = (
    '(?i)\b(?:user32|kernel32|ntdll|advapi32|OpenProcess|' +
    'CreateRemoteThread|VirtualAllocEx|WriteProcessMemory|SetWindowsHookEx|' +
    'SendMessage|PostMessage|SetWindowLong|SetWindowPos|FindWindow|' +
    'EnumWindows|TerminateProcess|System\.Diagnostics\.Process|' +
    'ServiceController|Microsoft\.Win32\.Registry)\b'
)
Add-Check `
    -Name 'source.no-external-window-process-or-system-mutation-api' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenRuntimePattern)) `
    -Detail (
        'The style lab must contain no external-window discovery, process, ' +
        'service, registry, hook, injection or cross-process message API.'
    )

$dllImportMatches = [regex]::Matches(
    $stylerText,
    '\[DllImport\("dwmapi\.dll", ExactSpelling = true\)\]')
Add-Check `
    -Name 'source.only-reviewed-dwm-entrypoint' `
    -Passed (
        $dllImportMatches.Count -eq 2 -and
        [regex]::Matches($sourceText, '\[DllImport\(').Count -eq 2 -and
        $stylerText.Contains(
            'private static extern int DwmSetWindowAttribute(')
    ) `
    -Detail (
        'Exactly two typed overload declarations may bind the same reviewed ' +
        'dwmapi!DwmSetWindowAttribute entrypoint.'
    )

Add-Check `
    -Name 'source.owned-hwnd-only' `
    -Passed (
        $stylerText.Contains(
            'new WindowInteropHelper(ownedWindow).Handle') -and
        -not $stylerText.Contains('new WindowInteropHelper(' + 'this') -and
        $stylerText.Contains(
            'The style lab could not obtain its own HWND.')
    ) `
    -Detail (
        'Every DWM call must use the HWND obtained from the Window instance ' +
        'owned by this process.'
    )

Add-Check `
    -Name 'contract.documented-dwm-attributes' `
    -Passed (
        $stylerText.Contains('UseImmersiveDarkMode = 20') -and
        $stylerText.Contains('WindowCornerPreference = 33') -and
        $stylerText.Contains('BorderColor = 34') -and
        $stylerText.Contains('CaptionColor = 35') -and
        $stylerText.Contains('TextColor = 36') -and
        $stylerText.Contains('SystemBackdropType = 38') -and
        $stylerText.Contains('BackdropMainWindow = 2') -and
        $stylerText.Contains('BackdropTransientWindow = 3') -and
        $stylerText.Contains('BackdropTabbedWindow = 4')
    ) `
    -Detail (
        'The lab must pin only the reviewed Windows 11 DWM attribute and ' +
        'system-backdrop constants.'
    )

$forbiddenSurfacePattern = (
    '(?i)(?:Topmost\s*=\s*"True"|AllowsTransparency\s*=\s*"True"|' +
    'WindowState\s*=\s*"Maximized"|ShowInTaskbar\s*=\s*"False"|' +
    'WindowStyle\s*=\s*"None")'
)
Add-Check `
    -Name 'xaml.ordinary-native-window-and-rollback' `
    -Passed (
        -not [regex]::IsMatch($windowText, $forbiddenSurfacePattern) -and
        $windowText.Contains('WindowStyle="SingleBorderWindow"') -and
        $windowText.Contains('ResizeMode="CanResizeWithGrip"') -and
        $windowText.Contains('Content="SYSTEM DEFAULT"') -and
        $windowText.Contains('OWN PROCESS ONLY') -and
        $windowText.Contains('NO EXPLORER OR DESKTOP MUTATION')
    ) `
    -Detail (
        'The live lab must remain a normal system-framed window with an ' +
        'explicit system-default rollback and visible own-process scope.'
    )

$buildOutput = @(
    & dotnet build $projectPath --configuration Release --nologo 2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 8) -join [Environment]::NewLine)

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-native-style-lab-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    scope = 'own-process-hwnd-only'
    explorerMutationSupported = $false
    activationPermitted = $false
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 8

if (-not $passed) {
    exit 1
}
