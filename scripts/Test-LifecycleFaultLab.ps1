[CmdletBinding()]
param(
    [string]$ToolCache = (Join-Path $env:LOCALAPPDATA 'JARVIS2\tool-cache\windhawk-1.7.3'),
    [string]$OutputPath,
    [ValidateRange(5, 300)]
    [int]$ProcessTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'The offline lifecycle fault lab requires PowerShell 7 or newer.'
}

$root = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $root 'artifacts\lifecycle-fault-lab'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $artifactRoot 'latest.json'
}

$m1SourcePath = Join-Path $root 'mods\jarvis-native-taskbar.wh.cpp'
$protocolHeaderPath = Join-Path $root 'mods\jarvis-resource-protocol.hpp'
$labSourcePath = Join-Path $root 'tests\native\jarvis_lifecycle_harness.cpp'
$runnerScriptPath = $PSCommandPath
$receiptSchemaPath = Join-Path $root 'config\offline-lifecycle-receipt.schema.json'
$testProjectPath = Join-Path $root 'scripts\Test-Project.ps1'
$toolchainLockPath = Join-Path $root 'config\toolchain-lock.json'
$runId = '{0}-{1}' -f [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$stagingRoot = Join-Path $artifactRoot '.staging'
$stageDirectory = Join-Path $stagingRoot $runId

$environmentVariablesClearedForLab = @(
    'CCC_OVERRIDE_OPTIONS',
    'CFLAGS',
    'CPPFLAGS',
    'C_INCLUDE_PATH',
    'CXXFLAGS',
    'CPLUS_INCLUDE_PATH',
    'CPATH',
    'GCC_EXEC_PREFIX',
    'INCLUDE',
    'LIB',
    'LDFLAGS',
    'LIBRARY_PATH',
    'OBJC_INCLUDE_PATH',
    'PYTHONHOME',
    'PYTHONINSPECT',
    'PYTHONPATH',
    'PYTHONSTARTUP',
    'SDKROOT'
)

function Get-NormalizedRelativePath {
    param(
        [Parameter(Mandatory)] [string]$BasePath,
        [Parameter(Mandatory)] [string]$Path
    )

    return [System.IO.Path]::GetRelativePath(
        [System.IO.Path]::GetFullPath($BasePath),
        [System.IO.Path]::GetFullPath($Path)
    ).Replace('\', '/')
}

function Assert-PathWithin {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Parent
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($fullParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing an offline-lab filesystem operation outside $fullParent`: $fullPath"
    }
    return $fullPath
}

function Assert-NoReparsePointsInPath {
    param([Parameter(Mandatory)] [string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrEmpty($pathRoot)) {
        throw "Path has no filesystem root: $fullPath"
    }

    $current = $pathRoot
    $relative = $fullPath.Substring($pathRoot.Length)
    foreach ($segment in $relative.Split(@('\', '/'), [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current)) {
            continue
        }

        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Reparse points aren't allowed in offline-lab paths: $($item.FullName)"
        }
    }

    return $fullPath
}

function Assert-NonSystemPath {
    param([Parameter(Mandatory)] [string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $forbiddenRoots = @(
        $env:SystemRoot,
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)}
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($forbiddenRoot in $forbiddenRoots) {
        $fullForbiddenRoot = [System.IO.Path]::GetFullPath($forbiddenRoot).TrimEnd('\')
        if ($fullPath.Equals($fullForbiddenRoot, [StringComparison]::OrdinalIgnoreCase) -or
            $fullPath.StartsWith($fullForbiddenRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Offline-lab paths must stay outside Windows and Program Files: $fullPath"
        }
    }

    return $fullPath
}

function Remove-SafeStage {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedParent
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $verifiedPath = Assert-PathWithin -Path $Path -Parent $AllowedParent
    $null = Assert-NoReparsePointsInPath -Path $verifiedPath
    foreach ($item in Get-ChildItem -LiteralPath $verifiedPath -Recurse -Force -ErrorAction Stop) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing offline-lab cleanup because a reparse point exists: $($item.FullName)"
        }
    }

    Remove-Item -LiteralPath $verifiedPath -Recurse -Force -ErrorAction Stop
}

function Write-AtomicUtf8Json {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [object]$Value
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $parent = Split-Path -Parent $fullPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $null = Assert-NoReparsePointsInPath -Path $parent

    $tempPath = Join-Path $parent ('.{0}.{1}.tmp' -f [System.IO.Path]::GetFileName($fullPath), [Guid]::NewGuid().ToString('N'))
    $backupPath = Join-Path $parent ('.{0}.{1}.bak' -f [System.IO.Path]::GetFileName($fullPath), [Guid]::NewGuid().ToString('N'))
    $encoding = [System.Text.UTF8Encoding]::new($false)
    try {
        $json = ($Value | ConvertTo-Json -Depth 16) + [Environment]::NewLine
        [System.IO.File]::WriteAllText($tempPath, $json, $encoding)
        $stream = [System.IO.File]::Open($tempPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        try {
            $stream.Flush($true)
        }
        finally {
            $stream.Dispose()
        }

        if (Test-Path -LiteralPath $fullPath) {
            [System.IO.File]::Replace($tempPath, $fullPath, $backupPath, $true)
            [System.IO.File]::Delete($backupPath)
        }
        else {
            [System.IO.File]::Move($tempPath, $fullPath)
        }
    }
    finally {
        if (Test-Path -LiteralPath $tempPath) {
            Remove-Item -LiteralPath $tempPath -Force
        }
        if (Test-Path -LiteralPath $backupPath) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }
}

function Get-FileIdentity {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required offline-lab input is missing: $Path"
    }

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or $item.Length -lt 1) {
        throw "Offline-lab inputs must be nonempty regular files: $Path"
    }

    return [pscustomobject]@{
        relativePath = Get-NormalizedRelativePath -BasePath $root -Path $item.FullName
        size = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

function Assert-FileStillMatches {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [object]$Identity
    )

    $current = Get-FileIdentity -Path $Path
    if ($current.relativePath -ne $Identity.relativePath -or
        [int64]$current.size -ne [int64]$Identity.size -or
        -not ([string]$current.sha256).Equals([string]$Identity.sha256, [StringComparison]::Ordinal)) {
        throw "Offline-lab input changed during the run: $($Identity.relativePath)"
    }
}

function Assert-OfflineOnlySource {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Label
    )

    $text = [System.IO.File]::ReadAllText($Path)
    $forbiddenPatterns = @(
        '(?i)\b(?:explorer|dwm)\.exe\b',
        '(?i)#\s*include\s*[<"]windows\.h[>"]',
        '(?i)\b(?:CreateRemoteThread|WriteProcessMemory|SetWindowsHookEx[AW]?|TerminateProcess|RegSetValue(?:Ex)?[AW]?|ShellExecute(?:Ex)?[AW]?|WinExec|CreateProcess[AW]?|LoadLibrary(?:Ex)?[AW]?)\s*\(',
        '(?i)\b(?:system|_wsystem|popen|_popen)\s*\('
    )
    foreach ($pattern in $forbiddenPatterns) {
        if ([regex]::IsMatch($text, $pattern)) {
            throw "$Label contains a forbidden live-system primitive. The lifecycle lab must remain pure and offline."
        }
    }
}

function Get-CompileInputAggregate {
    param(
        [Parameter(Mandatory)] [string]$PortablePath,
        [Parameter(Mandatory)] [object[]]$Scopes
    )

    $portableFullPath = [System.IO.Path]::GetFullPath($PortablePath)
    $null = Assert-NoReparsePointsInPath -Path $portableFullPath
    $filesByRelativePath = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)

    foreach ($scope in $Scopes) {
        $scopePath = Join-Path $portableFullPath ([string]$scope.relativePath)
        $scopePath = Assert-PathWithin -Path $scopePath -Parent $portableFullPath
        if (-not (Test-Path -LiteralPath $scopePath)) {
            throw "Locked compiler input scope is missing: $($scope.relativePath)"
        }
        $null = Assert-NoReparsePointsInPath -Path $scopePath

        $scopeFiles = switch ([string]$scope.kind) {
            'tree' {
                $scopeItems = @(Get-ChildItem -LiteralPath $scopePath -Recurse -Force -ErrorAction Stop)
                foreach ($scopeItem in $scopeItems) {
                    if (($scopeItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        throw "Reparse points aren't allowed in compiler inputs: $($scopeItem.FullName)"
                    }
                }
                @($scopeItems | Where-Object { -not $_.PSIsContainer })
            }
            'file' { @(Get-Item -LiteralPath $scopePath -Force -ErrorAction Stop) }
            default { throw "Unknown compiler input scope kind: $($scope.kind)" }
        }

        foreach ($file in $scopeFiles) {
            if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points aren't allowed in compiler inputs: $($file.FullName)"
            }
            $relativePath = Get-NormalizedRelativePath -BasePath $portableFullPath -Path $file.FullName
            if (-not $filesByRelativePath.TryAdd($relativePath, $file.FullName)) {
                throw "Compiler input scopes overlap at: $relativePath"
            }
        }
    }

    $relativePaths = [string[]]$filesByRelativePath.Keys
    [Array]::Sort($relativePaths, [StringComparer]::Ordinal)
    $incrementalHash = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [uint64]$totalBytes = 0
    try {
        foreach ($relativePath in $relativePaths) {
            $filePath = $filesByRelativePath[$relativePath]
            $item = Get-Item -LiteralPath $filePath -Force
            $fileHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToUpperInvariant()
            $totalBytes += [uint64]$item.Length
            $record = "$relativePath`0$($item.Length)`0$fileHash`n"
            $incrementalHash.AppendData($utf8.GetBytes($record))
        }

        return [pscustomobject]@{
            algorithm = 'sha256-path-size-content-v1'
            fileCount = $relativePaths.Count
            bytes = $totalBytes
            sha256 = [Convert]::ToHexString($incrementalHash.GetHashAndReset())
        }
    }
    finally {
        $incrementalHash.Dispose()
    }
}

function Assert-CompileInputAggregate {
    param(
        [Parameter(Mandatory)] [object]$Actual,
        [Parameter(Mandatory)] [object]$Expected
    )

    if ($Actual.algorithm -ne $Expected.algorithm -or
        [uint64]$Actual.fileCount -ne [uint64]$Expected.fileCount -or
        [uint64]$Actual.bytes -ne [uint64]$Expected.bytes -or
        -not ([string]$Actual.sha256).Equals([string]$Expected.sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Portable compiler input aggregate doesn't match config/toolchain-lock.json."
    }
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$WorkingDirectory,
        [Parameter(Mandatory)] [string]$CompilerDirectory,
        [Parameter(Mandatory)] [int]$TimeoutSeconds
    )

    $resolvedFilePath = [System.IO.Path]::GetFullPath($FilePath)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $resolvedFilePath
    $startInfo.WorkingDirectory = [System.IO.Path]::GetFullPath($WorkingDirectory)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    foreach ($name in $environmentVariablesClearedForLab) {
        $null = $startInfo.Environment.Remove($name)
    }
    $targetRuntimeDirectory = Join-Path `
        (Split-Path -Parent ([System.IO.Path]::GetFullPath($CompilerDirectory))) `
        'x86_64-w64-mingw32\bin'
    if (-not (Test-Path -LiteralPath $targetRuntimeDirectory -PathType Container)) {
        throw "The locked AMD64 compiler runtime directory is missing: $targetRuntimeDirectory"
    }
    $null = Assert-NoReparsePointsInPath -Path $targetRuntimeDirectory
    $safePath = @(
        [System.IO.Path]::GetFullPath($CompilerDirectory),
        [System.IO.Path]::GetFullPath($targetRuntimeDirectory),
        (Join-Path $env:SystemRoot 'System32'),
        $env:SystemRoot
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
    $startInfo.Environment['PATH'] = $safePath -join [System.IO.Path]::PathSeparator

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Failed to start the offline-lab process: $resolvedFilePath"
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $completed = $process.WaitForExit($TimeoutSeconds * 1000)
        if (-not $completed) {
            $process.Kill($true)
        }
        $process.WaitForExit()
        [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($stdoutTask, $stderrTask))

        return [pscustomobject]@{
            filePath = $resolvedFilePath
            exitCode = if ($completed) { $process.ExitCode } else { -1 }
            timedOut = -not $completed
            stdout = $stdoutTask.Result
            stderr = $stderrTask.Result
        }
    }
    finally {
        $process.Dispose()
    }
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory)] [object]$Value,
        [Parameter(Mandatory)] [string[]]$Expected,
        [Parameter(Mandatory)] [string]$Label
    )

    $actual = @($Value.PSObject.Properties.Name | Sort-Object)
    $expectedSorted = @($Expected | Sort-Object)
    $difference = @(Compare-Object -ReferenceObject $expectedSorted -DifferenceObject $actual)
    if ($difference.Count -ne 0) {
        throw "$Label has missing or unexpected properties."
    }
}

