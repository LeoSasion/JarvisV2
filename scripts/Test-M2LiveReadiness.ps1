[CmdletBinding()]
param(
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$moduleId = 'jarvis-taskbar-icon-size'
$schemaPath = Join-Path $root 'config\m2-live-readiness-receipt.schema.json'
$compatibilityPath = Join-Path $root 'config\compatibility.json'
$nativeReceiptPath =
    Join-Path $root 'docs\receipts\native-build-2026-07-22.json'
$m2SourcePath = Join-Path $root 'mods\windows11\jarvis-taskbar-icon-size.wh.cpp'
$supervisorDll = Join-Path $root (
    'src\platforms\windows11\Jarvis.Supervisor\bin\Release\net8.0-windows\' +
    'jarvis-supervisor.dll'
)
$recoveryPath = Join-Path $root 'docs\RECOVERY.md'
$allowedOutputRoot =
    Join-Path $root 'artifacts\m2-live-readiness\runs'
$expectedWindhawkBaseDllPath =
    Join-Path $env:ProgramFiles 'Windhawk\Engine\1.7.3\64\windhawk.dll'
$expectedWindhawkBaseDllSize = 979544
$expectedWindhawkBaseDllSha256 =
    '0AAD074CAF156200BE7A77E4615F9171CEA884CDE96BAF90397366C28C4F10A1'
$hostActivationQuarantineReason =
    'windhawk-service-global-runtime-injection-observed-20260727'
$hostActivationIncidentAtUtc = '2026-07-27T05:28:39.5311113Z'
$exactCommand = (
    'dotnet run --project .\src\platforms\windows11\Jarvis.Supervisor ' +
    '--configuration Release --no-build -- clear-kill-switch ' +
    '--module jarvis-taskbar-icon-size --confirm'
)
$recoveryCommand = (
    'dotnet run --project .\src\platforms\windows11\Jarvis.Supervisor ' +
    '--configuration Release --no-build -- arm-kill-switch'
)

$errors = [System.Collections.Generic.List[string]]::new()

function Add-Failure {
    param([Parameter(Mandatory)] [string]$Code)
    if (-not $errors.Contains($Code)) {
        $errors.Add($Code)
    }
}

Add-Failure 'windhawk-host-activation-quarantined'

function Get-FileIdentity {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$RelativePath
    )

    $item = Get-Item -LiteralPath $Path -Force
    return [ordered]@{
        relativePath = $RelativePath.Replace('\', '/')
        size = [int64]$item.Length
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }
}

function Resolve-OutputPath {
    param([Parameter(Mandatory)] [string]$Path)

    $candidate = if ([IO.Path]::IsPathRooted($Path)) {
        [IO.Path]::GetFullPath($Path)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $root $Path))
    }
    $allowed = [IO.Path]::GetFullPath($allowedOutputRoot).TrimEnd('\')
    if (-not $candidate.StartsWith(
            $allowed + '\',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputPath must stay under $allowed."
    }
    if (Test-Path -LiteralPath $candidate) {
        throw "Refusing to overwrite an existing readiness receipt."
    }
    return $candidate
}

$sourceIdentity = [ordered]@{}
foreach ($source in @(
    [pscustomobject]@{
        Key = 'readinessScript'
        Path = $PSCommandPath
        RelativePath = 'scripts/Test-M2LiveReadiness.ps1'
    },
    [pscustomobject]@{
        Key = 'receiptSchema'
        Path = $schemaPath
        RelativePath = 'config/m2-live-readiness-receipt.schema.json'
    },
    [pscustomobject]@{
        Key = 'compatibilityManifest'
        Path = $compatibilityPath
        RelativePath = 'config/compatibility.json'
    },
    [pscustomobject]@{
        Key = 'nativeBuildReceipt'
        Path = $nativeReceiptPath
        RelativePath = 'docs/receipts/native-build-2026-07-22.json'
    },
    [pscustomobject]@{
        Key = 'm2Source'
        Path = $m2SourcePath
        RelativePath = 'mods/windows11/jarvis-taskbar-icon-size.wh.cpp'
    }
)) {
    try {
        $sourceIdentity[$source.Key] =
            Get-FileIdentity $source.Path $source.RelativePath
    }
    catch {
        Add-Failure "source-identity-$($source.Key)-unavailable"
    }
}

$stateRoot =
    Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'JARVIS2'
$killSwitchPath = Join-Path $stateRoot 'disabled.flag'
$permitPath = Join-Path $stateRoot 'active-module.txt'
$killSwitchExists = $false
$killSwitchSha256 = $null
$killSwitchState = 'unknown'
$permitExists = $false
$permitState = 'unknown'

try {
    $killSwitchExists =
        Test-Path -LiteralPath $killSwitchPath -PathType Leaf
    $killSwitchState = if ($killSwitchExists) { 'armed' } else { 'missing' }
    if ($killSwitchExists) {
        $killSwitchSha256 =
            (Get-FileHash -LiteralPath $killSwitchPath -Algorithm SHA256).Hash
    }
    if (-not $killSwitchExists) {
        Add-Failure 'kill-switch-not-armed'
    }
}
catch {
    Add-Failure 'kill-switch-state-unknown'
}

try {
    $permitExists = Test-Path -LiteralPath $permitPath -PathType Leaf
    $permitState = if ($permitExists) { 'present' } else { 'absent' }
    if ($permitExists) {
        Add-Failure 'active-module-permit-present'
    }
}
catch {
    Add-Failure 'active-module-permit-state-unknown'
}

$serviceReceipt = [ordered]@{
    found = $false
    count = 0
    state = $null
    startMode = $null
    processId = $null
}
try {
    $services = @(
        Get-CimInstance -ClassName Win32_Service |
            Where-Object {
                $_.Name -eq 'Windhawk' -or
                $_.DisplayName -eq 'Windhawk'
            }
    )
    $serviceReceipt.count = $services.Count
    $serviceReceipt.found = $services.Count -eq 1
    if ($services.Count -eq 1) {
        $serviceReceipt.state = [string]$services[0].State
        $serviceReceipt.startMode = [string]$services[0].StartMode
        $serviceReceipt.processId = [int]$services[0].ProcessId
        if ($serviceReceipt.state -ne 'Stopped' -or
            $serviceReceipt.startMode -ne 'Manual' -or
            $serviceReceipt.processId -ne 0) {
            Add-Failure 'windhawk-service-not-stopped-manual'
        }
    }
    else {
        Add-Failure 'windhawk-service-identity-not-unique'
    }
}
catch {
    Add-Failure 'windhawk-service-inspection-failed'
}

$supervisorReport = $null
$compatibilityReceipt = [ordered]@{
    profileId = $null
    compatible = $false
    checksPassed = 0
    checkCount = 0
    killSwitchState = $null
    activeModuleState = $null
    explorerProcessIds = @()
}
try {
    if (-not (Test-Path -LiteralPath $supervisorDll -PathType Leaf)) {
        throw 'Release supervisor build is missing.'
    }
    $inspectOutput = & dotnet $supervisorDll inspect 2>&1
    $inspectExitCode = $LASTEXITCODE
    if ($inspectExitCode -ne 0) {
        throw "Supervisor inspect returned $inspectExitCode."
    }
    $supervisorReport =
        (($inspectOutput | ForEach-Object { [string]$_ }) -join
            [Environment]::NewLine) |
            ConvertFrom-Json -Depth 100
    $compatibilityReceipt.profileId = [string]$supervisorReport.profileId
    $compatibilityReceipt.compatible = [bool]$supervisorReport.compatible
    $compatibilityReceipt.checkCount = @($supervisorReport.checks).Count
    $compatibilityReceipt.checksPassed = @(
        $supervisorReport.checks | Where-Object passed
    ).Count
    $compatibilityReceipt.killSwitchState =
        [string]$supervisorReport.host.killSwitchState
    $compatibilityReceipt.activeModuleState =
        [string]$supervisorReport.host.activeModuleState
    $compatibilityReceipt.explorerProcessIds =
        @($supervisorReport.host.explorerProcessIds | ForEach-Object { [int]$_ })
    if (-not $compatibilityReceipt.compatible -or
        $compatibilityReceipt.checkCount -eq 0 -or
        $compatibilityReceipt.checksPassed -ne
            $compatibilityReceipt.checkCount) {
        Add-Failure 'supervisor-compatibility-failed'
    }
    if ($compatibilityReceipt.killSwitchState -ne 'armed' -or
        $compatibilityReceipt.activeModuleState -ne 'absent') {
        Add-Failure 'supervisor-state-not-locked'
    }
}
catch {
    Add-Failure 'supervisor-inspect-failed'
}

$canonicalBuild = [ordered]@{
    runId = $null
    status = $null
    canonicalFullRun = $false
    receiptSha256 = $null
    runSummarySha256 = $null
    m2SourceSha256 = $null
    warningCount = $null
    errorCount = $null
    activationPermitted = $false
    liveExplorer = 'not-run'
}
try {
    $compatibilityManifest =
        Get-Content -LiteralPath $compatibilityPath -Raw |
            ConvertFrom-Json -Depth 100
    $m1Manifests = @(
        $compatibilityManifest.modules |
            Where-Object id -eq 'jarvis-native-taskbar'
    )
    $m2Manifests = @(
        $compatibilityManifest.modules |
            Where-Object id -eq $moduleId
    )
    if ($m1Manifests.Count -ne 1 -or
        $m1Manifests[0].supervisorActivationEligible -ne $false -or
        $m2Manifests.Count -ne 1 -or
        $m2Manifests[0].supervisorActivationEligible -ne $true) {
        Add-Failure 'module-allowlist-boundary-invalid'
    }

    $nativeReceipt =
        Get-Content -LiteralPath $nativeReceiptPath -Raw |
            ConvertFrom-Json -Depth 100
    $canonicalBuild.receiptSha256 =
        (Get-FileHash -LiteralPath $nativeReceiptPath -Algorithm SHA256).Hash
    $summaryRelativePath =
        [string]$nativeReceipt.runSummary.relativePath
    if ([IO.Path]::IsPathRooted($summaryRelativePath)) {
        throw 'Canonical summary path is rooted.'
    }
    $summaryPath =
        [IO.Path]::GetFullPath(
            (Join-Path $root $summaryRelativePath.Replace('/', '\')))
    $allowedSummaryRoot =
        [IO.Path]::GetFullPath(
            (Join-Path $root 'artifacts\native\runs')).TrimEnd('\')
    if (-not $summaryPath.StartsWith(
            $allowedSummaryRoot + '\',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Canonical summary path escaped the runs root.'
    }
    $actualSummarySha256 =
        (Get-FileHash -LiteralPath $summaryPath -Algorithm SHA256).Hash
    if ($actualSummarySha256 -ne
        [string]$nativeReceipt.runSummary.sha256) {
        throw 'Canonical summary hash mismatch.'
    }
    $runSummary =
        Get-Content -LiteralPath $summaryPath -Raw |
            ConvertFrom-Json -Depth 100
    $canonicalBuild.runId = [string]$runSummary.runId
    $canonicalBuild.status = [string]$runSummary.status
    $canonicalBuild.canonicalFullRun =
        [bool]$runSummary.canonicalFullRun
    $canonicalBuild.runSummarySha256 = $actualSummarySha256
    $canonicalBuild.activationPermitted =
        [bool]$runSummary.activationPermitted
    $canonicalBuild.liveExplorer = [string]$runSummary.liveExplorer

    $m2Builds = @($runSummary.modules | Where-Object id -eq $moduleId)
    if ($m2Builds.Count -ne 1) {
        throw 'Canonical run does not contain exactly one M2 result.'
    }
    $canonicalBuild.m2SourceSha256 = [string]$m2Builds[0].sourceSha256
    $canonicalBuild.warningCount = [int]$m2Builds[0].result.warningCount
    $canonicalBuild.errorCount = [int]$m2Builds[0].result.errorCount
    $currentM2Sha256 =
        (Get-FileHash -LiteralPath $m2SourcePath -Algorithm SHA256).Hash
    if ($nativeReceipt.schemaVersion -ne 3 -or
        $nativeReceipt.runId -ne $runSummary.runId -or
        $canonicalBuild.status -ne 'complete' -or
        -not $canonicalBuild.canonicalFullRun -or
        $canonicalBuild.m2SourceSha256 -ne $currentM2Sha256 -or
        $canonicalBuild.warningCount -ne 0 -or
        $canonicalBuild.errorCount -ne 0 -or
        $canonicalBuild.activationPermitted -or
        $canonicalBuild.liveExplorer -ne 'not-run') {
        Add-Failure 'canonical-build-boundary-failed'
    }
}
catch {
    Add-Failure 'canonical-build-inspection-failed'
}

$processes = @()
$namedProcesses = @()
$moduleMappings =
    [System.Collections.Generic.List[object]]::new()
$moduleEnumerationErrors =
    [System.Collections.Generic.List[object]]::new()
$moduleEnumerableProcessIds =
    [System.Collections.Generic.List[int]]::new()
$moduleNotEnumerableProcessCount = 0
$jarvisModuleMappings = @()
$acceptedBaseRuntimeMappings = @()
$unexpectedWindhawkRuntimeMappings = @()
$safetyRelevantModuleEnumerationErrorCount = 0
$nonTargetModuleEnumerationErrorCount = 0
$mappingPattern = (
    '(?i)(windhawk|jarvis[-_]?native[-_]?taskbar|' +
    'jarvis[-_]?taskbar[-_]?icon[-_]?size|\\JARVIS2\\)'
)
try {
    $processes = @(Get-Process | Sort-Object Id)
    $namedProcesses = @(
        $processes |
            Where-Object {
                $_.ProcessName -match '(?i)^(windhawk|jarvis)'
            }
    )
    foreach ($process in $processes) {
        try {
            $modules = @(
                $process.Modules |
                    Where-Object { $null -ne $_ }
            )
            if ($modules.Count -eq 0) {
                $moduleNotEnumerableProcessCount++
                continue
            }
            $moduleEnumerableProcessIds.Add([int]$process.Id)
            foreach ($module in $modules) {
                $fileName = $null
                try {
                    $fileName = $module.FileName
                }
                catch {
                }
                if ("$($module.ModuleName)|$fileName" -match $mappingPattern) {
                    $isJarvis =
                        [string]$module.ModuleName -match '(?i)^jarvis-' -or
                        [string]$fileName -match
                            '(?i)[\\/]jarvis-[^\\/]+\.dll$'
                    $moduleMappings.Add([ordered]@{
                        processId = [int]$process.Id
                        processName = [string]$process.ProcessName
                        moduleName = [string]$module.ModuleName
                        path = [string]$fileName
                        isJarvis = [bool]$isJarvis
                    })
                }
            }
        }
        catch {
            $moduleEnumerationErrors.Add([ordered]@{
                processId = [int]$process.Id
                processName = [string]$process.ProcessName
                errorType = $_.Exception.GetType().Name
            })
        }
    }
    if ($namedProcesses.Count -ne 0) {
        Add-Failure 'windhawk-or-jarvis-process-running'
    }
    $jarvisModuleMappings =
        @($moduleMappings | Where-Object { [bool]$_['isJarvis'] })
    $windhawkBaseIdentityValid = $false
    try {
        $windhawkBaseItem =
            Get-Item -LiteralPath $expectedWindhawkBaseDllPath -Force
        $windhawkBaseIdentityValid =
            -not (
                $windhawkBaseItem.Attributes -band
                [IO.FileAttributes]::ReparsePoint
            ) -and
            [string]$windhawkBaseItem.VersionInfo.ProductVersion -ceq
                '1.7.3' -and
            [int64]$windhawkBaseItem.Length -eq
                $expectedWindhawkBaseDllSize -and
            (Get-FileHash `
                -LiteralPath $expectedWindhawkBaseDllPath `
                -Algorithm SHA256).Hash -ceq
                $expectedWindhawkBaseDllSha256
    }
    catch {
        $windhawkBaseIdentityValid = $false
    }
    $acceptedBaseRuntimeMappings = @(
        $moduleMappings |
            Where-Object {
                -not [bool]$_['isJarvis'] -and
                $windhawkBaseIdentityValid -and
                [string]$_['moduleName'] -ceq 'windhawk.dll' -and
                [string]$_['path'] -ieq $expectedWindhawkBaseDllPath -and
                [int]$_['processId'] -notin
                    @($compatibilityReceipt.explorerProcessIds) -and
                [string]$_['processName'] -notmatch
                    '(?i)^(windhawk|jarvis)'
            }
    )
    $unexpectedWindhawkRuntimeMappings = @(
        $moduleMappings |
            Where-Object {
                -not [bool]$_['isJarvis'] -and
                (
                    -not $windhawkBaseIdentityValid -or
                    [string]$_['moduleName'] -cne 'windhawk.dll' -or
                    [string]$_['path'] -ine $expectedWindhawkBaseDllPath -or
                    [int]$_['processId'] -in
                        @($compatibilityReceipt.explorerProcessIds) -or
                    [string]$_['processName'] -match
                        '(?i)^(windhawk|jarvis)'
                )
            }
    )
    if ($jarvisModuleMappings.Count -ne 0) {
        Add-Failure 'jarvis-module-mapped'
    }
    if ($unexpectedWindhawkRuntimeMappings.Count -ne 0) {
        Add-Failure 'unexpected-windhawk-runtime-mapped'
    }
    $explorerProcessIds =
        @($compatibilityReceipt.explorerProcessIds | ForEach-Object { [int]$_ })
    $safetyRelevantModuleEnumerationErrors = @(
        $moduleEnumerationErrors |
            Where-Object {
                [int]$_['processId'] -in $explorerProcessIds -or
                [string]$_['processName'] -match '(?i)^(windhawk|jarvis)'
            }
    )
    $safetyRelevantModuleEnumerationErrorCount =
        $safetyRelevantModuleEnumerationErrors.Count
    $nonTargetModuleEnumerationErrorCount =
        $moduleEnumerationErrors.Count -
        $safetyRelevantModuleEnumerationErrorCount
    if ($safetyRelevantModuleEnumerationErrorCount -ne 0) {
        Add-Failure 'safety-relevant-process-module-enumeration-incomplete'
        foreach ($errorType in @(
            $safetyRelevantModuleEnumerationErrors |
                ForEach-Object { [string]$_['errorType'] } |
                Sort-Object -Unique
        )) {
            Add-Failure (
                'safety-relevant-process-module-enumeration-error:' +
                $errorType
            )
        }
    }
}
catch {
    Add-Failure 'runtime-process-inspection-failed'
    Add-Failure (
        'runtime-process-inspection-error:' +
        $_.Exception.GetType().Name
    )
}

$explorerProcessIds =
    @($compatibilityReceipt.explorerProcessIds | ForEach-Object { [int]$_ })
$explorerMatchingModuleCount = @(
    $moduleMappings |
        Where-Object { $_.processId -in $explorerProcessIds }
).Count
$explorerModuleInspectionSucceeded =
    $explorerProcessIds.Count -gt 0 -and
    @(
        $explorerProcessIds |
            Where-Object { $_ -notin $moduleEnumerableProcessIds }
    ).Count -eq 0
if ($explorerMatchingModuleCount -ne 0) {
    Add-Failure 'explorer-has-matching-module'
}
if (-not $explorerModuleInspectionSucceeded) {
    Add-Failure 'explorer-module-inspection-incomplete'
}

try {
    $recoveryText =
        [System.IO.File]::ReadAllText($recoveryPath)
    if (-not $recoveryText.Contains(
            '急停是**加载互锁和运行时静默请求**，不是结束进程的按钮。') -or
        -not $recoveryText.Contains('M1 继续 build-only')) {
        Add-Failure 'recovery-contract-missing'
    }
}
catch {
    Add-Failure 'recovery-contract-unreadable'
}

$passed = $errors.Count -eq 0
$runId = (
    [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') +
    '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
)
$receipt = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-m2-live-readiness'
    runId = $runId
    inspectedAtUtc = [DateTime]::UtcNow.ToString('o')
    result = if ($passed) { 'passed' } else { 'failed' }
    offlineReadinessPassed = $passed
    readyForExactApproval = $passed
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    requestedModule = $moduleId
    hostActivation = [ordered]@{
        state = 'quarantined'
        reason = $hostActivationQuarantineReason
        incidentAtUtc = $hostActivationIncidentAtUtc
        activationPermitted = $false
    }
    sourceIdentity = $sourceIdentity
    killSwitch = [ordered]@{
        path = $killSwitchPath
        state = $killSwitchState
        exists = $killSwitchExists
        sha256 = $killSwitchSha256
    }
    activeModulePermit = [ordered]@{
        path = $permitPath
        state = $permitState
        exists = $permitExists
    }
    windhawkService = $serviceReceipt
    compatibility = $compatibilityReceipt
    canonicalBuild = $canonicalBuild
    runtime = [ordered]@{
        namedProcessCount = $namedProcesses.Count
        processCount = $processes.Count
        moduleEnumerableProcessCount = $moduleEnumerableProcessIds.Count
        moduleNotEnumerableProcessCount = $moduleNotEnumerableProcessCount
        moduleMappingCount = $moduleMappings.Count
        jarvisModuleMappingCount = $jarvisModuleMappings.Count
        acceptedBaseRuntimeMappingCount =
            $acceptedBaseRuntimeMappings.Count
        unexpectedWindhawkRuntimeMappingCount =
            $unexpectedWindhawkRuntimeMappings.Count
        moduleEnumerationErrorCount = $moduleEnumerationErrors.Count
        safetyRelevantModuleEnumerationErrorCount =
            $safetyRelevantModuleEnumerationErrorCount
        nonTargetModuleEnumerationErrorCount =
            $nonTargetModuleEnumerationErrorCount
        explorerProcessIds = $explorerProcessIds
        explorerMatchingModuleCount = $explorerMatchingModuleCount
        explorerModuleInspectionSucceeded =
            $explorerModuleInspectionSucceeded
    }
    approval = [ordered]@{
        exactCommand = $exactCommand
        recoveryCommand = $recoveryCommand
        exactCommandApproved = $false
        recoveryTerminalAvailable = $false
        canExecuteNow = $false
    }
    errors = @($errors)
}
$json = $receipt | ConvertTo-Json -Depth 20

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutputPath = Resolve-OutputPath $OutputPath
    $outputDirectory = Split-Path -Parent $resolvedOutputPath
    $null = [IO.Directory]::CreateDirectory($outputDirectory)
    $temporaryPath =
        Join-Path $outputDirectory ('.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            $json + [Environment]::NewLine,
            [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporaryPath -Destination $resolvedOutputPath
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

Write-Output $json
if (-not $passed) {
    exit 1
}
