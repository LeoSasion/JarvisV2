[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectPath =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerHostModel\Jarvis.ExplorerHostModel.csproj'
$sourceRoot = Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerHostModel'
$schemaPath =
    Join-Path $root 'config\explorer-host-offline-plan.schema.json'
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) ("jarvis2-explorer-host-model-" + [Guid]::NewGuid().ToString('N'))

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

function Copy-JsonObject {
    param([Parameter(Mandatory)] [object]$Value)

    return $Value |
        ConvertTo-Json -Depth 30 |
        ConvertFrom-Json -Depth 30 -DateKind String
}

function Invoke-ModelScenario {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [object]$Snapshot,
        [Parameter(Mandatory)] [string]$ExpectedResult,
        [string]$ExpectedFailure
    )

    $fixturePath = Join-Path $temporaryRoot "$Name.json"
    $Snapshot |
        ConvertTo-Json -Depth 30 |
        Set-Content -LiteralPath $fixturePath -Encoding utf8NoBOM

    $output = @(
        & dotnet run `
            --project $projectPath `
            --configuration Release `
            --no-build `
            -- evaluate-fixture $fixturePath 2>&1
    )
    $exitCode = $LASTEXITCODE
    $json = $output -join [Environment]::NewLine
    $receipt = $json | ConvertFrom-Json -Depth 30
    $schemaPassed =
        $json | Test-Json -SchemaFile $schemaPath -ErrorAction Stop
    $expectedExitCode = if ($ExpectedResult -eq 'passed-offline-plan') {
        0
    }
    else {
        12
    }
    $failurePresent =
        [string]::IsNullOrEmpty($ExpectedFailure) -or
        @($receipt.failures) -contains $ExpectedFailure
    $boundaryPassed =
        -not $receipt.executionSupported -and
        -not $receipt.activationPermitted -and
        -not $receipt.mutationPerformed -and
        $receipt.liveExplorer -eq 'not-run'

    Add-Check `
        -Name "scenario.$Name" `
        -Passed (
            $schemaPassed -and
            $exitCode -eq $expectedExitCode -and
            $receipt.result -eq $ExpectedResult -and
            $failurePresent -and
            $boundaryPassed
        ) `
        -Detail (
            "Expected $ExpectedResult / exit $expectedExitCode / " +
            "failure '$ExpectedFailure'; observed $($receipt.result) / " +
            "exit $exitCode. Failures: $(@($receipt.failures) -join ', ')."
        )

    return $receipt
}

$baseSnapshot = [ordered]@{
    schemaVersion = 1
    evidenceKind = 'offline-fixture'
    liveSystemTouched = $false
    currentSessionId = 3
    killSwitchState = 'armed'
    activeModulePermitState = 'absent'
    legacyHost = [ordered]@{
        quarantined = $true
        serviceState = 'Stopped'
        serviceProcessId = 0
        baseRuntimeMappingCount = 0
    }
    selection = [ordered]@{
        mode = 'shell-window-exact'
        processEnumerationPerformed = $false
        shellWindowPresent = $true
        shellWindowProcessId = 4242
        shellWindowThreadId = 9001
        desktopShellCandidateCount = 1
    }
    target = [ordered]@{
        processId = 4242
        sessionId = 3
        processName = 'explorer.exe'
        imagePath = 'C:\Windows\explorer.exe'
        expectedImagePath = 'C:\Windows\explorer.exe'
        imageSha256 =
            'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'
        expectedImageSha256 =
            'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'
        productVersion = '10.0.26200.8875'
        expectedProductVersion = '10.0.26200.8875'
        signatureState = 'trusted'
        signerSubject = 'Microsoft Windows'
        expectedSignerSubject = 'Microsoft Windows'
        architecture = 'amd64'
        startTimeUtc = '2026-07-27T05:00:00.0000000+00:00'
    }
    module = [ordered]@{
        moduleId = 'jarvis-explorer-bridge'
        contract = 'standalone-explicit-init-v1'
        path = 'C:\JarvisV2\jarvis-explorer-bridge.dll'
        sha256 =
            'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB'
        expectedSha256 =
            'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB'
        signatureState = 'trusted'
        signerSubject = 'JarvisV2 Release Signing'
        expectedSignerSubject = 'JarvisV2 Release Signing'
        architecture = 'amd64'
    }
    existingMappings = @()
}

$sourceText = @(
    Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$forbiddenRuntimePattern = (
    '(?i)\b(?:DllImport|LibraryImport|OpenProcess|CreateRemoteThread|' +
    'VirtualAllocEx|WriteProcessMemory|SetWindowsHookEx|NtQueueApcThread|' +
    'StartService|ServiceController|Microsoft\.Win32\.Registry|' +
    'System\.Diagnostics\.Process)\b'
)
Add-Check `
    -Name 'source.no-live-process-or-injection-api' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenRuntimePattern)) `
    -Detail (
        'The offline model must contain no process, service, registry, ' +
        'injection, hook-installation or P/Invoke API.'
    )
Add-Check `
    -Name 'source.fixture-only-entrypoint' `
    -Passed (
        $sourceText.Contains('evaluate-fixture') -and
        $sourceText.Contains('ExecutionSupported = false') -and
        $sourceText.Contains('ActivationPermitted = false') -and
        $sourceText.Contains('LiveExplorer = "not-run"') -and
        $sourceText.Contains('MutationPerformed = false')
    ) `
    -Detail (
        'The only entrypoint must evaluate a fixture and every receipt must ' +
        'deny execution, activation and live evidence.'
    )