function ConvertTo-NonNegativeInteger {
    param(
        [Parameter(Mandatory)] [object]$Value,
        [Parameter(Mandatory)] [string]$Label
    )

    if ($Value -isnot [byte] -and
        $Value -isnot [sbyte] -and
        $Value -isnot [int16] -and
        $Value -isnot [uint16] -and
        $Value -isnot [int32] -and
        $Value -isnot [uint32] -and
        $Value -isnot [int64] -and
        $Value -isnot [uint64]) {
        throw "$Label must be an integer."
    }
    if ([decimal]$Value -lt 0 -or [decimal]$Value -gt [long]::MaxValue) {
        throw "$Label must be a nonnegative Int64."
    }
    return [int64]$Value
}

function ConvertAndTest-HarnessPayload {
    param([Parameter(Mandatory)] [object]$Payload)

    Assert-ExactProperties -Value $Payload -Expected @(
        'protocolVersion',
        'passed',
        'summary',
        'scenarios',
        'errors'
    ) -Label 'Harness payload'
    if ((ConvertTo-NonNegativeInteger -Value $Payload.protocolVersion -Label 'protocolVersion') -ne 1) {
        throw 'Unsupported lifecycle harness protocolVersion.'
    }
    if ($Payload.passed -isnot [bool]) {
        throw 'Harness passed must be boolean.'
    }

    $scenarioValues = @($Payload.scenarios)
    if ($scenarioValues.Count -lt 1) {
        throw 'The lifecycle harness must report at least one scenario.'
    }

    $normalizedScenarios = [System.Collections.Generic.List[object]]::new()
    $scenarioIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $areas = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [int64]$passedCount = 0
    [int64]$failedCount = 0
    [int64]$retainedExplained = 0
    [int64]$retainedUnexplained = 0
    [int64]$doubleRelease = 0
    $boundedConcurrencyUsed = $false

    foreach ($scenario in $scenarioValues) {
        Assert-ExactProperties -Value $scenario -Expected @(
            'id',
            'area',
            'passed',
            'terminalState',
            'steps',
            'faults',
            'resourceEvents',
            'resourceAccounting',
            'boundedConcurrency',
            'detail'
        ) -Label 'Harness scenario'

        $scenarioId = [string]$scenario.id
        if ($scenarioId -notmatch '^[a-z0-9]+(?:[.-][a-z0-9]+)*$' -or -not $scenarioIds.Add($scenarioId)) {
            throw "Scenario id is invalid or duplicated: $scenarioId"
        }
        $area = [string]$scenario.area
        if ($area -notin @('git', 'ui-thread', 'dispatch', 'module')) {
            throw "Scenario area is invalid: $area"
        }
        $null = $areas.Add($area)
        if ($scenario.passed -isnot [bool]) {
            throw "Scenario passed must be boolean: $scenarioId"
        }
        $terminalState = [string]$scenario.terminalState
        if ([string]::IsNullOrWhiteSpace($terminalState)) {
            throw "Scenario terminalState is empty: $scenarioId"
        }

        $steps = @($scenario.steps)
        if ($steps.Count -lt 1 -or @($steps | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_) }).Count -ne 0) {
            throw "Scenario steps are invalid: $scenarioId"
        }
        $faults = @($scenario.faults)
        if (@($faults | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_) }).Count -ne 0) {
            throw "Scenario faults are invalid: $scenarioId"
        }
        if ($scenario.detail -isnot [string]) {
            throw "Scenario detail must be a string: $scenarioId"
        }

        $resourceEvents = @($scenario.resourceEvents)
        $normalizedResourceEvents =
            [System.Collections.Generic.List[object]]::new()
        $resourceStates =
            [System.Collections.Generic.Dictionary[string, object]]::new(
                [StringComparer]::Ordinal)
        [int64]$eventCreated = 0
        [int64]$eventReleased = 0
        [int64]$eventRetained = 0
        foreach ($resourceEvent in $resourceEvents) {
            Assert-ExactProperties -Value $resourceEvent -Expected @(
                'resourceId',
                'resourceKind',
                'action',
                'reasonCode'
            ) -Label "Scenario resourceEvent ($scenarioId)"
            if ($resourceEvent.resourceId -isnot [string] -or
                $resourceEvent.resourceKind -isnot [string] -or
                $resourceEvent.action -isnot [string] -or
                $resourceEvent.reasonCode -isnot [string]) {
                throw "Scenario resource event fields must be strings: $scenarioId"
            }

            $resourceId = [string]$resourceEvent.resourceId
            $resourceKind = [string]$resourceEvent.resourceKind
            $resourceAction = [string]$resourceEvent.action
            $reasonCode = [string]$resourceEvent.reasonCode
            $resourceIdPattern =
                '^' + [regex]::Escape($scenarioId) +
                '/resource-[1-9][0-9]*$'
            if ($resourceId -notmatch $resourceIdPattern) {
                throw "Scenario resource id is not stable or scoped: $scenarioId ($resourceId)"
            }
            if ($resourceKind -notmatch '^[a-z0-9]+(?:[.-][a-z0-9]+)*$') {
                throw "Scenario resource kind is invalid: $scenarioId ($resourceKind)"
            }
            if ($resourceAction -notin @('create', 'release', 'retain')) {
                throw "Scenario resource action is invalid: $scenarioId ($resourceAction)"
            }
            $knownReasonCodes = @(
                'none',
                'external-uncertainty',
                'retry-pending',
                'retry-exhausted',
                'owner-transfer',
                'protocol-failure',
                'cleanup-failure',
                'hook-removal-failure',
                'module-permanent',
                'capability-retained',
                'delegate-rejected',
                'rollback-failure',
                'resource-transferred'
            )
            if ($reasonCode -notin $knownReasonCodes) {
                throw "Scenario resource retain reason is unknown: $scenarioId ($reasonCode)"
            }
            if ($resourceAction -eq 'retain') {
                if ($reasonCode -eq 'none') {
                    throw "Scenario retained resource has no reason: $scenarioId ($resourceId)"
                }
            }
            elseif ($reasonCode -ne 'none') {
                throw "Scenario non-retained resource has a retain reason: $scenarioId ($resourceId)"
            }

            if ($resourceAction -eq 'create') {
                if ($resourceStates.ContainsKey($resourceId)) {
                    throw "Scenario has duplicate resource create: $scenarioId ($resourceId)"
                }
                $resourceStates.Add(
                    $resourceId,
                    [pscustomobject]@{
                        kind = $resourceKind
                        terminal = $false
                    })
                $eventCreated++
            }
            else {
                if (-not $resourceStates.ContainsKey($resourceId)) {
                    throw "Scenario has an unknown resource terminal event: $scenarioId ($resourceId)"
                }
                $state = $resourceStates[$resourceId]
                if ($state.kind -cne $resourceKind) {
                    throw "Scenario resource kind changed before terminal event: $scenarioId ($resourceId)"
                }
                if ($state.terminal) {
                    throw "Scenario has a duplicate resource terminal event: $scenarioId ($resourceId)"
                }
                $state.terminal = $true
                if ($resourceAction -eq 'release') {
                    $eventReleased++
                }
                else {
                    $eventRetained++
                }
            }

            $normalizedResourceEvents.Add([pscustomobject]@{
                resourceId = $resourceId
                resourceKind = $resourceKind
                action = $resourceAction
                reasonCode = $reasonCode
            })
        }
        foreach ($entry in $resourceStates.GetEnumerator()) {
            if (-not $entry.Value.terminal) {
                throw "Scenario has an unterminated resource: $scenarioId ($($entry.Key))"
            }
        }

        Assert-ExactProperties -Value $scenario.resourceAccounting -Expected @(
            'created',
            'released',
            'retained',
            'unexplained',
            'doubleRelease'
        ) -Label "Scenario resourceAccounting ($scenarioId)"
        $created = ConvertTo-NonNegativeInteger -Value $scenario.resourceAccounting.created -Label "$scenarioId.created"
        $released = ConvertTo-NonNegativeInteger -Value $scenario.resourceAccounting.released -Label "$scenarioId.released"
        $retained = ConvertTo-NonNegativeInteger -Value $scenario.resourceAccounting.retained -Label "$scenarioId.retained"
        $unexplained = ConvertTo-NonNegativeInteger -Value $scenario.resourceAccounting.unexplained -Label "$scenarioId.unexplained"
        $scenarioDoubleRelease = ConvertTo-NonNegativeInteger -Value $scenario.resourceAccounting.doubleRelease -Label "$scenarioId.doubleRelease"
        if ($created -ne $eventCreated -or
            $released -ne $eventReleased -or
            $retained -ne $eventRetained -or
            $unexplained -ne 0 -or
            $scenarioDoubleRelease -ne 0) {
            throw "Scenario resource accounting does not match its event ledger: $scenarioId"
        }
        if ($created -ne $released + $retained) {
            throw "Scenario event-derived resource accounting is inconsistent: $scenarioId"
        }

        Assert-ExactProperties -Value $scenario.boundedConcurrency -Expected @(
            'used',
            'barrierParticipants'
        ) -Label "Scenario boundedConcurrency ($scenarioId)"
        if ($scenario.boundedConcurrency.used -isnot [bool]) {
            throw "Scenario boundedConcurrency.used must be boolean: $scenarioId"
        }
        $barrierParticipants = ConvertTo-NonNegativeInteger -Value $scenario.boundedConcurrency.barrierParticipants -Label "$scenarioId.barrierParticipants"
        if ($scenario.boundedConcurrency.used) {
            if ($barrierParticipants -lt 2 -or $barrierParticipants -gt 64) {
                throw "A bounded concurrency scenario needs 2 to 64 barrier participants: $scenarioId"
            }
            $boundedConcurrencyUsed = $true
        }
        elseif ($barrierParticipants -ne 0) {
            throw "A sequential scenario must report zero barrier participants: $scenarioId"
        }

        if ($scenario.passed) {
            $passedCount++
        }
        else {
            $failedCount++
        }
        $retainedExplained += $retained - $unexplained
        $retainedUnexplained += $unexplained
        $doubleRelease += $scenarioDoubleRelease

        $normalizedScenarios.Add([pscustomobject]@{
            id = $scenarioId
            area = $area
            passed = [bool]$scenario.passed
            terminalState = $terminalState
            steps = [string[]]$steps
            faults = [string[]]$faults
            resourceEvents = $normalizedResourceEvents.ToArray()
            resourceAccounting = [pscustomobject]@{
                created = $created
                released = $released
                retained = $retained
                unexplained = $unexplained
                doubleRelease = $scenarioDoubleRelease
            }
            boundedConcurrency = [pscustomobject]@{
                used = [bool]$scenario.boundedConcurrency.used
                barrierParticipants = $barrierParticipants
            }
            detail = [string]$scenario.detail
        })
    }

    foreach ($requiredArea in @('git', 'ui-thread', 'dispatch', 'module')) {
        if (-not $areas.Contains($requiredArea)) {
            throw "The lifecycle harness has no $requiredArea scenario."
        }
    }
    if (-not $boundedConcurrencyUsed) {
        throw 'The lifecycle harness must include at least one bounded concurrency scenario.'
    }

    Assert-ExactProperties -Value $Payload.summary -Expected @(
        'scenarioCount',
        'passed',
        'failed',
        'retainedExplained',
        'retainedUnexplained',
        'doubleRelease'
    ) -Label 'Harness summary'
    $reportedSummary = [pscustomobject]@{
        scenarioCount = ConvertTo-NonNegativeInteger -Value $Payload.summary.scenarioCount -Label 'summary.scenarioCount'
        passed = ConvertTo-NonNegativeInteger -Value $Payload.summary.passed -Label 'summary.passed'
        failed = ConvertTo-NonNegativeInteger -Value $Payload.summary.failed -Label 'summary.failed'
        retainedExplained = ConvertTo-NonNegativeInteger -Value $Payload.summary.retainedExplained -Label 'summary.retainedExplained'
        retainedUnexplained = ConvertTo-NonNegativeInteger -Value $Payload.summary.retainedUnexplained -Label 'summary.retainedUnexplained'
        doubleRelease = ConvertTo-NonNegativeInteger -Value $Payload.summary.doubleRelease -Label 'summary.doubleRelease'
    }
    if ($reportedSummary.scenarioCount -ne $normalizedScenarios.Count -or
        $reportedSummary.passed -ne $passedCount -or
        $reportedSummary.failed -ne $failedCount -or
        $reportedSummary.retainedExplained -ne $retainedExplained -or
        $reportedSummary.retainedUnexplained -ne $retainedUnexplained -or
        $reportedSummary.doubleRelease -ne $doubleRelease) {
        throw 'Harness summary does not match the independently recomputed scenario accounting.'
    }

    $harnessErrors = @($Payload.errors)
    if (@($harnessErrors | Where-Object { $_ -isnot [string] }).Count -ne 0) {
        throw 'Harness errors must contain only strings.'
    }
    $computedPassed = $failedCount -eq 0 -and $retainedUnexplained -eq 0 -and $doubleRelease -eq 0 -and $harnessErrors.Count -eq 0
    if ([bool]$Payload.passed -ne $computedPassed) {
        throw 'Harness passed does not match scenario accounting and errors.'
    }

    return [pscustomobject]@{
        passed = $computedPassed
        summary = $reportedSummary
        scenarios = $normalizedScenarios.ToArray()
        errors = [string[]]$harnessErrors
    }
}

