[CmdletBinding()]
param(
    [ValidateSet(
        'Inspect',
        'UpdateDisabledInstallation',
        'StartDisabledHost',
        'EnableOnce',
        'Observe',
        'Recover'
    )]
    [string]$Action = 'Inspect',

    [string]$SessionPlanPath,

    [ValidateRange(0, [int]::MaxValue)]
    [int]$ExpectedExplorerProcessId = 0,

    [ValidateRange(3, 60)]
    [int]$ObservationSeconds = 10,

    [ValidateRange(1, 100)]
    [double]$MaxSingleCoreCpuPercent = 25,

    [string]$OutputPath,

    [switch]$ConfirmUpdateDisabledInstallation,

    [switch]$RetireStaleRecoveryLease,

    [switch]$ConfirmStartDisabledHost,

    [switch]$ConfirmEnableOnce,

    [switch]$ConfirmRecover
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$moduleId = 'jarvis-taskbar-icon-size'
$windhawkModId = "local@$moduleId"
$serviceName = 'Windhawk'
$libraryFileName = 'jarvis-taskbar-icon-size_0.2.0_jarvis2.dll'
$controllerRelativePath = 'scripts/Invoke-M2ControlledLiveValidation.ps1'
$expectedSourceSha256 =
    '9F955ADD6B9CE1E087F8DCB97093C392B17845BEF2DBD31688A17B7D1B9B0C31'
$expectedDllSha256 =
    '747F17F4DF5974222218DE716661416A934F86CF10DC15BEA50FB18E72327F5B'
$oldInstalledSourceSha256 =
    '4A0278E2BC1CC81D616AC885F87BB51CE26DD044E4F44DDB8341E0C6D79087C4'
$oldInstalledDllSha256 =
    'DBABD5BEDAB2A2CF1BA0592A1742C2E31FF91E2F9CD4EEF440E0E2A82AF8C490'
$canonicalSourcePath =
    Join-Path $root 'mods\jarvis-taskbar-icon-size.wh.cpp'
$canonicalDllPath = Join-Path $root (
    'artifacts\native\runs\20260726T183340920Z-efe8b4b3\modules\' +
    'jarvis-taskbar-icon-size\jarvis-taskbar-icon-size-x64.dll'
)
$supervisorProject =
    Join-Path $root 'src\Jarvis.Supervisor\Jarvis.Supervisor.csproj'
$planSchemaPath =
    Join-Path $root 'config\m2-validation-session-plan.schema.json'
$recoveryTerminalScriptPath =
    Join-Path $root 'scripts\Open-M2RecoveryTerminal.ps1'
$stateRoot = Join-Path $env:LOCALAPPDATA 'JARVIS2'
$killSwitchPath = Join-Path $stateRoot 'disabled.flag'
$permitPath = Join-Path $stateRoot 'active-module.txt'
$leasePath = Join-Path $stateRoot 'Recovery\m2-recovery-terminal.json'
$windhawkDataRoot = Join-Path $env:ProgramData 'Windhawk'
$installedSourcePath =
    Join-Path $windhawkDataRoot "ModsSource\$windhawkModId.wh.cpp"
$installedDllPath =
    Join-Path $windhawkDataRoot "Engine\Mods\64\$libraryFileName"
$modsRegistryRoot =
    'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Windhawk\Engine\Mods'
$modRegistryPath = Join-Path $modsRegistryRoot $windhawkModId
$settingsRegistryPath = Join-Path $modRegistryPath 'Settings'
$allowedPlanRoot =
    Join-Path $root 'artifacts\m2-validation-session-plans\runs'
$allowedOutputRoot =
    Join-Path $root 'artifacts\m2-controlled-live\runs'
$disabledInstallBackupRoot =
    Join-Path $root 'artifacts\m2-controlled-live\disabled-install-backups'
$armCommand = (
    'dotnet run --project .\src\Jarvis.Supervisor ' +
    '--configuration Release --no-build -- arm-kill-switch'
)
$clearCommand = (
    'dotnet run --project .\src\Jarvis.Supervisor ' +
    '--configuration Release --no-build -- clear-kill-switch ' +
    '--module jarvis-taskbar-icon-size --confirm'
)
$script:mutationPerformed = $false
$script:stopRequired = $false

function Assert-Condition {
    param(
        [Parameter(Mandatory)] [bool]$Condition,
        [Parameter(Mandatory)] [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Administrator {
    $principal = [Security.Principal.WindowsPrincipal]::new(
        [Security.Principal.WindowsIdentity]::GetCurrent())
    Assert-Condition `
        -Condition $principal.IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator) `
        -Message 'Administrator elevation is required for this action.'
}

function Get-Sha256 {
    param([Parameter(Mandatory)] [string]$LiteralPath)

    return (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash
}

function Resolve-RestrictedPath {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedRoot,
        [switch]$MustExist
    )

    $candidate = if ([IO.Path]::IsPathRooted($Path)) {
        [IO.Path]::GetFullPath($Path)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $root $Path))
    }
    $allowed = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\')
    Assert-Condition `
        -Condition $candidate.StartsWith(
            $allowed + '\',
            [StringComparison]::OrdinalIgnoreCase) `
        -Message "Path must stay below $allowed."
    if ($MustExist) {
        Assert-Condition `
            -Condition (Test-Path -LiteralPath $candidate -PathType Leaf) `
            -Message "Required file is missing: $candidate"
    }
    $null = Assert-NoReparsePointsInPath $candidate
    return $candidate
}

function Invoke-Supervisor {
    param([Parameter(Mandatory)] [string[]]$Arguments)

    $output = @(
        & dotnet run `
            --project $supervisorProject `
            --configuration Release `
            --no-build `
            -- @Arguments 2>&1 |
            ForEach-Object { [string]$_ }
    )
    $exitCode = $LASTEXITCODE
    $json = $output -join [Environment]::NewLine
    if ($exitCode -ne 0) {
        throw (
            "Supervisor command failed with exit code $exitCode. " +
            $json
        )
    }
    return $json | ConvertFrom-Json -Depth 100
}

function Get-RecoveryLeaseProbe {
    $output = @(
        & dotnet run `
            --project $supervisorProject `
            --configuration Release `
            --no-build `
            -- inspect-recovery-terminal `
            --module $moduleId 2>&1 |
            ForEach-Object { [string]$_ }
    )
    $exitCode = $LASTEXITCODE
    $json = $output -join [Environment]::NewLine
    $report = $null
    try {
        $report = $json | ConvertFrom-Json -Depth 100
    }
    catch {
        throw (
            'Supervisor recovery-lease probe did not return JSON. ' +
            $json
        )
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Report = $report
    }
}

function Assert-LockedPlanDryRun {
    param(
        [Parameter(Mandatory)] [object]$PlanIdentity
    )

    $output = @(
        & pwsh `
            -NoLogo `
            -NoProfile `
            -File $recoveryTerminalScriptPath `
            -SessionPlanPath $PlanIdentity.PlanPath 2>&1 |
            ForEach-Object { [string]$_ }
    )
    $exitCode = $LASTEXITCODE
    $json = $output -join [Environment]::NewLine
    Assert-Condition `
        -Condition ($exitCode -eq 0) `
        -Message (
            'The plan-bound locked recovery dry run failed. ' + $json
        )
    $dryRun = $json | ConvertFrom-Json -Depth 100
    Assert-Condition `
        -Condition (
            [string]$dryRun.result -ceq 'passed' -and
            [string]$dryRun.sessionPlanRunId -ceq
                [string]$PlanIdentity.Plan.runId -and
            [string]$dryRun.planSha256 -ieq
                [string]$PlanIdentity.PlanSha256 -and
            -not [bool]$dryRun.launchPerformed -and
            -not [bool]$dryRun.terminalAvailable -and
            -not [bool]$dryRun.activationPermitted -and
            -not [bool]$dryRun.mutationPerformed -and
            -not [bool]$dryRun.canExecuteNow
        ) `
        -Message 'The locked recovery dry run crossed its inert boundary.'
    return $dryRun
}

function Get-CompatibilityReport {
    $report = Invoke-Supervisor -Arguments @('inspect')
    Assert-Condition `
        -Condition ([bool]$report.compatible) `
        -Message 'Supervisor compatibility inspection failed.'
    Assert-Condition `
        -Condition ([bool]$report.explorerRuntime.inspectionSucceeded) `
        -Message 'Supervisor could not inspect the desktop Explorer runtime.'
    Assert-Condition `
        -Condition (
            @($report.checks).Count -eq 23 -and
            @($report.checks | Where-Object { -not $_.passed }).Count -eq 0
        ) `
        -Message 'Supervisor compatibility report is not the expected 23/23.'
    return $report
}

function Get-ServiceSnapshot {
    $service =
        Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    Assert-Condition `
        -Condition ($null -ne $service) `
        -Message 'Windhawk service was not found.'
    return [pscustomobject]@{
        State = [string]$service.State
        StartMode = [string]$service.StartMode
        ProcessId = [int]$service.ProcessId
    }
}

function Get-CanonicalModConfig {
    param(
        [string]$RequiredSourceSha256 = $script:expectedSourceSha256,
        [string]$RequiredDllSha256 = $script:expectedDllSha256
    )

    Assert-Condition `
        -Condition (Test-Path -LiteralPath $modsRegistryRoot) `
        -Message 'Windhawk mod registry root is missing.'
    $installedModIds = @(
        Get-ChildItem -LiteralPath $modsRegistryRoot -ErrorAction Stop |
            Select-Object -ExpandProperty PSChildName
    )
    Assert-Condition `
        -Condition (
            $installedModIds.Count -eq 1 -and
            $installedModIds[0] -ceq $windhawkModId
        ) `
        -Message (
            'Exactly local@jarvis-taskbar-icon-size must be configured. ' +
            'Observed: ' + ($installedModIds -join ', ')
        )

    $config =
        Get-ItemProperty -LiteralPath $modRegistryPath -ErrorAction Stop
    $settings =
        Get-ItemProperty -LiteralPath $settingsRegistryPath -ErrorAction Stop
    Assert-Condition `
        -Condition ([string]$config.LibraryFileName -ceq $libraryFileName) `
        -Message 'M2 library file name drifted.'
    Assert-Condition `
        -Condition ([string]$config.Include -ceq '%SystemRoot%\explorer.exe') `
        -Message 'M2 include target drifted.'
    Assert-Condition `
        -Condition ([string]$config.Exclude -ceq '') `
        -Message 'M2 exclude target drifted.'
    Assert-Condition `
        -Condition ([string]$config.IncludeCustom -ceq '') `
        -Message 'M2 custom include target drifted.'
    Assert-Condition `
        -Condition ([string]$config.ExcludeCustom -ceq '') `
        -Message 'M2 custom exclude target drifted.'
    Assert-Condition `
        -Condition ([int]$config.IncludeExcludeCustomOnly -eq 0) `
        -Message 'M2 custom-only targeting drifted.'
    Assert-Condition `
        -Condition ([int]$config.PatternsMatchCriticalSystemProcesses -eq 0) `
        -Message 'M2 critical-process targeting must remain disabled.'
    Assert-Condition `
        -Condition ([string]$config.Architecture -ceq 'amd64') `
        -Message 'M2 architecture drifted.'
    Assert-Condition `
        -Condition ([int]$settings.Enabled -eq 1) `
        -Message 'M2 internal Enabled setting drifted.'
    Assert-Condition `
        -Condition ([int]$settings.IconSize -eq 24) `
        -Message 'M2 IconSize drifted.'

    foreach ($file in @(
        [pscustomobject]@{
            Path = $installedSourcePath
            Hash = $RequiredSourceSha256
            Label = 'installed M2 source'
        },
        [pscustomobject]@{
            Path = $installedDllPath
            Hash = $RequiredDllSha256
            Label = 'installed M2 DLL'
        }
    )) {
        $null = Assert-NoReparsePointsInPath $file.Path
        Assert-Condition `
            -Condition (Test-Path -LiteralPath $file.Path -PathType Leaf) `
            -Message "$($file.Label) is missing: $($file.Path)"
        Assert-Condition `
            -Condition ((Get-Sha256 $file.Path) -ceq $file.Hash) `
            -Message "$($file.Label) hash drifted."
    }

    return [pscustomobject]@{
        Disabled = [int]$config.Disabled
        LibraryFileName = [string]$config.LibraryFileName
        Include = [string]$config.Include
        Architecture = [string]$config.Architecture
        IconSize = [int]$settings.IconSize
    }
}

function Get-ProcessMappings {
    param([switch]$IncludeWindhawkRuntime)

    $mappings = [System.Collections.Generic.List[object]]::new()
    foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
        try {
            foreach ($module in $process.Modules) {
                $path = [string]$module.FileName
                $moduleName = [string]$module.ModuleName
                $isJarvis =
                    $moduleName -match '(?i)^jarvis-' -or
                    $path -match '(?i)[\\/]jarvis-[^\\/]+\.dll$'
                $isWindhawk =
                    $moduleName -match '(?i)^windhawk\.dll$' -or
                    $path -match '(?i)[\\/]Windhawk[\\/]'
                if ($isJarvis -or ($IncludeWindhawkRuntime -and $isWindhawk)) {
                    $mappings.Add([pscustomobject]@{
                        process = [string]$process.ProcessName
                        processId = [int]$process.Id
                        module = $moduleName
                        path = $path
                        isJarvis = [bool]$isJarvis
                    })
                }
            }
        }
        catch {
            # The exact desktop Explorer mapping is checked separately and may
            # never be silently skipped. Protected unrelated processes are not
            # evidence that a Jarvis module was mapped.
        }
    }
    return @($mappings)
}

function Get-DesktopExplorerMappings {
    param([Parameter(Mandatory)] [int]$ExplorerProcessId)

    $process = Get-Process -Id $ExplorerProcessId -ErrorAction Stop
    Assert-Condition `
        -Condition ([string]$process.ProcessName -ieq 'explorer') `
        -Message "PID $ExplorerProcessId is not Explorer."
    try {
        return @(
            foreach ($module in $process.Modules) {
                $path = [string]$module.FileName
                $moduleName = [string]$module.ModuleName
                if (
                    $moduleName -match '(?i)^jarvis-' -or
                    $path -match '(?i)[\\/]jarvis-[^\\/]+\.dll$'
                ) {
                    [pscustomobject]@{
                        process = [string]$process.ProcessName
                        processId = [int]$process.Id
                        module = $moduleName
                        path = $path
                        isJarvis = $true
                    }
                }
            }
        )
    }
    catch {
        throw "Desktop Explorer module enumeration failed: $($_.Exception.Message)"
    }
}

function Assert-ExplorerIdentity {
    param(
        [Parameter(Mandatory)] [object]$Compatibility,
        [Parameter(Mandatory)] [int]$ExpectedProcessId
    )

    Assert-Condition `
        -Condition ($ExpectedProcessId -gt 0) `
        -Message 'ExpectedExplorerProcessId must be supplied for this action.'
    Assert-Condition `
        -Condition (
            [int]$Compatibility.explorerRuntime.processId -eq
                $ExpectedProcessId
        ) `
        -Message (
            "Explorer PID drifted. Expected $ExpectedProcessId, observed " +
            "$($Compatibility.explorerRuntime.processId)."
        )
}

function Assert-TargetMappingState {
    param(
        [Parameter(Mandatory)] [int]$ExplorerProcessId,
        [Parameter(Mandatory)] [int]$ExpectedCount
    )

    $explorerMappings =
        @(Get-DesktopExplorerMappings -ExplorerProcessId $ExplorerProcessId)
    $targetMappings = @(
        $explorerMappings |
            Where-Object {
                [string]$_.path -ieq $installedDllPath -and
                [string]$_.module -ieq $libraryFileName
            }
    )
    $unexpectedJarvis = @(
        Get-ProcessMappings |
            Where-Object {
                -not (
                    [int]$_.processId -eq $ExplorerProcessId -and
                    [string]$_.path -ieq $installedDllPath
                )
            }
    )
    Assert-Condition `
        -Condition ($targetMappings.Count -eq $ExpectedCount) `
        -Message (
            "Expected $ExpectedCount canonical M2 mapping(s) in desktop " +
            "Explorer, observed $($targetMappings.Count)."
        )
    Assert-Condition `
        -Condition ($unexpectedJarvis.Count -eq 0) `
        -Message (
            'Unexpected Jarvis mapping(s): ' +
            (($unexpectedJarvis | ConvertTo-Json -Depth 6 -Compress))
        )
    return $targetMappings
}

function Assert-SessionPlanIdentity {
    Assert-Condition `
        -Condition (-not [string]::IsNullOrWhiteSpace($SessionPlanPath)) `
        -Message 'SessionPlanPath is required for this action.'
    $resolvedPlanPath = Resolve-RestrictedPath `
        -Path $SessionPlanPath `
        -AllowedRoot $allowedPlanRoot `
        -MustExist
    $planJson = Get-Content -LiteralPath $resolvedPlanPath -Raw
    Assert-Condition `
        -Condition ($planJson |
            Test-Json -SchemaFile $planSchemaPath -ErrorAction Stop) `
        -Message 'Session plan failed its committed JSON schema.'
    $plan = $planJson | ConvertFrom-Json -Depth 100
    Assert-Condition `
        -Condition ([string]$plan.result -ceq 'passed') `
        -Message 'Session plan did not pass.'
    Assert-Condition `
        -Condition ([string]$plan.state -ceq 'awaiting-exact-approval') `
        -Message 'Session plan state drifted.'
    Assert-Condition `
        -Condition ([string]$plan.moduleId -ceq $moduleId) `
        -Message 'Session plan module drifted.'
    Assert-Condition `
        -Condition (
            -not [bool]$plan.activationPermitted -and
            -not [bool]$plan.mutationPerformed -and
            [string]$plan.liveExplorer -ceq 'not-run'
        ) `
        -Message 'Session plan crossed its non-activation evidence boundary.'
    Assert-Condition `
        -Condition (
            [string]$plan.approval.exactCommand -ceq $clearCommand -and
            -not [bool]$plan.approval.exactCommandApproved -and
            -not [bool]$plan.approval.canExecuteNow
        ) `
        -Message 'Session plan approval boundary drifted.'
    $expiresAt = if ($plan.expiresAtUtc -is [DateTime]) {
        [DateTimeOffset]::new(
            ([DateTime]$plan.expiresAtUtc).ToUniversalTime(),
            [TimeSpan]::Zero)
    }
    elseif ($plan.expiresAtUtc -is [DateTimeOffset]) {
        ([DateTimeOffset]$plan.expiresAtUtc).ToUniversalTime()
    }
    else {
        [DateTimeOffset]::Parse(
            [string]$plan.expiresAtUtc,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    }
    Assert-Condition `
        -Condition ($expiresAt -gt [DateTimeOffset]::UtcNow) `
        -Message 'Session plan has expired.'

    $controllerIdentity = $plan.sourceIdentity.controlledLiveController
    Assert-Condition `
        -Condition ($null -ne $controllerIdentity) `
        -Message 'Session plan does not bind the controlled-live controller.'
    Assert-Condition `
        -Condition (
            [string]$controllerIdentity.relativePath -ceq
                $controllerRelativePath
        ) `
        -Message 'Session plan controller path drifted.'
    $controllerItem = Get-Item -LiteralPath $PSCommandPath -Force
    Assert-Condition `
        -Condition (
            [int64]$controllerIdentity.size -eq [int64]$controllerItem.Length
        ) `
        -Message 'Session plan controller size drifted.'
    Assert-Condition `
        -Condition (
            [string]$controllerIdentity.sha256 -ieq
                (Get-Sha256 $PSCommandPath)
        ) `
        -Message 'Session plan controller hash drifted.'

    return [pscustomobject]@{
        Plan = $plan
        PlanPath = $resolvedPlanPath
        PlanSha256 = Get-Sha256 $resolvedPlanPath
    }
}

function Assert-PlanAndRecoveryLease {
    $planIdentity = Assert-SessionPlanIdentity
    $plan = $planIdentity.Plan
    $resolvedPlanPath = $planIdentity.PlanPath
    $leaseReport = Invoke-Supervisor `
        -Arguments @(
            'inspect-recovery-terminal',
            '--module',
            $moduleId
        )
    Assert-Condition `
        -Condition ([bool]$leaseReport.ready) `
        -Message 'Recovery terminal lease is not ready.'
    Assert-Condition `
        -Condition (
            [string]$leaseReport.sessionPlanRunId -ceq
                [string]$plan.runId
        ) `
        -Message 'Recovery lease session-plan run ID drifted.'

    $lease = Get-Content -LiteralPath $leasePath -Raw |
        ConvertFrom-Json -Depth 100
    Assert-Condition `
        -Condition (
            [IO.Path]::GetFullPath([string]$lease.planPath) -ieq
                $resolvedPlanPath
        ) `
        -Message 'Recovery lease points to another session plan.'
    Assert-Condition `
        -Condition (
            [string]$lease.planSha256 -ieq
                (Get-Sha256 $resolvedPlanPath)
        ) `
        -Message 'Recovery lease session-plan hash drifted.'

    return [pscustomobject]@{
        Plan = $plan
        PlanPath = $resolvedPlanPath
        PlanSha256 = $planIdentity.PlanSha256
        Lease = $leaseReport
    }
}

function Assert-ExactPermit {
    Assert-Condition `
        -Condition (Test-Path -LiteralPath $permitPath -PathType Leaf) `
        -Message 'The one-shot M2 permit is missing.'
    [byte[]]$bytes = [IO.File]::ReadAllBytes($permitPath)
    $expectedBytes = [Text.Encoding]::ASCII.GetBytes($moduleId)
    Assert-Condition `
        -Condition ([Linq.Enumerable]::SequenceEqual(
            [byte[]]$bytes,
            [byte[]]$expectedBytes)) `
        -Message 'The one-shot permit payload is not the exact ASCII M2 id.'
    $lastWriteUtc = [DateTimeOffset]::new(
        [IO.File]::GetLastWriteTimeUtc($permitPath),
        [TimeSpan]::Zero)
    $age = [DateTimeOffset]::UtcNow - $lastWriteUtc
    Assert-Condition `
        -Condition ($age -ge [TimeSpan]::Zero) `
        -Message 'The one-shot permit is future-dated.'
    Assert-Condition `
        -Condition ($age -le [TimeSpan]::FromMinutes(5)) `
        -Message 'The one-shot permit is older than five minutes.'
}

function Wait-ServiceState {
    param(
        [Parameter(Mandatory)] [string]$State,
        [ValidateRange(1, 30)] [int]$TimeoutSeconds = 8
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $service = Get-ServiceSnapshot
        if ([string]$service.State -ceq $State) {
            return $service
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Windhawk did not reach service state $State within $TimeoutSeconds seconds."
}

function Assert-NoReparsePointsInPath {
    param([Parameter(Mandatory)] [string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($fullPath)
    $current = $pathRoot
    foreach ($segment in $fullPath.Substring($pathRoot.Length).Split(
            @('\', '/'),
            [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current)) {
            continue
        }
        $item = Get-Item -LiteralPath $current -Force
        Assert-Condition `
            -Condition (
                ($item.Attributes -band
                    [IO.FileAttributes]::ReparsePoint) -eq 0
            ) `
            -Message "Path contains a reparse point: $($item.FullName)"
    }
}

function Install-FileAtomically {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination,
        [Parameter(Mandatory)] [string]$ExpectedSha256
    )

    $null = Assert-NoReparsePointsInPath $Source
    $null = Assert-NoReparsePointsInPath $Destination
    $directory = Split-Path -Parent $Destination
    $temporaryPath = Join-Path $directory (
        '.' + [IO.Path]::GetFileName($Destination) + '.' +
        [Guid]::NewGuid().ToString('N') + '.tmp'
    )
    try {
        [IO.File]::Copy($Source, $temporaryPath, $false)
        Assert-Condition `
            -Condition ((Get-Sha256 $temporaryPath) -ceq $ExpectedSha256) `
            -Message "Temporary file hash mismatch: $temporaryPath"
        [IO.File]::Move($temporaryPath, $Destination, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Retire-StaleRecoveryLease {
    if (-not (Test-Path -LiteralPath $leasePath)) {
        return $false
    }
    Assert-Condition `
        -Condition $RetireStaleRecoveryLease `
        -Message (
            'A recovery lease file exists. Retire it only with the exact ' +
            '-RetireStaleRecoveryLease switch after confirming it is blocked.'
        )
    $null = Assert-NoReparsePointsInPath $leasePath
    $probe = Get-RecoveryLeaseProbe
    Assert-Condition `
        -Condition (
            [int]$probe.ExitCode -eq 11 -and
            -not [bool]$probe.Report.ready -and
            [string]$probe.Report.status -ceq 'blocked'
        ) `
        -Message 'The recovery lease is still ready or could not fail closed.'
    $lease = Get-Content -LiteralPath $leasePath -Raw |
        ConvertFrom-Json -Depth 100
    Assert-Condition `
        -Condition (
            [int]$lease.schemaVersion -eq 1 -and
            [string]$lease.receiptType -ceq
                'jarvisv2-m2-recovery-terminal-lease' -and
            [string]$lease.moduleId -ceq $moduleId -and
            [int]$lease.processId -gt 0
        ) `
        -Message 'The blocked recovery lease has an unexpected identity.'
    $leaseProcess =
        Get-Process -Id ([int]$lease.processId) -ErrorAction SilentlyContinue
    Assert-Condition `
        -Condition ($null -eq $leaseProcess) `
        -Message (
            "Recovery terminal PID $($lease.processId) is still alive; " +
            'the lease cannot be retired.'
        )
    $script:mutationPerformed = $true
    Remove-Item -LiteralPath $leasePath -Force
    Assert-Condition `
        -Condition (-not (Test-Path -LiteralPath $leasePath)) `
        -Message 'The blocked recovery lease could not be retired.'
    return $true
}

function Invoke-ArmKillSwitch {
    return Invoke-Supervisor -Arguments @('arm-kill-switch')
}

function New-ControllerResult {
    param(
        [Parameter(Mandatory)] [string]$Result,
        [Parameter(Mandatory)] [bool]$MutationPerformed,
        [Parameter(Mandatory)] [bool]$StopRequired,
        [object]$Detail
    )

    return [ordered]@{
        schemaVersion = 1
        receiptType = 'jarvisv2-m2-controlled-live-controller'
        action = $Action
        result = $Result
        moduleId = $moduleId
        observedAtUtc = [DateTime]::UtcNow.ToString('o')
        expectedExplorerProcessId = $ExpectedExplorerProcessId
        mutationPerformed = $MutationPerformed
        stopRequired = $StopRequired
        explorerRestartRequested = $false
        processTerminationRequested = $false
        serviceStartModeMutationRequested = $false
        exactClearCommand = $clearCommand
        emergencyArmCommand = $armCommand
        detail = $Detail
    }
}

function Publish-ControllerResult {
    param([Parameter(Mandatory)] [object]$Result)

    $json = $Result | ConvertTo-Json -Depth 16
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $resolvedOutputPath = Resolve-RestrictedPath `
            -Path $OutputPath `
            -AllowedRoot $allowedOutputRoot
        Assert-Condition `
            -Condition (-not (Test-Path -LiteralPath $resolvedOutputPath)) `
            -Message 'Refusing to overwrite an existing controller receipt.'
        $null = [IO.Directory]::CreateDirectory(
            (Split-Path -Parent $resolvedOutputPath))
        [IO.File]::WriteAllText(
            $resolvedOutputPath,
            $json + [Environment]::NewLine,
            [Text.UTF8Encoding]::new($false))
    }
    Write-Output $json
}

function Get-ReadOnlySnapshot {
    $compatibility = Get-CompatibilityReport
    $explorerProcessId = [int]$compatibility.explorerRuntime.processId
    $service = Get-ServiceSnapshot
    $config = $null
    $configError = $null
    $installedGeneration = 'unknown'
    try {
        $config = Get-CanonicalModConfig
        $installedGeneration = 'phase5-canonical'
    }
    catch {
        $phase5Error = $_.Exception.Message
        try {
            $config = Get-CanonicalModConfig `
                -RequiredSourceSha256 $oldInstalledSourceSha256 `
                -RequiredDllSha256 $oldInstalledDllSha256
            $installedGeneration = 'phase4-reviewed-old'
            $configError =
                "Phase 5 canonical files are not installed: $phase5Error"
        }
        catch {
            $configError = (
                "Phase 5 check: $phase5Error; old reviewed check: " +
                $_.Exception.Message
            )
        }
    }
    $recovery = $null
    try {
        $recovery = Invoke-Supervisor `
            -Arguments @(
                'inspect-recovery-terminal',
                '--module',
                $moduleId
            )
    }
    catch {
        $recovery = [pscustomobject]@{
            ready = $false
            status = 'blocked'
            error = $_.Exception.Message
        }
    }
    return [ordered]@{
        compatibilityPassed = [bool]$compatibility.compatible
        compatibilityCheckCount = @($compatibility.checks).Count
        explorerProcessId = $explorerProcessId
        killSwitchState = [string]$compatibility.host.killSwitchState
        permitState = [string]$compatibility.host.activeModuleState
        permitModuleId = $compatibility.host.activeModuleId
        serviceState = [string]$service.State
        serviceStartMode = [string]$service.StartMode
        serviceProcessId = [int]$service.ProcessId
        configuredModule = $windhawkModId
        installedGeneration = $installedGeneration
        targetDisabled = if ($null -ne $config) {
            [int]$config.Disabled -eq 1
        } else {
            $null
        }
        configError = $configError
        recoveryTerminalReady = [bool]$recovery.ready
        recoveryTerminalStatus = [string]$recovery.status
        recoveryTerminalError = $recovery.error
        explorerJarvisMappings =
            @(Get-DesktopExplorerMappings $explorerProcessId)
        allJarvisMappings = @(Get-ProcessMappings)
        allWindhawkAndJarvisMappings =
            @(Get-ProcessMappings -IncludeWindhawkRuntime)
    }
}

function Invoke-InspectAction {
    $snapshot = Get-ReadOnlySnapshot
    $planInspection = $null
    if (-not [string]::IsNullOrWhiteSpace($SessionPlanPath)) {
        $planIdentity = Assert-SessionPlanIdentity
        $dryRun = Assert-LockedPlanDryRun $planIdentity
        $planInspection = [ordered]@{
            runId = [string]$planIdentity.Plan.runId
            path = [string]$planIdentity.PlanPath
            sha256 = [string]$planIdentity.PlanSha256
            controllerSha256 = Get-Sha256 $PSCommandPath
            lockedDryRunPassed =
                [string]$dryRun.result -ceq 'passed'
            activationPermitted = $false
            mutationPerformed = $false
        }
    }
    $snapshot['planInspection'] = $planInspection
    return New-ControllerResult `
        -Result 'passed-read-only' `
        -MutationPerformed $false `
        -StopRequired $false `
        -Detail $snapshot
}

function Invoke-UpdateDisabledInstallationAction {
    Assert-Condition `
        -Condition $ConfirmUpdateDisabledInstallation `
        -Message (
            'UpdateDisabledInstallation is inert without ' +
            '-ConfirmUpdateDisabledInstallation.'
        )
    Assert-Administrator
    $planIdentity = Assert-SessionPlanIdentity
    $null = Assert-LockedPlanDryRun $planIdentity
    $compatibility = Get-CompatibilityReport
    Assert-ExplorerIdentity $compatibility $ExpectedExplorerProcessId
    Assert-Condition `
        -Condition (
            [string]$compatibility.host.killSwitchState -ceq 'armed'
        ) `
        -Message 'Kill switch must be armed during disabled installation.'
    Assert-Condition `
        -Condition (
            [string]$compatibility.host.activeModuleState -ceq 'absent'
        ) `
        -Message 'The one-shot permit must be absent during installation.'
    $service = Get-ServiceSnapshot
    Assert-Condition `
        -Condition (
            [string]$service.State -ceq 'Stopped' -and
            [string]$service.StartMode -ceq 'Manual' -and
            [int]$service.ProcessId -eq 0
        ) `
        -Message 'Windhawk must be Stopped / Manual / PID 0 during installation.'
    $oldConfig = Get-CanonicalModConfig `
        -RequiredSourceSha256 $oldInstalledSourceSha256 `
        -RequiredDllSha256 $oldInstalledDllSha256
    Assert-Condition `
        -Condition ([int]$oldConfig.Disabled -eq 1) `
        -Message 'M2 must remain disabled during installation.'
    $null = Assert-TargetMappingState `
        -ExplorerProcessId $ExpectedExplorerProcessId `
        -ExpectedCount 0
    $runtimeMappings =
        @(Get-ProcessMappings -IncludeWindhawkRuntime)
    Assert-Condition `
        -Condition ($runtimeMappings.Count -eq 0) `
        -Message (
            'A Windhawk/Jarvis runtime mapping is still present: ' +
            ($runtimeMappings | ConvertTo-Json -Depth 8 -Compress)
        )
    foreach ($source in @(
        [pscustomobject]@{
            Path = $canonicalSourcePath
            Hash = $expectedSourceSha256
            Label = 'canonical M2 source'
        },
        [pscustomobject]@{
            Path = $canonicalDllPath
            Hash = $expectedDllSha256
            Label = 'canonical M2 DLL'
        }
    )) {
        Assert-Condition `
            -Condition (Test-Path -LiteralPath $source.Path -PathType Leaf) `
            -Message "$($source.Label) is missing: $($source.Path)"
        Assert-Condition `
            -Condition ((Get-Sha256 $source.Path) -ceq $source.Hash) `
            -Message "$($source.Label) hash drifted."
    }

    $staleLeaseRetired = Retire-StaleRecoveryLease
    $backupDirectory = Join-Path $disabledInstallBackupRoot (
        [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' +
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    )
    Assert-Condition `
        -Condition (-not (Test-Path -LiteralPath $backupDirectory)) `
        -Message 'Refusing to reuse an installation backup directory.'
    $null = Assert-NoReparsePointsInPath $backupDirectory
    $null = [IO.Directory]::CreateDirectory($backupDirectory)
    $script:mutationPerformed = $true
    $backupSourcePath =
        Join-Path $backupDirectory 'installed-source.wh.cpp'
    $backupDllPath = Join-Path $backupDirectory $libraryFileName
    [IO.File]::Copy($installedSourcePath, $backupSourcePath, $false)
    [IO.File]::Copy($installedDllPath, $backupDllPath, $false)
    Assert-Condition `
        -Condition (
            (Get-Sha256 $backupSourcePath) -ceq
                $oldInstalledSourceSha256
        ) `
        -Message 'Installed source backup verification failed.'
    Assert-Condition `
        -Condition (
            (Get-Sha256 $backupDllPath) -ceq $oldInstalledDllSha256
        ) `
        -Message 'Installed DLL backup verification failed.'

    $sourceReplaced = $false
    $dllReplaced = $false
    try {
        Install-FileAtomically `
            -Source $canonicalSourcePath `
            -Destination $installedSourcePath `
            -ExpectedSha256 $expectedSourceSha256
        $sourceReplaced = $true
        Install-FileAtomically `
            -Source $canonicalDllPath `
            -Destination $installedDllPath `
            -ExpectedSha256 $expectedDllSha256
        $dllReplaced = $true

        $newConfig = Get-CanonicalModConfig
        Assert-Condition `
            -Condition ([int]$newConfig.Disabled -eq 1) `
            -Message 'M2 became enabled during disabled installation.'
        $service = Get-ServiceSnapshot
        Assert-Condition `
            -Condition (
                [string]$service.State -ceq 'Stopped' -and
                [string]$service.StartMode -ceq 'Manual' -and
                [int]$service.ProcessId -eq 0
            ) `
            -Message 'Windhawk service state changed during installation.'
        $compatibility = Get-CompatibilityReport
        Assert-ExplorerIdentity $compatibility $ExpectedExplorerProcessId
        Assert-Condition `
            -Condition (
                [string]$compatibility.host.killSwitchState -ceq 'armed' -and
                [string]$compatibility.host.activeModuleState -ceq 'absent'
            ) `
            -Message 'Locked state changed during disabled installation.'
        Assert-Condition `
            -Condition (-not (Test-Path -LiteralPath $leasePath)) `
            -Message 'A recovery lease appeared during disabled installation.'
        $null = Assert-TargetMappingState `
            -ExplorerProcessId $ExpectedExplorerProcessId `
            -ExpectedCount 0
        Assert-Condition `
            -Condition (
                @(Get-ProcessMappings -IncludeWindhawkRuntime).Count -eq 0
            ) `
            -Message 'A Windhawk/Jarvis mapping appeared during installation.'
    }
    catch {
        $installationError = $_.Exception
        $rollbackErrors = [System.Collections.Generic.List[Exception]]::new()
        if ($sourceReplaced) {
            try {
                Install-FileAtomically `
                    -Source $backupSourcePath `
                    -Destination $installedSourcePath `
                    -ExpectedSha256 $oldInstalledSourceSha256
            }
            catch {
                $rollbackErrors.Add($_.Exception)
            }
        }
        if ($dllReplaced) {
            try {
                Install-FileAtomically `
                    -Source $backupDllPath `
                    -Destination $installedDllPath `
                    -ExpectedSha256 $oldInstalledDllSha256
            }
            catch {
                $rollbackErrors.Add($_.Exception)
            }
        }
        if ($rollbackErrors.Count -ne 0) {
            $script:stopRequired = $true
            throw [AggregateException]::new(
                'Disabled M2 installation failed and rollback was incomplete.',
                @($installationError) + @($rollbackErrors))
        }
        throw [InvalidOperationException]::new(
            'Disabled M2 installation failed and old files were restored.',
            $installationError)
    }

    return New-ControllerResult `
        -Result 'passed-disabled-installation-updated' `
        -MutationPerformed $true `
        -StopRequired $false `
        -Detail ([ordered]@{
            sessionPlanRunId = [string]$planIdentity.Plan.runId
            sessionPlanSha256 = [string]$planIdentity.PlanSha256
            explorerProcessId = $ExpectedExplorerProcessId
            staleRecoveryLeaseRetired = $staleLeaseRetired
            targetDisabled = $true
            serviceState = [string]$service.State
            serviceStartMode = [string]$service.StartMode
            serviceProcessId = [int]$service.ProcessId
            killSwitchArmed = $true
            permitPresent = $false
            sourceSha256 = Get-Sha256 $installedSourcePath
            dllSha256 = Get-Sha256 $installedDllPath
            backupDirectory = $backupDirectory
            targetMappingCount = 0
        })
}

function Invoke-StartDisabledHostAction {
    Assert-Condition `
        -Condition $ConfirmStartDisabledHost `
        -Message (
            'StartDisabledHost is inert without -ConfirmStartDisabledHost.'
        )
    Assert-Administrator
    $planLease = Assert-PlanAndRecoveryLease
    $compatibility = Get-CompatibilityReport
    Assert-ExplorerIdentity $compatibility $ExpectedExplorerProcessId
    Assert-Condition `
        -Condition (
            [string]$compatibility.host.killSwitchState -ceq 'armed'
        ) `
        -Message 'Kill switch must be armed before starting Windhawk.'
    Assert-Condition `
        -Condition (
            [string]$compatibility.host.activeModuleState -ceq 'absent'
        ) `
        -Message 'One-shot permit must be absent before starting Windhawk.'
    $service = Get-ServiceSnapshot
    Assert-Condition `
        -Condition (
            [string]$service.State -ceq 'Stopped' -and
            [string]$service.StartMode -ceq 'Manual' -and
            [int]$service.ProcessId -eq 0
        ) `
        -Message 'Windhawk must be Stopped / Manual / PID 0 before start.'
    $config = Get-CanonicalModConfig
    Assert-Condition `
        -Condition ([int]$config.Disabled -eq 1) `
        -Message 'M2 must remain disabled while the service starts.'
    $null = Assert-TargetMappingState `
        -ExplorerProcessId $ExpectedExplorerProcessId `
        -ExpectedCount 0

    $serviceStarted = $false
    try {
        # Starting the service through SCM preserves its Manual start mode.
        $script:mutationPerformed = $true
        Start-Service -Name $serviceName -ErrorAction Stop
        $serviceStarted = $true
        $service = Wait-ServiceState -State 'Running' -TimeoutSeconds 8
        Assert-Condition `
            -Condition (
                [string]$service.StartMode -ceq 'Manual' -and
                [int]$service.ProcessId -gt 0
            ) `
            -Message 'Windhawk did not remain Running / Manual after start.'
        $planLease = Assert-PlanAndRecoveryLease
        $compatibility = Get-CompatibilityReport
        Assert-ExplorerIdentity $compatibility $ExpectedExplorerProcessId
        Assert-Condition `
            -Condition (
                [string]$compatibility.host.killSwitchState -ceq 'armed' -and
                [string]$compatibility.host.activeModuleState -ceq 'absent'
            ) `
            -Message 'Locked state changed while starting disabled Windhawk.'
        $config = Get-CanonicalModConfig
        Assert-Condition `
            -Condition ([int]$config.Disabled -eq 1) `
            -Message 'M2 became enabled while starting the service.'
        $null = Assert-TargetMappingState `
            -ExplorerProcessId $ExpectedExplorerProcessId `
            -ExpectedCount 0
    }
    catch {
        $startError = $_.Exception
        if ($serviceStarted) {
            try {
                Stop-Service -Name $serviceName -ErrorAction Stop
                $null = Wait-ServiceState -State 'Stopped' -TimeoutSeconds 8
            }
            catch {
                $script:stopRequired = $true
                throw [AggregateException]::new(
                    'Disabled Windhawk start failed and normal service stop also failed.',
                    @($startError, $_.Exception))
            }
        }
        throw $startError
    }

    return New-ControllerResult `
        -Result 'passed-disabled-host-running' `
        -MutationPerformed $true `
        -StopRequired $false `
        -Detail ([ordered]@{
            sessionPlanRunId = [string]$planLease.Plan.runId
            sessionPlanSha256 = [string]$planLease.PlanSha256
            recoveryTerminalProcessId = [int]$planLease.Lease.processId
            explorerProcessId = $ExpectedExplorerProcessId
            serviceState = [string]$service.State
            serviceStartMode = [string]$service.StartMode
            serviceProcessId = [int]$service.ProcessId
            targetDisabled = $true
            permitPresent = $false
            killSwitchArmed = $true
            targetMappingCount = 0
        })
}

function Invoke-FailClosedAfterEnableError {
    param([Parameter(Mandatory)] [Exception]$ActivationError)

    $recoveryErrors = [System.Collections.Generic.List[Exception]]::new()
    try {
        $null = Invoke-ArmKillSwitch
    }
    catch {
        $recoveryErrors.Add($_.Exception)
    }
    try {
        if (Test-Path -LiteralPath $modRegistryPath) {
            Set-ItemProperty `
                -LiteralPath $modRegistryPath `
                -Name Disabled `
                -Value 1
        }
    }
    catch {
        $recoveryErrors.Add($_.Exception)
    }
    try {
        $service = Get-ServiceSnapshot
        if ([string]$service.State -ne 'Stopped') {
            Stop-Service -Name $serviceName -ErrorAction Stop
            $null = Wait-ServiceState -State 'Stopped' -TimeoutSeconds 8
        }
    }
    catch {
        $recoveryErrors.Add($_.Exception)
    }

    if ($recoveryErrors.Count -ne 0) {
        throw [AggregateException]::new(
            'M2 enable failed and fail-closed recovery was incomplete.',
            @($ActivationError) + @($recoveryErrors))
    }
    throw [InvalidOperationException]::new(
        'M2 enable failed; kill switch was re-armed, M2 was disabled, and Windhawk was stopped.',
        $ActivationError)
}

function Assert-ActiveState {
    $planLease = Assert-PlanAndRecoveryLease
    $compatibility = Get-CompatibilityReport
    Assert-ExplorerIdentity $compatibility $ExpectedExplorerProcessId
    Assert-Condition `
        -Condition (
            [string]$compatibility.host.killSwitchState -ceq 'disarmed'
        ) `
        -Message 'Kill switch is not disarmed during the active observation.'
    Assert-Condition `
        -Condition (
            [string]$compatibility.host.activeModuleState -ceq 'absent'
        ) `
        -Message 'The one-shot permit was not consumed exactly once.'
    $service = Get-ServiceSnapshot
    Assert-Condition `
        -Condition (
            [string]$service.State -ceq 'Running' -and
            [string]$service.StartMode -ceq 'Manual' -and
            [int]$service.ProcessId -gt 0
        ) `
        -Message 'Windhawk is not Running / Manual during observation.'
    $config = Get-CanonicalModConfig
    Assert-Condition `
        -Condition ([int]$config.Disabled -eq 0) `
        -Message 'M2 is not enabled during the active observation.'
    $targetMappings = Assert-TargetMappingState `
        -ExplorerProcessId $ExpectedExplorerProcessId `
        -ExpectedCount 1
    return [pscustomobject]@{
        PlanLease = $planLease
        Compatibility = $compatibility
        Service = $service
        TargetMappings = $targetMappings
    }
}

function Invoke-EnableOnceAction {
    Assert-Condition `
        -Condition $ConfirmEnableOnce `
        -Message 'EnableOnce is inert without -ConfirmEnableOnce.'
    $script:stopRequired = $true
    try {
        Assert-Administrator
        $planLease = Assert-PlanAndRecoveryLease
        $compatibility = Get-CompatibilityReport
        Assert-ExplorerIdentity $compatibility $ExpectedExplorerProcessId
        Assert-Condition `
            -Condition (
                [string]$compatibility.host.killSwitchState -ceq 'disarmed'
            ) `
            -Message (
                'The exact clear-kill-switch command must succeed immediately ' +
                'before EnableOnce.'
            )
        Assert-Condition `
            -Condition (
                [string]$compatibility.host.activeModuleState -ceq 'valid' -and
                [string]$compatibility.host.activeModuleId -ceq $moduleId
            ) `
            -Message 'A fresh exact M2 one-shot permit is required.'
        Assert-ExactPermit
        $service = Get-ServiceSnapshot
        Assert-Condition `
            -Condition (
                [string]$service.State -ceq 'Running' -and
                [string]$service.StartMode -ceq 'Manual' -and
                [int]$service.ProcessId -gt 0
            ) `
            -Message 'Windhawk must already be Running / Manual.'
        $config = Get-CanonicalModConfig
        Assert-Condition `
            -Condition ([int]$config.Disabled -eq 1) `
            -Message 'M2 must still be disabled before the one-shot enable.'
        $null = Assert-TargetMappingState `
            -ExplorerProcessId $ExpectedExplorerProcessId `
            -ExpectedCount 0

        $script:mutationPerformed = $true
        Set-ItemProperty `
            -LiteralPath $modRegistryPath `
            -Name Disabled `
            -Value 0

        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        $activeState = $null
        do {
            try {
                $activeState = Assert-ActiveState
                break
            }
            catch {
                $lastActivationCheckError = $_.Exception
                Start-Sleep -Milliseconds 500
            }
        } while ([DateTime]::UtcNow -lt $deadline)

        if ($null -eq $activeState) {
            throw [TimeoutException]::new(
                'M2 did not reach the exact one-shot active state within 10 seconds.',
                $lastActivationCheckError)
        }
    }
    catch {
        Invoke-FailClosedAfterEnableError -ActivationError $_.Exception
    }
    $script:stopRequired = $false

    return New-ControllerResult `
        -Result 'passed-one-shot-m2-active' `
        -MutationPerformed $true `
        -StopRequired $false `
        -Detail ([ordered]@{
            sessionPlanRunId = [string]$planLease.Plan.runId
            sessionPlanSha256 = [string]$planLease.PlanSha256
            recoveryTerminalProcessId = [int]$planLease.Lease.processId
            explorerProcessId = $ExpectedExplorerProcessId
            serviceState = [string]$activeState.Service.State
            serviceStartMode = [string]$activeState.Service.StartMode
            serviceProcessId = [int]$activeState.Service.ProcessId
            targetDisabled = $false
            permitConsumed = $true
            killSwitchArmed = $false
            targetMappingCount =
                @($activeState.TargetMappings).Count
            targetDllSha256 = Get-Sha256 $installedDllPath
        })
}

function Invoke-ObserveAction {
    $script:stopRequired = $true
    Assert-Condition `
        -Condition ($ExpectedExplorerProcessId -gt 0) `
        -Message 'ExpectedExplorerProcessId is required for Observe.'
    $initial = Assert-ActiveState
    $explorer =
        Get-Process -Id $ExpectedExplorerProcessId -ErrorAction Stop
    $previousCpu = [double]$explorer.CPU
    $previousSampleAt = [DateTimeOffset]::UtcNow
    $maximumObservedCpuPercent = 0.0
    $consecutiveElevatedSamples = 0
    $samples = [System.Collections.Generic.List[object]]::new()
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($ObservationSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 1000
        $state = Assert-ActiveState
        $explorer =
            Get-Process -Id $ExpectedExplorerProcessId -ErrorAction Stop
        Assert-Condition `
            -Condition $explorer.Responding `
            -Message 'Desktop Explorer stopped responding.'
        $sampledAt = [DateTimeOffset]::UtcNow
        $elapsedSeconds =
            ($sampledAt - $previousSampleAt).TotalSeconds
        $cpuDelta = [double]$explorer.CPU - $previousCpu
        $singleCoreCpuPercent = if ($elapsedSeconds -gt 0) {
            [Math]::Max(0.0, 100.0 * $cpuDelta / $elapsedSeconds)
        }
        else {
            0.0
        }
        $maximumObservedCpuPercent =
            [Math]::Max($maximumObservedCpuPercent, $singleCoreCpuPercent)
        if ($singleCoreCpuPercent -gt $MaxSingleCoreCpuPercent) {
            $consecutiveElevatedSamples++
        }
        else {
            $consecutiveElevatedSamples = 0
        }
        Assert-Condition `
            -Condition ($consecutiveElevatedSamples -lt 3) `
            -Message (
                'Explorer exceeded the idle CPU threshold for three ' +
                'consecutive samples.'
            )
        $samples.Add([pscustomobject]@{
            sampledAtUtc = $sampledAt.ToString('o')
            approximateSingleCoreCpuPercent =
                [Math]::Round($singleCoreCpuPercent, 3)
            workingSetBytes = [int64]$explorer.WorkingSet64
            handleCount = [int]$explorer.HandleCount
            threadCount = @($explorer.Threads).Count
            recoveryTerminalProcessId =
                [int]$state.PlanLease.Lease.processId
        })
        $previousCpu = [double]$explorer.CPU
        $previousSampleAt = $sampledAt
    }

    $script:stopRequired = $false
    return New-ControllerResult `
        -Result 'passed-bounded-idle-observation' `
        -MutationPerformed $false `
        -StopRequired $false `
        -Detail ([ordered]@{
            sessionPlanRunId = [string]$initial.PlanLease.Plan.runId
            sessionPlanSha256 = [string]$initial.PlanLease.PlanSha256
            explorerProcessId = $ExpectedExplorerProcessId
            observationSeconds = $ObservationSeconds
            maxAllowedSingleCoreCpuPercent =
                $MaxSingleCoreCpuPercent
            maximumObservedSingleCoreCpuPercent =
                [Math]::Round($maximumObservedCpuPercent, 3)
            sampleCount = $samples.Count
            samples = $samples
        })
}

function Invoke-RecoverAction {
    Assert-Condition `
        -Condition $ConfirmRecover `
        -Message 'Recover is inert without -ConfirmRecover.'
    Assert-Administrator

    # Recovery deliberately does not require a live session plan or lease.
    # The terminal may be the reason recovery is required. The emergency flag
    # and permit revocation must already have been confirmed first.
    Assert-Condition `
        -Condition (Test-Path -LiteralPath $killSwitchPath -PathType Leaf) `
        -Message "Run the emergency arm command first: $armCommand"
    Assert-Condition `
        -Condition (-not (Test-Path -LiteralPath $permitPath)) `
        -Message 'The one-shot permit must be absent before cleanup.'

    $recoveryErrors = [System.Collections.Generic.List[string]]::new()
    try {
        if (Test-Path -LiteralPath $modRegistryPath) {
            $script:mutationPerformed = $true
            Set-ItemProperty `
                -LiteralPath $modRegistryPath `
                -Name Disabled `
                -Value 1
        }
    }
    catch {
        $recoveryErrors.Add(
            "M2 disable failed: $($_.Exception.Message)")
    }

    try {
        $service = Get-ServiceSnapshot
        if ([string]$service.State -ne 'Stopped') {
            $script:mutationPerformed = $true
            Stop-Service -Name $serviceName -ErrorAction Stop
        }
        $service =
            Wait-ServiceState -State 'Stopped' -TimeoutSeconds 10
    }
    catch {
        $recoveryErrors.Add(
            "Normal Windhawk service stop failed: $($_.Exception.Message)")
        $service = Get-ServiceSnapshot
    }

    $after = $null
    try {
        $after = Get-CompatibilityReport
    }
    catch {
        $recoveryErrors.Add(
            "Post-recovery compatibility inspection failed: $($_.Exception.Message)")
    }
    $actualExplorerProcessId = if ($null -ne $after) {
        [int]$after.explorerRuntime.processId
    }
    elseif (
        $ExpectedExplorerProcessId -gt 0 -and
        $null -ne (Get-Process `
            -Id $ExpectedExplorerProcessId `
            -ErrorAction SilentlyContinue)
    ) {
        $ExpectedExplorerProcessId
    }
    else {
        0
    }
    $targetMappings = @()
    if ($actualExplorerProcessId -gt 0) {
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        do {
            try {
                $targetMappings = @(
                    Get-DesktopExplorerMappings $actualExplorerProcessId |
                        Where-Object {
                            [string]$_.path -ieq $installedDllPath
                        }
                )
            }
            catch {
                $recoveryErrors.Add(
                    "Explorer mapping inspection failed: $($_.Exception.Message)")
                break
            }
            if ($targetMappings.Count -eq 0) {
                break
            }
            Start-Sleep -Milliseconds 500
        } while ([DateTime]::UtcNow -lt $deadline)
    }
    else {
        $recoveryErrors.Add(
            'Desktop Explorer is unavailable for post-recovery mapping inspection.')
    }

    $targetDisabled = $null
    try {
        $targetDisabled =
            [int](Get-ItemProperty `
                -LiteralPath $modRegistryPath `
                -Name Disabled `
                -ErrorAction Stop).Disabled -eq 1
        if (-not $targetDisabled) {
            $recoveryErrors.Add('M2 registry state is not disabled.')
        }
    }
    catch {
        $recoveryErrors.Add(
            "M2 disabled-state verification failed: $($_.Exception.Message)")
    }
    if ([string]$service.State -ne 'Stopped') {
        $recoveryErrors.Add(
            "Windhawk service remained $($service.State).")
    }
    if ([string]$service.StartMode -ne 'Manual') {
        $recoveryErrors.Add(
            "Windhawk start mode drifted to $($service.StartMode).")
    }
    if ([int]$service.ProcessId -ne 0) {
        $recoveryErrors.Add(
            "Windhawk service PID remained $($service.ProcessId).")
    }
    if (-not (Test-Path -LiteralPath $killSwitchPath -PathType Leaf)) {
        $recoveryErrors.Add('Kill switch is not armed after recovery.')
    }
    if (Test-Path -LiteralPath $permitPath) {
        $recoveryErrors.Add('Permit is not absent after recovery.')
    }
    if ($targetMappings.Count -ne 0) {
        $recoveryErrors.Add(
            'The M2 DLL remains physically mapped in desktop Explorer.')
    }

    $runtimeMappings =
        @(Get-ProcessMappings -IncludeWindhawkRuntime)
    $result = if ($recoveryErrors.Count -eq 0) {
        if ($runtimeMappings.Count -eq 0) {
            'passed-locked-zero-mappings'
        }
        else {
            'passed-locked-runtime-residual-recorded'
        }
    }
    else {
        'failed-locked-state-needs-attention'
    }
    return New-ControllerResult `
        -Result $result `
        -MutationPerformed $true `
        -StopRequired ($recoveryErrors.Count -ne 0) `
        -Detail ([ordered]@{
            errors = $recoveryErrors
            expectedExplorerProcessId = $ExpectedExplorerProcessId
            actualExplorerProcessId = $actualExplorerProcessId
            explorerProcessIdStable =
                $ExpectedExplorerProcessId -gt 0 -and
                $actualExplorerProcessId -eq $ExpectedExplorerProcessId
            targetDisabled = $targetDisabled
            serviceState = [string]$service.State
            serviceStartMode = [string]$service.StartMode
            serviceProcessId = [int]$service.ProcessId
            killSwitchArmed =
                (Test-Path -LiteralPath $killSwitchPath -PathType Leaf)
            permitPresent =
                (Test-Path -LiteralPath $permitPath)
            targetExplorerMappingCount = $targetMappings.Count
            allWindhawkAndJarvisMappings = $runtimeMappings
            explorerRestartRequested = $false
            forceStopRequested = $false
        })
}

try {
    $result = switch ($Action) {
        'Inspect' {
            Invoke-InspectAction
            break
        }
        'UpdateDisabledInstallation' {
            Invoke-UpdateDisabledInstallationAction
            break
        }
        'StartDisabledHost' {
            Invoke-StartDisabledHostAction
            break
        }
        'EnableOnce' {
            Invoke-EnableOnceAction
            break
        }
        'Observe' {
            Invoke-ObserveAction
            break
        }
        'Recover' {
            Invoke-RecoverAction
            break
        }
    }
    Publish-ControllerResult -Result $result
}
catch {
    $failure = New-ControllerResult `
        -Result 'failed' `
        -MutationPerformed $script:mutationPerformed `
        -StopRequired $script:stopRequired `
        -Detail ([ordered]@{
            error = $_.Exception.Message
            exceptionType = $_.Exception.GetType().FullName
        })
    Publish-ControllerResult -Result $failure
    throw
}
