[CmdletBinding()]
param(
    [switch]$StaticOnly,
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.DesktopStyleSession')
$projectPath = Join-Path $sourceRoot (
    'Jarvis.Win10.DesktopStyleSession.csproj')
$commonRoot = Join-Path $root (
    'src\common\Jarvis.DesktopStyleSession')
$surfaceProbeRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.ShellSurfaceProbe')
$profilePath = Join-Path $root 'config\windows10-host-profiles.json'

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

$adapterFiles = @(
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.csproj') |
        Sort-Object FullName
)
$adapterText = @(
    $adapterFiles | ForEach-Object {
        [IO.File]::ReadAllText($_.FullName)
    }
) -join [Environment]::NewLine
$commonText = @(
    Get-ChildItem -LiteralPath $commonRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.csproj') |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$surfaceProbeText = @(
    Get-ChildItem -LiteralPath $surfaceProbeRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.csproj') |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$transportText = [IO.File]::ReadAllText(
    (Join-Path $commonRoot 'DesktopListViewTransport.cs'))
$controllerText = [IO.File]::ReadAllText(
    (Join-Path $commonRoot 'DesktopStyleSessionController.cs'))
$profiles =
    Get-Content -LiteralPath $profilePath -Raw |
        ConvertFrom-Json
$profile = @($profiles.profiles)

Add-Check `
    -Name 'project.exact-reviewed-dependencies' `
    -Passed (
        $adapterText.Contains(
            'Jarvis.DesktopStyleSession.csproj') -and
        $adapterText.Contains(
            'Jarvis.Win10.HostAdmission.csproj') -and
        $adapterText.Contains(
            'Jarvis.Win10.ShellSurfaceProbe.csproj')) `
    -Detail (
        'The Win10 adapter must reuse the reviewed exact-host, read-only ' +
        'surface and bounded rollback projects.')

$forbiddenAdapterPattern = (
    '(?i)\b(?:DllImport|LibraryImport|OpenProcess|CreateRemoteThread|' +
    'VirtualAllocEx|WriteProcessMemory|ReadProcessMemory|SetWindowsHookEx|' +
    'PostMessage|SendMessage|SetWindowLong|SetWindowPos|MoveWindow|' +
    'ShowWindow|DestroyWindow|TerminateProcess|Process\.Kill|' +
    'CloseMainWindow|ServiceController|RegistryKey\.SetValue|' +
    'Registry\.SetValue|RegistryKey\.Delete|Start-Process|Stop-Process|' +
    'explorer\.exe|windhawk\.exe)\b'
)
Add-Check `
    -Name 'adapter.no-native-or-host-mutation-api' `
    -Passed (-not [regex]::IsMatch(
        $adapterText,
        $forbiddenAdapterPattern)) `
    -Detail (
        'The platform adapter may admit and delegate only; it cannot add ' +
        'native hooks, injection, services, registry writes or Explorer ' +
        'lifecycle controls.')

Add-Check `
    -Name 'gate.exact-win10-and-shell-identity' `
    -Passed (
        $adapterText.Contains(
            'ShellSurfaceInspector.Inspect()') -and
        $adapterText.Contains(
            'admission.Passed') -and
        $adapterText.Contains(
            'ExactDesktopHostObserved') -and
        $adapterText.Contains(
            'ExactPrimaryTaskbarObserved') -and
        $adapterText.Contains(
            'DesktopShellProcessId')) `
    -Detail (
        'The adapter must bind the exact Win10 profile and the verified ' +
        'desktop/taskbar Shell PID before delegating.')

Add-Check `
    -Name 'gate.locked-state-required' `
    -Passed (
        $adapterText.Contains('"disabled.flag"') -and
        $adapterText.Contains('"active-module.txt"') -and
        $adapterText.Contains('"kill-switch-not-armed"') -and
        $adapterText.Contains('"one-shot-module-permit-present"') -and
        $adapterText.Contains('EnsureOrdinaryPath(') -and
        -not $adapterText.Contains('File.Delete(') -and
        -not $adapterText.Contains('File.Move(') -and
        -not $adapterText.Contains('File.Write')) `
    -Detail (
        'The adapter must require the armed kill switch and absent permit ' +
        'without modifying either path.')

Add-Check `
    -Name 'session.profile-bound-command-and-common-recovery' `
    -Passed (
        $commonText.Contains(
            'ForExactWindows10Host(') -and
        $commonText.Contains(
            'HostProfileId = context.HostProfileId') -and
        $commonText.Contains(
            'CommandRequiresExpectedExplorerProcessId') -and
        $commonText.Contains(
            'DesktopStyleSessionContext.Shared.CommandProjectPath') -and
        $commonText.Contains(
            'The desktop style journal host profile does not match')) `
    -Detail (
        'Plans and journals must record the Win10 profile, omit a user-supplied ' +
        'PID from adapter commands and retain the common exact-PID emergency ' +
        'rollback path.')

Add-Check `
    -Name 'session.bounded-single-property-engine-retained' `
    -Passed (
        $commonText.Contains('MinimumTtlSeconds = 10') -and
        $commonText.Contains('MaximumTtlSeconds = 60') -and
        $commonText.Contains('LVM_GETTEXTCOLOR') -and
        $commonText.Contains('LVM_SETTEXTCOLOR') -and
        $commonText.Contains('store.Prepare(journal);') -and
        $commonText.Contains('finally') -and
        $commonText.Contains('RollBackExactTarget(') -and
        $transportText.Contains('RedrawExactFolderView(') -and
        $transportText.Contains('RedrawInvalidate |') -and
        $transportText.Contains('RedrawErase |') -and
        $transportText.Contains('RedrawAllChildren |') -and
        $transportText.Contains('RedrawUpdateNow;') -and
        $controllerText.Contains(
            'journal.ApplyRedrawAttempted = true;') -and
        $controllerText.Contains(
            'journal.ApplyRedrawAccepted = true;') -and
        $controllerText.Contains(
            'journal.RollbackRedrawAttempted = true;') -and
        $controllerText.Contains(
            'journal.RollbackRedrawAccepted = true;') -and
        -not $transportText.Contains('HWND_BROADCAST') -and
        -not $surfaceProbeText.Contains('CreateRemoteThread')) `
    -Detail (
        'The existing 10-60 second, original-first, finally-rollback text-only ' +
        'transport remains the sole mutation engine and redraws only the ' +
        'already-admitted FolderView after apply and rollback.')

Add-Check `
    -Name 'profile.capability-granted-with-activation-denied' `
    -Passed (
        $profile.Count -eq 1 -and
        $profile[0].profileId -eq
            'win10-22h2-19045.6466-x64' -and
        $profile[0].status -eq
            'observed-caption-write-disabled-owned-overlay-visually-verified' -and
        @($profile[0].allowedCapabilities) -contains
            'run-bounded-desktop-text-color-preview' -and
        -not $profile[0].activationPermitted -and
        $profile[0].liveExplorer -eq 'not-run') `
    -Detail (
        'Only the exact observed Win10 profile grants the bounded preview; ' +
        'module activation remains denied.')

$buildOutput = @(
    & $DotnetPath build `
        $projectPath `
        --configuration Release `
        --nologo `
        --warnaserror 2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release-warning-free' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 10) -join
        [Environment]::NewLine)

$modelReceipt = $null
if ($buildExitCode -eq 0) {
    $assemblyPath = Join-Path $sourceRoot (
        'bin\Release\net8.0-windows\' +
        'jarvis-win10-desktop-style-session.dll')
    $modelOutput = @(
        & $DotnetPath $assemblyPath model-test 2>&1
    )
    $modelExitCode = $LASTEXITCODE
    try {
        $modelReceipt =
            ($modelOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $modelReceipt = $null
    }
    Add-Check `
        -Name 'model.fail-closed-scenarios' `
        -Passed (
            $modelExitCode -eq 0 -and
            $null -ne $modelReceipt -and
            $modelReceipt.result -eq 'passed' -and
            $modelReceipt.scenarioCount -eq 12 -and
            $modelReceipt.passedCount -eq 12 -and
            -not $modelReceipt.moduleActivationPermitted -and
            -not $modelReceipt.mutationPerformed -and
            $modelReceipt.liveExplorer -eq 'not-run') `
        -Detail (
            "Model exit $modelExitCode; scenarios " +
            "$($modelReceipt.passedCount)/$($modelReceipt.scenarioCount).")
}