function New-RunnerFailureReport {
    param([Parameter(Mandatory)] [string[]]$Errors)

    $detail = if ($Errors.Count -eq 0) { 'Offline lifecycle runner failed.' } else { $Errors -join ' | ' }
    return [pscustomobject]@{
        passed = $false
        summary = [pscustomobject]@{
            scenarioCount = 1
            passed = 0
            failed = 1
            retainedExplained = 0
            retainedUnexplained = 0
            doubleRelease = 0
        }
        scenarios = @(
            [pscustomobject]@{
                id = 'runner.preflight'
                area = 'dispatch'
                passed = $false
                terminalState = 'runner-failed'
                steps = @('offline-runner-preflight')
                faults = @('runner-failure')
                resourceEvents = @()
                resourceAccounting = [pscustomobject]@{
                    created = 0
                    released = 0
                    retained = 0
                    unexplained = 0
                    doubleRelease = 0
                }
                boundedConcurrency = [pscustomobject]@{
                    used = $false
                    barrierParticipants = 0
                }
                detail = $detail
            }
        )
        errors = [string[]]$Errors
    }
}

function New-HarnessValidatorProbe {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$ResourceEvents,
        [Parameter(Mandatory)] [int64]$Created,
        [Parameter(Mandatory)] [int64]$Released,
        [Parameter(Mandatory)] [int64]$Retained
    )

    return [pscustomobject]@{
        protocolVersion = 1
        passed = $true
        summary = [pscustomobject]@{
            scenarioCount = 1
            passed = 1
            failed = 0
            retainedExplained = $Retained
            retainedUnexplained = 0
            doubleRelease = 0
        }
        scenarios = @(
            [pscustomobject]@{
                id = 'git.validator-probe'
                area = 'git'
                passed = $true
                terminalState = 'probe'
                steps = @('validate-negative-case')
                faults = @()
                resourceEvents = $ResourceEvents
                resourceAccounting = [pscustomobject]@{
                    created = $Created
                    released = $Released
                    retained = $Retained
                    unexplained = 0
                    doubleRelease = 0
                }
                boundedConcurrency = [pscustomobject]@{
                    used = $false
                    barrierParticipants = 0
                }
                detail = 'runner validator negative-case probe'
            }
        )
        errors = @()
    }
}