$buildOutput = @(
    & dotnet build $projectPath --configuration Release --nologo 2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 8) -join [Environment]::NewLine)

if ($buildExitCode -eq 0) {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    try {
        $validReceipt = Invoke-ModelScenario `
            -Name 'valid-exact-shell-window' `
            -Snapshot (Copy-JsonObject $baseSnapshot) `
            -ExpectedResult 'passed-offline-plan'
        Add-Check `
            -Name 'scenario.valid-candidate-bounded' `
            -Passed (
                $null -ne $validReceipt.candidate -and
                $validReceipt.candidate.processId -eq 4242 -and
                $validReceipt.candidate.threadId -eq 9001 -and
                $validReceipt.candidate.hookScope -eq 'single-thread' -and
                $validReceipt.candidate.requiresLiveImplementationReview -and
                $validReceipt.candidate.requiresExactUserApproval
            ) `
            -Detail (
                'A passing fixture may produce only one explicit PID/TID ' +
                'review candidate, never an executable activation plan.'
            )

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.selection.shellWindowProcessId = 5000
        $null = Invoke-ModelScenario `
            -Name 'shell-window-owner-mismatch' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'target-does-not-own-shell-window'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.selection.shellWindowThreadId = 0
        $null = Invoke-ModelScenario `
            -Name 'zero-thread-global-scope-risk' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'shell-window-thread-id-invalid'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.selection.processEnumerationPerformed = $true
        $null = Invoke-ModelScenario `
            -Name 'process-enumeration-forbidden' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'process-enumeration-forbidden'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.selection.desktopShellCandidateCount = 2
        $null = Invoke-ModelScenario `
            -Name 'multiple-shell-candidates' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'desktop-shell-candidate-count-not-one'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.target.processName = 'dwm.exe'
        $null = Invoke-ModelScenario `
            -Name 'dwm-target-rejected' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'target-is-not-explorer'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.target.sessionId = 4
        $null = Invoke-ModelScenario `
            -Name 'session-mismatch' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'target-session-mismatch'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.target.signatureState = 'untrusted'
        $null = Invoke-ModelScenario `
            -Name 'target-signature-untrusted' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'target-signature-mismatch'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.killSwitchState = 'disarmed'
        $null = Invoke-ModelScenario `
            -Name 'kill-switch-disarmed' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'kill-switch-not-armed'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.activeModulePermitState = 'valid'
        $null = Invoke-ModelScenario `
            -Name 'permit-present' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'active-module-permit-present'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.legacyHost.serviceState = 'Running'
        $scenario.legacyHost.serviceProcessId = 1234
        $null = Invoke-ModelScenario `
            -Name 'legacy-service-running' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'legacy-host-service-not-stopped'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.legacyHost.baseRuntimeMappingCount = 1
        $null = Invoke-ModelScenario `
            -Name 'legacy-base-runtime-mapped' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'legacy-host-base-runtime-mapped'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.module.contract = 'windhawk-mod-v1'
        $null = Invoke-ModelScenario `
            -Name 'current-windhawk-mod-contract-rejected' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'module-contract-not-standalone'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.module.sha256 =
            'CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC'
        $null = Invoke-ModelScenario `
            -Name 'module-hash-drift' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'module-sha256-mismatch'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.existingMappings = @(
            [ordered]@{
                processId = 7777
                moduleName = 'windhawk.dll'
                path = 'C:\Program Files\Windhawk\Engine\windhawk.dll'
            }
        )
        $null = Invoke-ModelScenario `
            -Name 'existing-windhawk-mapping' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'legacy-or-target-runtime-already-mapped'

        $scenario = Copy-JsonObject $baseSnapshot
        $scenario.liveSystemTouched = $true
        $null = Invoke-ModelScenario `
            -Name 'live-touch-claim-rejected' `
            -Snapshot $scenario `
            -ExpectedResult 'blocked' `
            -ExpectedFailure 'fixture-claims-live-system-touch'
    }
    finally {
        $resolvedTemporaryRoot =
            [IO.Path]::GetFullPath($temporaryRoot)
        $resolvedSystemTemporaryRoot =
            [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (
            $resolvedTemporaryRoot.StartsWith(
                $resolvedSystemTemporaryRoot,
                [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($resolvedTemporaryRoot).StartsWith(
                'jarvis2-explorer-host-model-',
                [StringComparison]::Ordinal)
        ) {
            Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
        }
        else {
            throw "Refusing to remove unexpected temp path: $resolvedTemporaryRoot"
        }
    }
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-explorer-host-model-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    executionSupported = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 8

if (-not $passed) {
    exit 1
}