if ($buildExitCode -eq 0 -and -not $StaticOnly) {
    $assemblyPath = Join-Path $sourceRoot (
        'bin\Release\net8.0-windows\' +
        'jarvis-win10-desktop-style-session.dll')
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
                ConvertFrom-Json
    }
    catch {
        $inspectReceipt = $null
    }
    $explorerAfter = @(
        Get-Process -Name explorer -ErrorAction Stop |
            ForEach-Object {
                "$($_.Id):$($_.StartTime.ToUniversalTime().Ticks)"
            } |
            Sort-Object
    )
    Add-Check `
        -Name 'live.readonly-profile-bound-inspection' `
        -Passed (
            $inspectExitCode -eq 0 -and
            $null -ne $inspectReceipt -and
            $inspectReceipt.result -eq 'passed-read-only' -and
            $inspectReceipt.hostProfileId -eq
                'win10-22h2-19045.6466-x64' -and
            -not $inspectReceipt.mutationSupported -and
            -not $inspectReceipt.activationPermitted -and
            -not $inspectReceipt.mutationPerformed -and
            (($explorerBefore -join '|') -eq
                ($explorerAfter -join '|'))) `
        -Detail (
            "Inspect exit $inspectExitCode; result " +
            "$($inspectReceipt.result); Explorer identity unchanged.")
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-win10-desktop-style-session-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    scenarioCount = if ($null -eq $modelReceipt) {
        0
    }
    else {
        $modelReceipt.scenarioCount
    }
    scenarioPassedCount = if ($null -eq $modelReceipt) {
        0
    }
    else {
        $modelReceipt.passedCount
    }
    moduleActivationPermitted = $false
    liveMutationRun = $false
    mutationPerformed = $false
    liveExplorer = if ($StaticOnly) {
        'not-run'
    }
    else {
        'read-only-inspection'
    }
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 10

if (-not $passed) {
    exit 1
}