function Assert-HarnessValidatorRejects {
    param(
        [Parameter(Mandatory)] [object]$Payload,
        [Parameter(Mandatory)] [string]$Label
    )

    $rejected = $false
    try {
        $null = ConvertAndTest-HarnessPayload -Payload $Payload
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Lifecycle harness validator accepted counterfeit evidence: $Label"
    }
}

function Assert-HarnessValidatorNegativeCases {
    $totalsOnly = New-HarnessValidatorProbe `
        -ResourceEvents @() `
        -Created 1 `
        -Released 1 `
        -Retained 0
    Assert-HarnessValidatorRejects `
        -Payload $totalsOnly `
        -Label 'internally-balanced totals without owner events'

    $missingReason = New-HarnessValidatorProbe `
        -ResourceEvents @(
            [pscustomobject]@{
                resourceId = 'git.validator-probe/resource-1'
                resourceKind = 'probe-owner'
                action = 'create'
                reasonCode = 'none'
            },
            [pscustomobject]@{
                resourceId = 'git.validator-probe/resource-1'
                resourceKind = 'probe-owner'
                action = 'retain'
                reasonCode = 'none'
            }
        ) `
        -Created 1 `
        -Released 0 `
        -Retained 1
    Assert-HarnessValidatorRejects `
        -Payload $missingReason `
        -Label 'retained owner without a reason'

    $unknownReason = New-HarnessValidatorProbe `
        -ResourceEvents @(
            [pscustomobject]@{
                resourceId = 'git.validator-probe/resource-1'
                resourceKind = 'probe-owner'
                action = 'create'
                reasonCode = 'none'
            },
            [pscustomobject]@{
                resourceId = 'git.validator-probe/resource-1'
                resourceKind = 'probe-owner'
                action = 'retain'
                reasonCode = 'invented-reason'
            }
        ) `
        -Created 1 `
        -Released 0 `
        -Retained 1
    Assert-HarnessValidatorRejects `
        -Payload $unknownReason `
        -Label 'retained owner with an unknown reason'
}

Assert-HarnessValidatorNegativeCases

$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$artifactRootFullPath = [System.IO.Path]::GetFullPath($artifactRoot)
$null = Assert-NonSystemPath -Path $artifactRootFullPath
$null = Assert-PathWithin -Path $outputFullPath -Parent $artifactRootFullPath
if ([System.IO.Path]::GetExtension($outputFullPath) -ne '.json') {
    throw 'OutputPath must name a JSON file under artifacts/lifecycle-fault-lab.'
}

$sourceIdentity = [pscustomobject]@{
    m1Source = Get-FileIdentity -Path $m1SourcePath
    protocolHeader = Get-FileIdentity -Path $protocolHeaderPath
    labSource = Get-FileIdentity -Path $labSourcePath
    runnerScript = Get-FileIdentity -Path $runnerScriptPath
    receiptSchema = Get-FileIdentity -Path $receiptSchemaPath
    testProject = Get-FileIdentity -Path $testProjectPath
}
$toolchainLockIdentity = Get-FileIdentity -Path $toolchainLockPath
Assert-OfflineOnlySource -Path $protocolHeaderPath -Label 'Protocol header'
Assert-OfflineOnlySource -Path $labSourcePath -Label 'Lifecycle harness'

$toolchainLock = Get-Content -LiteralPath $toolchainLockPath -Raw | ConvertFrom-Json
if ($toolchainLock.schemaVersion -ne 2 -or
    $toolchainLock.compileInputTree.algorithm -ne 'sha256-path-size-content-v1') {
    throw 'Unsupported or incomplete toolchain lock schema.'
}

$toolCacheFullPath = Assert-NonSystemPath -Path $ToolCache
$null = Assert-NoReparsePointsInPath -Path $toolCacheFullPath
$portablePath = Join-Path $toolCacheFullPath 'portable'
$portableCompiler = Join-Path $portablePath 'Compiler\bin\clang++.exe'
$portableIniPath = Join-Path $portablePath 'windhawk.ini'
$compilerDirectory = Split-Path -Parent $portableCompiler
$toolchainIdentity = $null
$labReport = $null
$clangVersion = $null
$runErrors = [System.Collections.Generic.List[string]]::new()
$mutexAcquired = $false
$toolchainMutex = $null

try {
    if (-not (Test-Path -LiteralPath $portablePath -PathType Container) -or
        -not (Test-Path -LiteralPath $portableCompiler -PathType Leaf) -or
        -not (Test-Path -LiteralPath $portableIniPath -PathType Leaf)) {
        throw 'The complete locked portable Windhawk toolchain is not pre-provisioned.'
    }
    $null = Assert-NoReparsePointsInPath -Path $portablePath
    $portableIni = [System.IO.File]::ReadAllText($portableIniPath)
    if (-not $portableIni.Contains('Portable=1') -or
        -not $portableIni.Contains("EnginePath=Engine\$($toolchainLock.windhawkVersion)") -or
        -not $portableIni.Contains('CompilerPath=Compiler')) {
        throw 'The pre-provisioned portable Windhawk configuration failed validation.'
    }

    $mutexNameBytes = [System.Text.Encoding]::UTF8.GetBytes($toolCacheFullPath.ToUpperInvariant())
    $mutexSuffix = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($mutexNameBytes)).Substring(0, 24)
    $toolchainMutex = [System.Threading.Mutex]::new($false, "Local\JARVIS2-NativeBuild-$mutexSuffix")
    try {
        $mutexAcquired = $toolchainMutex.WaitOne([TimeSpan]::FromSeconds(10))
    }
    catch [System.Threading.AbandonedMutexException] {
        $mutexAcquired = $true
    }
    if (-not $mutexAcquired) {
        throw 'Another JARVIS2 native build is using the shared locked toolchain cache.'
    }

    $compileInputBefore = Get-CompileInputAggregate -PortablePath $portablePath -Scopes $toolchainLock.compileInputTree.scopes
    $compilerItem = Get-Item -LiteralPath $portableCompiler -Force
    $toolchainIdentity = [pscustomobject]@{
        windhawkVersion = [string]$toolchainLock.windhawkVersion
        clang = [pscustomobject]@{
            fileName = $compilerItem.Name
            size = $compilerItem.Length
            sha256 = (Get-FileHash -LiteralPath $portableCompiler -Algorithm SHA256).Hash.ToUpperInvariant()
        }
        toolchainLock = $toolchainLockIdentity
        compileInputTree = $compileInputBefore
    }
    Assert-CompileInputAggregate -Actual $compileInputBefore -Expected $toolchainLock.compileInputTree

    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
    $null = Assert-NoReparsePointsInPath -Path $stagingRoot
    $stageDirectory = Assert-PathWithin -Path $stageDirectory -Parent $stagingRoot
    New-Item -ItemType Directory -Path $stageDirectory | Out-Null
    $null = Assert-NoReparsePointsInPath -Path $stageDirectory

    $snapshotRoot = Join-Path $stageDirectory 'snapshot'
    $snapshotMods = Join-Path $snapshotRoot 'mods'
    $snapshotTests = Join-Path $snapshotRoot 'tests\native'
    New-Item -ItemType Directory -Path $snapshotMods -Force | Out-Null
    New-Item -ItemType Directory -Path $snapshotTests -Force | Out-Null
    $snapshotHeaderPath = Join-Path $snapshotMods 'jarvis-resource-protocol.hpp'
    $snapshotLabSourcePath = Join-Path $snapshotTests 'jarvis_lifecycle_harness.cpp'
    Copy-Item -LiteralPath $protocolHeaderPath -Destination $snapshotHeaderPath
    Copy-Item -LiteralPath $labSourcePath -Destination $snapshotLabSourcePath
    if ((Get-FileHash -LiteralPath $snapshotHeaderPath -Algorithm SHA256).Hash -ne $sourceIdentity.protocolHeader.sha256 -or
        (Get-FileHash -LiteralPath $snapshotLabSourcePath -Algorithm SHA256).Hash -ne $sourceIdentity.labSource.sha256) {
        throw 'The immutable offline-lab source snapshot failed SHA-256 verification.'
    }

    $clangProbe = Invoke-CapturedProcess -FilePath $portableCompiler -Arguments @('--version') -WorkingDirectory $stageDirectory -CompilerDirectory $compilerDirectory -TimeoutSeconds $ProcessTimeoutSeconds
    if ($clangProbe.timedOut -or $clangProbe.exitCode -ne 0 -or
        (($clangProbe.stdout -split '\r?\n' | Select-Object -First 1).Trim() -notmatch '(?i)\bclang version\b')) {
        throw 'The locked portable Clang compiler failed its identity probe.'
    }
    $clangVersion = ($clangProbe.stdout -split '\r?\n' | Select-Object -First 1).Trim()

    $labExecutablePath = Join-Path $stageDirectory 'jarvis-lifecycle-harness.exe'
    $compileArguments = @(
        '-std=c++20',
        '-O2',
        '-Wall',
        '-Wextra',
        '-Wpedantic',
        '-Werror',
        '-Wconversion',
        '-Wsign-conversion',
        '-Wshadow',
        '-Wformat=2',
        '-fno-color-diagnostics',
        '-pthread',
        '-static',
        '-target',
        'x86_64-w64-mingw32',
        '-I',
        $snapshotRoot,
        '-I',
        $snapshotMods,
        '-x',
        'c++',
        $snapshotLabSourcePath,
        '-o',
        $labExecutablePath
    )
    $compileResult = Invoke-CapturedProcess -FilePath $portableCompiler -Arguments $compileArguments -WorkingDirectory $stageDirectory -CompilerDirectory $compilerDirectory -TimeoutSeconds $ProcessTimeoutSeconds
    $compileText = $compileResult.stdout + [Environment]::NewLine + $compileResult.stderr
    if ($compileResult.timedOut -or $compileResult.exitCode -ne 0) {
        throw "Offline lifecycle harness compilation failed with exit code $($compileResult.exitCode): $($compileResult.stderr.Trim())"
    }
    if ($compileText -match '(?im)\b(?:warning|error)(?:\s+[A-Z]+\d+)?\s*:') {
        throw 'Offline lifecycle harness compilation emitted a warning or error diagnostic.'
    }
    if (-not (Test-Path -LiteralPath $labExecutablePath -PathType Leaf) -or
        (Get-Item -LiteralPath $labExecutablePath).Length -lt 1) {
        throw 'Offline lifecycle harness compilation produced no executable.'
    }

    $harnessResult = Invoke-CapturedProcess -FilePath $labExecutablePath -Arguments @('--json') -WorkingDirectory $stageDirectory -CompilerDirectory $compilerDirectory -TimeoutSeconds $ProcessTimeoutSeconds
    if ($harnessResult.timedOut) {
        throw 'Offline lifecycle harness exceeded its bounded execution timeout.'
    }
    if (-not [string]::IsNullOrWhiteSpace($harnessResult.stderr)) {
        throw "Offline lifecycle harness wrote to stderr: $($harnessResult.stderr.Trim())"
    }
    if ($harnessResult.exitCode -ne 0 -and
        [string]::IsNullOrWhiteSpace($harnessResult.stdout)) {
        throw "Offline lifecycle harness exited $($harnessResult.exitCode) without a JSON payload."
    }
    if ([string]::IsNullOrWhiteSpace($harnessResult.stdout)) {
        throw 'Offline lifecycle harness returned no JSON payload.'
    }

    try {
        $harnessPayload = $harnessResult.stdout | ConvertFrom-Json
    }
    catch {
        throw "Offline lifecycle harness returned invalid JSON: $($_.Exception.Message)"
    }
    $labReport = ConvertAndTest-HarnessPayload -Payload $harnessPayload
    if ($harnessResult.exitCode -ne 0 -and $labReport.passed) {
        throw "Offline lifecycle harness exited $($harnessResult.exitCode) despite reporting success."
    }
    if ($harnessResult.exitCode -eq 0 -and -not $labReport.passed) {
        throw 'Offline lifecycle harness exited zero despite reporting failed scenarios.'
    }

    $compileInputAfter = Get-CompileInputAggregate -PortablePath $portablePath -Scopes $toolchainLock.compileInputTree.scopes
    Assert-CompileInputAggregate -Actual $compileInputAfter -Expected $toolchainLock.compileInputTree
    if ($compileInputAfter.sha256 -ne $compileInputBefore.sha256 -or
        [uint64]$compileInputAfter.fileCount -ne [uint64]$compileInputBefore.fileCount -or
        [uint64]$compileInputAfter.bytes -ne [uint64]$compileInputBefore.bytes) {
        throw 'The locked compiler input tree changed during the offline lifecycle run.'
    }

    Assert-FileStillMatches -Path $m1SourcePath -Identity $sourceIdentity.m1Source
    Assert-FileStillMatches -Path $protocolHeaderPath -Identity $sourceIdentity.protocolHeader
    Assert-FileStillMatches -Path $labSourcePath -Identity $sourceIdentity.labSource
    Assert-FileStillMatches -Path $runnerScriptPath -Identity $sourceIdentity.runnerScript
    Assert-FileStillMatches -Path $receiptSchemaPath -Identity $sourceIdentity.receiptSchema
    Assert-FileStillMatches -Path $testProjectPath -Identity $sourceIdentity.testProject
    Assert-FileStillMatches -Path $toolchainLockPath -Identity $toolchainLockIdentity

    if (-not $labReport.passed) {
        foreach ($errorMessage in $labReport.errors) {
            $runErrors.Add($errorMessage)
        }
        if ($runErrors.Count -eq 0) {
            $runErrors.Add('One or more offline lifecycle scenarios failed.')
        }
    }
}
catch {
    $runErrors.Add($_.Exception.Message)
    $labReport = $null
}
finally {
    if ($mutexAcquired -and $null -ne $toolchainMutex) {
        $toolchainMutex.ReleaseMutex()
    }
    if ($null -ne $toolchainMutex) {
        $toolchainMutex.Dispose()
    }
    try {
        Remove-SafeStage -Path $stageDirectory -AllowedParent $stagingRoot
    }
    catch {
        $runErrors.Add("Offline-lab staging cleanup failed: $($_.Exception.Message)")
        $labReport = $null
    }
}

if ($null -eq $toolchainIdentity) {
    throw "No lifecycle receipt was written because the locked toolchain identity couldn't be established. $($runErrors -join ' | ')"
}
if ($null -eq $labReport) {
    $labReport = New-RunnerFailureReport -Errors $runErrors.ToArray()
}

$overallPassed = $labReport.passed -and $runErrors.Count -eq 0
$receiptErrors = if ($overallPassed) {
    @()
}
elseif ($runErrors.Count -gt 0) {
    [string[]]$runErrors.ToArray()
}
else {
    [string[]]$labReport.errors
}

$receipt = [pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvis2-offline-lifecycle-fault-lab'
    runId = $runId
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    result = if ($overallPassed) { 'passed' } else { 'failed' }
    offlineEvidenceReady = $overallPassed
    releaseReady = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    sourceIdentity = $sourceIdentity
    toolchainIdentity = $toolchainIdentity
    environment = [pscustomobject]@{
        framework = if ($null -ne $clangVersion) { "C++20; $clangVersion" } else { 'C++20; locked portable Clang' }
        osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    }
    summary = $labReport.summary
    scenarios = $labReport.scenarios
    errors = @($receiptErrors)
}

Write-AtomicUtf8Json -Path $outputFullPath -Value $receipt

if (-not $overallPassed) {
    throw "Offline lifecycle fault lab failed; failure receipt: $outputFullPath"
}

[pscustomobject]@{
    result = 'passed'
    offlineEvidenceReady = $true
    releaseReady = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    receiptPath = $outputFullPath
    runId = $runId
}
