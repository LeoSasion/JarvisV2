[CmdletBinding()]
param(
    [switch]$SkipManagedBuild,
    [string]$DotnetPath = 'dotnet',
    [string]$NodePath = 'node'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$stylerPath = Join-Path $root 'mods\windows11\jarvis-native-taskbar.wh.cpp'
$iconSizePath = Join-Path $root 'mods\windows11\jarvis-taskbar-icon-size.wh.cpp'
$compatibilityPath = Join-Path $root 'config\compatibility.json'
$upstreamLockPath = Join-Path $root 'config\upstream-lock.json'
$toolchainLockPath = Join-Path $root 'config\toolchain-lock.json'
$licensePath = Join-Path $root 'LICENSE'
$supervisorProject = Join-Path $root 'src\platforms\windows11\Jarvis.Supervisor\Jarvis.Supervisor.csproj'
$supervisorSourcePath = Join-Path $root 'src\platforms\windows11\Jarvis.Supervisor\CompatibilityInspector.cs'
$explorerHostModelProject =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerHostModel\Jarvis.ExplorerHostModel.csproj'
$explorerHostModelSourceRoot =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerHostModel'
$explorerHostModelAuditPath =
    Join-Path $root 'scripts\Test-ExplorerHostModel.ps1'
$explorerHostPlanSchemaPath =
    Join-Path $root 'config\explorer-host-offline-plan.schema.json'
$controlCenterProject =
    Join-Path $root 'src\common\Jarvis.ControlCenter\Jarvis.ControlCenter.csproj'
$controlCenterSourceRoot =
    Join-Path $root 'src\common\Jarvis.ControlCenter'
$controlCenterAuditPath =
    Join-Path $root 'scripts\Test-ControlCenter.ps1'
$piAgentHostConversationStatePath =
    Join-Path $root 'src\common\Jarvis.PiAgentHost\ConversationState.cs'
$nativeStyleLabProject =
    Join-Path $root 'src\platforms\windows11\Jarvis.NativeStyleLab\Jarvis.NativeStyleLab.csproj'
$nativeStyleLabSourceRoot =
    Join-Path $root 'src\platforms\windows11\Jarvis.NativeStyleLab'
$nativeStyleLabAuditPath =
    Join-Path $root 'scripts\Test-NativeStyleLab.ps1'
$desktopStyleProbeProject =
    Join-Path $root 'src\common\Jarvis.DesktopStyleProbe\Jarvis.DesktopStyleProbe.csproj'
$desktopStyleProbeSourceRoot =
    Join-Path $root 'src\common\Jarvis.DesktopStyleProbe'
$desktopStyleProbeAuditPath =
    Join-Path $root 'scripts\Test-DesktopStyleProbe.ps1'
$desktopStyleSessionProject =
    Join-Path $root 'src\common\Jarvis.DesktopStyleSession\Jarvis.DesktopStyleSession.csproj'
$desktopStyleSessionSourceRoot =
    Join-Path $root 'src\common\Jarvis.DesktopStyleSession'
$desktopStyleSessionAuditPath =
    Join-Path $root 'scripts\Test-DesktopStyleSession.ps1'
$nativeWindowStyleSessionProject =
    Join-Path $root 'src\platforms\windows11\Jarvis.NativeWindowStyleSession\Jarvis.NativeWindowStyleSession.csproj'
$nativeWindowStyleSessionSourceRoot =
    Join-Path $root 'src\platforms\windows11\Jarvis.NativeWindowStyleSession'
$nativeWindowStyleSessionAuditPath =
    Join-Path $root 'scripts\Test-NativeWindowStyleSession.ps1'
$explorerBridgeSourceRoot =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerBridgeModel'
$explorerBridgeHarnessPath =
    Join-Path $root 'tests\native\windows11\jarvis_explorer_bridge_model_harness.cpp'
$explorerBridgeAuditPath =
    Join-Path $root 'scripts\Test-ExplorerBridgeModel.ps1'
$explorerFrameModelProject =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerFrameModel\Jarvis.ExplorerFrameModel.csproj'
$explorerFrameModelSourceRoot =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerFrameModel'
$explorerFrameModelAuditPath =
    Join-Path $root 'scripts\Test-ExplorerFrameModel.ps1'
$explorerPreviewModelProject =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerPreviewModel\Jarvis.ExplorerPreviewModel.csproj'
$explorerPreviewModelSourceRoot =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerPreviewModel'
$explorerPreviewModelAuditPath =
    Join-Path $root 'scripts\Test-ExplorerPreviewModel.ps1'
$explorerSurfaceProbeProject =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerSurfaceProbe\Jarvis.ExplorerSurfaceProbe.csproj'
$explorerSurfaceProbeSourceRoot =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerSurfaceProbe'
$explorerSurfaceProbeAuditPath =
    Join-Path $root 'scripts\Test-ExplorerSurfaceProbe.ps1'
$explorerTransportModelSourceRoot =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerTransportModel'
$explorerTransportModelHarnessPath =
    Join-Path $root 'tests\native\windows11\jarvis_explorer_transport_model_harness.cpp'
$explorerTransportModelAuditPath =
    Join-Path $root 'scripts\Test-ExplorerTransportModel.ps1'
$explorerReadOnlyTapSourceRoot =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerTapReadOnly'
$explorerReadOnlyTapHarnessPath =
    Join-Path $root 'tests\native\windows11\jarvis_explorer_tap_protocol_harness.cpp'
$explorerReadOnlyTapAuditPath =
    Join-Path $root 'scripts\Test-ExplorerReadOnlyTap.ps1'
$explorerReadOnlyAdmissionHarnessPath =
    Join-Path $root 'tests\native\windows11\jarvis_explorer_tap_admission_harness.cpp'
$explorerReadOnlyAdmissionAuditPath =
    Join-Path $root 'scripts\Test-ExplorerReadOnlyAdmission.ps1'
$explorerInspectableAdapterHarnessPath =
    Join-Path $root 'tests\native\windows11\jarvis_explorer_tap_inspectable_adapter_harness.cpp'
$explorerInspectableAdapterAuditPath =
    Join-Path $root 'scripts\Test-ExplorerInspectableAdapter.ps1'
$explorerStyleTransactionHarnessPath =
    Join-Path $root 'tests\native\windows11\jarvis_explorer_tap_style_transaction_harness.cpp'
$explorerStyleTransactionAuditPath =
    Join-Path $root 'scripts\Test-ExplorerStyleTransaction.ps1'
$explorerXamlReadBridgeHarnessPath =
    Join-Path $root 'tests\native\windows11\jarvis_explorer_tap_xaml_read_bridge_harness.cpp'
$explorerXamlReadBridgeAuditPath =
    Join-Path $root 'scripts\Test-ExplorerXamlReadBridge.ps1'
$explorerXamlSurfaceDiscoveryHarnessPath =
    Join-Path $root 'tests\native\windows11\jarvis_explorer_tap_surface_discovery_harness.cpp'
$explorerXamlSurfaceDiscoveryAuditPath =
    Join-Path $root 'scripts\Test-ExplorerXamlSurfaceDiscovery.ps1'
$buildScriptPath = Join-Path $root 'scripts\Build-NativeMod.ps1'
$testScriptPath = $PSCommandPath
$artifactsRoot = Join-Path $root 'artifacts\native'
$nativeBuildReceiptPath = Join-Path $root 'docs\receipts\native-build-2026-07-22.json'
$safetyContractPath = Join-Path $root 'AGENTS.md'
$recoveryPath = Join-Path $root 'docs\RECOVERY.md'
$phase2TaskPath = Join-Path $root 'docs\PHASE-2-OFFLINE-LIFECYCLE-TASK.md'
$phase2ReceiptSchemaPath =
    Join-Path $root 'config\offline-lifecycle-receipt.schema.json'
$phase2ProtocolPath =
    Join-Path $root 'mods\common\jarvis-resource-protocol.hpp'
$phase2HarnessPath =
    Join-Path $root 'tests\native\common\jarvis_lifecycle_harness.cpp'
$phase2FaultRunnerPath =
    Join-Path $root 'scripts\Test-LifecycleFaultLab.ps1'
$phase2FaultReceiptPath =
    Join-Path $root 'artifacts\lifecycle-fault-lab\latest.json'
$phase3TaskPath =
    Join-Path $root 'docs\PHASE-3-OPEN-SOURCE-AND-M2-LIVE-PREP-TASK.md'
$publicationManifestPath =
    Join-Path $root 'config\publication-manifest.json'
$publicationScriptPath =
    Join-Path $root 'scripts\Test-PublicationBoundary.ps1'
$m2ReadinessSchemaPath =
    Join-Path $root 'config\m2-live-readiness-receipt.schema.json'
$m2ReadinessScriptPath =
    Join-Path $root 'scripts\Test-M2LiveReadiness.ps1'
$m2BaselineScriptPath =
    Join-Path $root 'scripts\Measure-M2HostBaseline.ps1'
$m2RunbookPath =
    Join-Path $root 'docs\M2-CONTROLLED-LIVE-VALIDATION-RUNBOOK.md'
$m2ChecklistPath =
    Join-Path $root 'docs\M2-INTERACTION-CHECKLIST.md'
$publicCiPath =
    Join-Path $root '.github\workflows\ci.yml'
$phase4TaskPath =
    Join-Path $root 'docs\PHASE-4-M2-RECOVERY-AND-OBSERVATION-TASK.md'
$m2SessionPlanSchemaPath =
    Join-Path $root 'config\m2-validation-session-plan.schema.json'
$m2ObservationSchemaPath =
    Join-Path $root 'config\m2-observation-rehearsal-receipt.schema.json'
$m2SessionPlannerPath =
    Join-Path $root 'scripts\New-M2ValidationSessionPlan.ps1'
$m2RecoveryTerminalPath =
    Join-Path $root 'scripts\Open-M2RecoveryTerminal.ps1'
$m2ObservationRehearsalPath =
    Join-Path $root 'scripts\Test-M2ObservationRehearsal.ps1'
$phase5TaskPath =
    Join-Path $root 'docs\PHASE-5-M2-RECOVERY-LEASE-TASK.md'
$phase5ReviewPath =
    Join-Path $root 'docs\PHASE-5-SAFETY-REVIEW.md'
$phase6TaskPath =
    Join-Path $root 'docs\PHASE-6-WINDHAWK-HOST-QUARANTINE-TASK.md'
$phase6AdrPath =
    Join-Path $root 'docs\ADR-0001-EXPLORER-ONLY-HOST.md'
$phase7TaskPath =
    Join-Path $root 'docs\PHASE-7-CONTROL-CENTER-AND-BRIDGE-CONTRACT-TASK.md'
$phase8TaskPath =
    Join-Path $root 'docs\PHASE-8-NATIVE-STYLE-LAB-AND-DESKTOP-PROBE-TASK.md'
$phase8DesktopSessionTaskPath =
    Join-Path $root 'docs\PHASE-8-DESKTOP-TEXT-COLOR-SESSION-TASK.md'
$phase8NativeWindowSessionTaskPath =
    Join-Path $root 'docs\PHASE-8-NATIVE-EXPLORER-WINDOW-STYLE-TASK.md'
$phase9TaskPath =
    Join-Path $root 'docs\PHASE-9-EXPLORER-FRAME-STYLER-TASK.md'
$phase10TaskPath =
    Join-Path $root 'docs\PHASE-10-BATCHED-EXPLORER-PREVIEW-PREP-TASK.md'
$phase11TaskPath =
    Join-Path $root 'docs\PHASE-11-EXPLORER-XAML-TRANSPORT-CORE-TASK.md'
$phase12TaskPath =
    Join-Path $root 'docs\PHASE-12-EXPLORER-READONLY-TAP-OFFLINE-BUILD-TASK.md'
$phase13TaskPath =
    Join-Path $root 'docs\PHASE-13-EXPLORER-READONLY-ADMISSION-AND-FINGERPRINT-TASK.md'
$phase14TaskPath =
    Join-Path $root 'docs\PHASE-14-EXPLORER-INSPECTABLE-ADAPTER-TASK.md'
$phase15TaskPath =
    Join-Path $root 'docs\PHASE-15-EXPLORER-REVERSIBLE-STYLE-TRANSACTION-TASK.md'
$phase16TaskPath =
    Join-Path $root 'docs\PHASE-16-EXPLORER-XAML-READ-BRIDGE-REVIEW-TASK.md'
$phase17TaskPath =
    Join-Path $root 'docs\PHASE-17-EXPLORER-XAML-SURFACE-DISCOVERY-TASK.md'
$explorerFrameSelectorProfilePath =
    Join-Path $root 'config\explorer-frame-selector-candidate.json'
$explorerFrameSelectorSchemaPath =
    Join-Path $root 'config\explorer-frame-selector-candidate.schema.json'
$explorerTransportContractPath =
    Join-Path $root 'config\explorer-xaml-transport-contract.json'
$explorerTransportContractSchemaPath =
    Join-Path $root 'config\explorer-xaml-transport-contract.schema.json'
$explorerReadOnlyTapContractPath =
    Join-Path $root 'config\explorer-readonly-tap-build-contract.json'
$explorerReadOnlyTapContractSchemaPath =
    Join-Path $root 'config\explorer-readonly-tap-build-contract.schema.json'
$explorerReadOnlyAdmissionContractPath =
    Join-Path $root 'config\explorer-readonly-admission-fingerprint-contract.json'
$explorerReadOnlyAdmissionContractSchemaPath =
    Join-Path $root 'config\explorer-readonly-admission-fingerprint-contract.schema.json'
$explorerInspectableAdapterContractPath =
    Join-Path $root 'config\explorer-inspectable-adapter-contract.json'
$explorerInspectableAdapterContractSchemaPath =
    Join-Path $root 'config\explorer-inspectable-adapter-contract.schema.json'
$explorerStyleTransactionContractPath =
    Join-Path $root 'config\explorer-style-transaction-contract.json'
$explorerStyleTransactionContractSchemaPath =
    Join-Path $root 'config\explorer-style-transaction-contract.schema.json'
$explorerXamlReadBridgeContractPath =
    Join-Path $root 'config\explorer-xaml-read-bridge-contract.json'
$explorerXamlReadBridgeContractSchemaPath =
    Join-Path $root 'config\explorer-xaml-read-bridge-contract.schema.json'
$explorerXamlSurfaceDiscoveryContractPath =
    Join-Path $root 'config\explorer-xaml-surface-discovery-contract.json'
$explorerXamlSurfaceDiscoveryContractSchemaPath =
    Join-Path $root 'config\explorer-xaml-surface-discovery-contract.schema.json'
$m2RecoveryLeaseSchemaPath =
    Join-Path $root 'config\m2-recovery-terminal-lease.schema.json'
$m2RecoveryLeaseLabSchemaPath =
    Join-Path $root 'config\m2-recovery-lease-lab-receipt.schema.json'
$m2RecoveryLeaseLabPath =
    Join-Path $root 'scripts\Test-M2RecoveryLeaseLab.ps1'
$recoveryTerminalLeaseSourcePath =
    Join-Path $root 'src\platforms\windows11\Jarvis.Supervisor\RecoveryTerminalLease.cs'
$killSwitchSourcePath =
    Join-Path $root 'src\platforms\windows11\Jarvis.Supervisor\KillSwitch.cs'
$programSourcePath =
    Join-Path $root 'src\platforms\windows11\Jarvis.Supervisor\Program.cs'

$checks = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()

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

function Test-Pattern {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Text,
        [Parameter(Mandatory)] [string]$Pattern,
        [Parameter(Mandatory)] [string]$Detail
    )

    Add-Check -Name $Name -Passed ([regex]::IsMatch($Text, $Pattern)) -Detail $Detail
}

function Test-NoPattern {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Text,
        [Parameter(Mandatory)] [string]$Pattern,
        [Parameter(Mandatory)] [string]$Detail
    )

    Add-Check -Name $Name -Passed (-not [regex]::IsMatch($Text, $Pattern)) -Detail $Detail
}

function Get-SourceSlice {
    param(
        [Parameter(Mandatory)] [string]$Text,
        [Parameter(Mandatory)] [string]$StartMarker,
        [Parameter(Mandatory)] [string]$EndMarker,
        [switch]$UseLastStart
    )

    $startIndex = if ($UseLastStart) {
        $Text.LastIndexOf($StartMarker, [StringComparison]::Ordinal)
    }
    else {
        $Text.IndexOf($StartMarker, [StringComparison]::Ordinal)
    }

    if ($startIndex -lt 0) {
        return ''
    }

    $endIndex = $Text.IndexOf(
        $EndMarker,
        $startIndex + $StartMarker.Length,
        [StringComparison]::Ordinal
    )
    if ($endIndex -lt 0) {
        return $Text.Substring($startIndex)
    }

    return $Text.Substring($startIndex, $endIndex - $startIndex)
}

function Test-MarkersInOrder {
    param(
        [Parameter(Mandatory)] [string]$Text,
        [Parameter(Mandatory)] [string[]]$Markers
    )

    $offset = 0
    foreach ($marker in $Markers) {
        $index = $Text.IndexOf(
            $marker,
            $offset,
            [StringComparison]::Ordinal
        )
        if ($index -lt 0) {
            return $false
        }
        $offset = $index + $marker.Length
    }
    return $true
}

function Get-NormalizedJson {
    param([Parameter(Mandatory)] [object]$Value)
    return $Value | ConvertTo-Json -Depth 12 -Compress
}

function Assert-NoReparsePointsInPath {
    param([Parameter(Mandatory)] [string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)
    $current = $pathRoot
    foreach ($segment in $fullPath.Substring($pathRoot.Length).Split(@('\', '/'), [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current)) {
            continue
        }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Evidence path contains a reparse point: $($item.FullName)"
        }
    }
}

function Resolve-EvidencePath {
    param(
        [Parameter(Mandatory)] [string]$RelativePath,
        [Parameter(Mandatory)] [string]$AllowedRoot
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "Evidence path must be repository-relative: $RelativePath"
    }
    $fullAllowedRoot = [System.IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\')
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $root $RelativePath.Replace('/', '\')))
    if (-not $fullPath.StartsWith($fullAllowedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Evidence path escapes its allowed root: $RelativePath"
    }
    $null = Assert-NoReparsePointsInPath -Path $fullPath
    return $fullPath
}

function Test-EvidenceFile {
    param(
        [Parameter(Mandatory)] [object]$Descriptor,
        [Parameter(Mandatory)] [string]$AllowedRoot
    )

    try {
        $path = Resolve-EvidencePath -RelativePath ([string]$Descriptor.relativePath) -AllowedRoot $AllowedRoot
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Evidence file is missing: $path"
        }
        $item = Get-Item -LiteralPath $path -Force
        if ([int64]$item.Length -ne [int64]$Descriptor.size) {
            throw "Evidence size mismatch for $path."
        }
        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if (-not $actualHash.Equals([string]$Descriptor.sha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Evidence SHA-256 mismatch for $path."
        }
        return [pscustomobject]@{ passed = $true; path = $path; detail = "$path ($actualHash)" }
    }
    catch {
        return [pscustomobject]@{ passed = $false; path = $null; detail = $_.Exception.Message }
    }
}

$compatibility = Get-Content -LiteralPath $compatibilityPath -Raw | ConvertFrom-Json
$upstreamLock = Get-Content -LiteralPath $upstreamLockPath -Raw | ConvertFrom-Json
$toolchainLock = Get-Content -LiteralPath $toolchainLockPath -Raw | ConvertFrom-Json
$styler = [System.IO.File]::ReadAllText($stylerPath)
$iconSize = [System.IO.File]::ReadAllText($iconSizePath)
$license = [System.IO.File]::ReadAllText($licensePath)
$supervisorSource = [System.IO.File]::ReadAllText($supervisorSourcePath)
$buildScript = [System.IO.File]::ReadAllText($buildScriptPath)
$buildTokens = $null
$buildParseErrors = $null
$buildAst = [System.Management.Automation.Language.Parser]::ParseInput(
    $buildScript,
    [ref]$buildTokens,
    [ref]$buildParseErrors
)
$nativeBuildReceipt = Get-Content -LiteralPath $nativeBuildReceiptPath -Raw | ConvertFrom-Json
$safetyContract = [System.IO.File]::ReadAllText($safetyContractPath)
$recovery = [System.IO.File]::ReadAllText($recoveryPath)
$phase2TaskExists = Test-Path -LiteralPath $phase2TaskPath -PathType Leaf
$phase2ReceiptSchemaExists =
    Test-Path -LiteralPath $phase2ReceiptSchemaPath -PathType Leaf
$phase2Task = if ($phase2TaskExists) {
    [System.IO.File]::ReadAllText($phase2TaskPath)
}
else {
    ''
}
$phase2ReceiptSchema = if ($phase2ReceiptSchemaExists) {
    Get-Content -LiteralPath $phase2ReceiptSchemaPath -Raw |
        ConvertFrom-Json
}
else {
    $null
}
$phase2Protocol = [System.IO.File]::ReadAllText(
    $phase2ProtocolPath
)
$phase2Harness = [System.IO.File]::ReadAllText(
    $phase2HarnessPath
)
$phase2FaultRunner = [System.IO.File]::ReadAllText(
    $phase2FaultRunnerPath
)
$phase3Task = [System.IO.File]::ReadAllText($phase3TaskPath)
$publicationManifest =
    Get-Content -LiteralPath $publicationManifestPath -Raw |
        ConvertFrom-Json -Depth 100
$publicationScript =
    [System.IO.File]::ReadAllText($publicationScriptPath)
$m2ReadinessSchema =
    Get-Content -LiteralPath $m2ReadinessSchemaPath -Raw |
        ConvertFrom-Json -Depth 100
$m2ReadinessScript =
    [System.IO.File]::ReadAllText($m2ReadinessScriptPath)
$m2BaselineScript =
    [System.IO.File]::ReadAllText($m2BaselineScriptPath)
$m2Runbook = [System.IO.File]::ReadAllText($m2RunbookPath)
$m2Checklist = [System.IO.File]::ReadAllText($m2ChecklistPath)
$publicCi = [System.IO.File]::ReadAllText($publicCiPath)
$phase4Task = [System.IO.File]::ReadAllText($phase4TaskPath)
$m2SessionPlanSchema =
    Get-Content -LiteralPath $m2SessionPlanSchemaPath -Raw |
        ConvertFrom-Json -Depth 100
$m2ObservationSchema =
    Get-Content -LiteralPath $m2ObservationSchemaPath -Raw |
        ConvertFrom-Json -Depth 100
$m2SessionPlanner =
    [System.IO.File]::ReadAllText($m2SessionPlannerPath)
$m2RecoveryTerminal =
    [System.IO.File]::ReadAllText($m2RecoveryTerminalPath)
$m2ObservationRehearsal =
    [System.IO.File]::ReadAllText($m2ObservationRehearsalPath)
$phase5Task = [System.IO.File]::ReadAllText($phase5TaskPath)
$phase5Review = [System.IO.File]::ReadAllText($phase5ReviewPath)
$m2RecoveryLeaseSchema =
    Get-Content -LiteralPath $m2RecoveryLeaseSchemaPath -Raw |
        ConvertFrom-Json -Depth 100
$m2RecoveryLeaseLabSchema =
    Get-Content -LiteralPath $m2RecoveryLeaseLabSchemaPath -Raw |
        ConvertFrom-Json -Depth 100
$m2RecoveryLeaseLab =
    [System.IO.File]::ReadAllText($m2RecoveryLeaseLabPath)
$recoveryTerminalLeaseSource =
    [System.IO.File]::ReadAllText($recoveryTerminalLeaseSourcePath)
$killSwitchSource =
    [System.IO.File]::ReadAllText($killSwitchSourcePath)
$programSource =
    [System.IO.File]::ReadAllText($programSourcePath)
$phase6Task = [System.IO.File]::ReadAllText($phase6TaskPath)
$phase6Adr = [System.IO.File]::ReadAllText($phase6AdrPath)
$explorerHostModelSource = @(
    Get-ChildItem `
        -LiteralPath $explorerHostModelSourceRoot `
        -Filter '*.cs' `
        -File |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$explorerHostPlanSchema =
    Get-Content -LiteralPath $explorerHostPlanSchemaPath -Raw |
        ConvertFrom-Json -Depth 100
$phase7Task = [System.IO.File]::ReadAllText($phase7TaskPath)
$controlCenterMainWindowSource = [System.IO.File]::ReadAllText(
    (Join-Path $controlCenterSourceRoot 'MainWindow.xaml'))
$piAgentHostConversationStateSource = [System.IO.File]::ReadAllText(
    $piAgentHostConversationStatePath)
$controlCenterSource = @(
    Get-ChildItem -LiteralPath $controlCenterSourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.xaml', '.csproj') |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$explorerBridgeSource = @(
    Get-ChildItem -LiteralPath $explorerBridgeSourceRoot -File -Recurse |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
    [IO.File]::ReadAllText($explorerBridgeHarnessPath)
) -join [Environment]::NewLine
$explorerFrameModelSource = @(
    Get-ChildItem `
        -LiteralPath $explorerFrameModelSourceRoot `
        -Filter '*.cs' `
        -File |
        Sort-Object Name |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$phase8Task = [System.IO.File]::ReadAllText($phase8TaskPath)
$nativeStyleLabSource = @(
    Get-ChildItem -LiteralPath $nativeStyleLabSourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.xaml', '.csproj') |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$desktopStyleProbeSource = @(
    Get-ChildItem -LiteralPath $desktopStyleProbeSourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.csproj') |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$phase8DesktopSessionTask =
    [System.IO.File]::ReadAllText($phase8DesktopSessionTaskPath)
$desktopStyleSessionSource = @(
    Get-ChildItem -LiteralPath $desktopStyleSessionSourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.csproj') |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$phase8NativeWindowSessionTask =
    [System.IO.File]::ReadAllText($phase8NativeWindowSessionTaskPath)
$nativeWindowStyleSessionSource = @(
    Get-ChildItem -LiteralPath $nativeWindowStyleSessionSourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.csproj') |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$phase9Task = [System.IO.File]::ReadAllText($phase9TaskPath)
$phase10Task = [System.IO.File]::ReadAllText($phase10TaskPath)
$phase11Task = [System.IO.File]::ReadAllText($phase11TaskPath)
$phase12Task = [System.IO.File]::ReadAllText($phase12TaskPath)
$phase13Task = [System.IO.File]::ReadAllText($phase13TaskPath)
$phase14Task = [System.IO.File]::ReadAllText($phase14TaskPath)
$phase15Task = [System.IO.File]::ReadAllText($phase15TaskPath)
$phase16Task = [System.IO.File]::ReadAllText($phase16TaskPath)
$phase17Task = [System.IO.File]::ReadAllText($phase17TaskPath)
$explorerFrameSelectorProfile =
    Get-Content -LiteralPath $explorerFrameSelectorProfilePath -Raw |
        ConvertFrom-Json -Depth 100
$explorerFrameSelectorSchema =
    Get-Content -LiteralPath $explorerFrameSelectorSchemaPath -Raw |
        ConvertFrom-Json -Depth 100
$explorerPreviewModelSource = @(
    Get-ChildItem `
        -LiteralPath $explorerPreviewModelSourceRoot `
        -Filter '*.cs' `
        -File |
        Sort-Object Name |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$explorerSurfaceProbeSource = @(
    Get-ChildItem `
        -LiteralPath $explorerSurfaceProbeSourceRoot `
        -File `
        -Recurse |
        Where-Object Extension -In @('.cs', '.csproj') |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$explorerTransportContract =
    Get-Content -LiteralPath $explorerTransportContractPath -Raw |
        ConvertFrom-Json -Depth 100
$explorerTransportContractSchema =
    Get-Content -LiteralPath $explorerTransportContractSchemaPath -Raw |
        ConvertFrom-Json -Depth 100
$explorerTransportModelSource = @(
    Get-ChildItem `
        -LiteralPath $explorerTransportModelSourceRoot `
        -File `
        -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
    [IO.File]::ReadAllText($explorerTransportModelHarnessPath)
) -join [Environment]::NewLine
$explorerReadOnlyTapContract =
    Get-Content -LiteralPath $explorerReadOnlyTapContractPath -Raw |
        ConvertFrom-Json -Depth 100
$explorerReadOnlyTapContractSchema =
    Get-Content -LiteralPath $explorerReadOnlyTapContractSchemaPath -Raw |
        ConvertFrom-Json -Depth 100
$explorerReadOnlyTapSource = @(
    Get-ChildItem `
        -LiteralPath $explorerReadOnlyTapSourceRoot `
        -File `
        -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
    [IO.File]::ReadAllText($explorerReadOnlyTapHarnessPath)
) -join [Environment]::NewLine
$explorerReadOnlyAdmissionContract =
    Get-Content -LiteralPath $explorerReadOnlyAdmissionContractPath -Raw |
        ConvertFrom-Json -Depth 100
$explorerReadOnlyAdmissionContractSchema =
    Get-Content `
        -LiteralPath $explorerReadOnlyAdmissionContractSchemaPath `
        -Raw |
        ConvertFrom-Json -Depth 100
$explorerReadOnlyAdmissionSource = @(
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_admission.h')
    )
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_admission.cpp')
    )
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_fingerprint.h')
    )
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_fingerprint.cpp')
    )
    [IO.File]::ReadAllText($explorerReadOnlyAdmissionHarnessPath)
) -join [Environment]::NewLine
$explorerInspectableAdapterContract =
    Get-Content -LiteralPath $explorerInspectableAdapterContractPath -Raw |
        ConvertFrom-Json -Depth 100
$explorerInspectableAdapterContractSchema =
    Get-Content `
        -LiteralPath $explorerInspectableAdapterContractSchemaPath `
        -Raw |
        ConvertFrom-Json -Depth 100
$explorerInspectableAdapterSource = @(
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_inspectable_adapter.h')
    )
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_inspectable_adapter.cpp')
    )
    [IO.File]::ReadAllText($explorerInspectableAdapterHarnessPath)
) -join [Environment]::NewLine
$explorerStyleTransactionContract =
    Get-Content -LiteralPath $explorerStyleTransactionContractPath -Raw |
        ConvertFrom-Json -Depth 100
$explorerStyleTransactionContractSchema =
    Get-Content `
        -LiteralPath $explorerStyleTransactionContractSchemaPath `
        -Raw |
        ConvertFrom-Json -Depth 100
$explorerStyleTransactionSource = @(
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_style_transaction.h')
    )
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_style_transaction.cpp')
    )
    [IO.File]::ReadAllText($explorerStyleTransactionHarnessPath)
) -join [Environment]::NewLine
$explorerXamlReadBridgeContract =
    Get-Content -LiteralPath $explorerXamlReadBridgeContractPath -Raw |
        ConvertFrom-Json -Depth 100
$explorerXamlReadBridgeContractSchema =
    Get-Content `
        -LiteralPath $explorerXamlReadBridgeContractSchemaPath `
        -Raw |
        ConvertFrom-Json -Depth 100
$explorerXamlReadBridgeSource = @(
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_xaml_read_bridge.h')
    )
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_xaml_read_bridge_policy.cpp')
    )
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_xaml_read_bridge_windows.cpp')
    )
    [IO.File]::ReadAllText($explorerXamlReadBridgeHarnessPath)
) -join [Environment]::NewLine
$explorerXamlSurfaceDiscoveryContract =
    Get-Content `
        -LiteralPath $explorerXamlSurfaceDiscoveryContractPath `
        -Raw |
        ConvertFrom-Json -Depth 100
$explorerXamlSurfaceDiscoveryContractSchema =
    Get-Content `
        -LiteralPath $explorerXamlSurfaceDiscoveryContractSchemaPath `
        -Raw |
        ConvertFrom-Json -Depth 100
$explorerXamlSurfaceDiscoverySource = @(
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_surface_discovery.h')
    )
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_surface_discovery.cpp')
    )
    [IO.File]::ReadAllText(
        (Join-Path $explorerReadOnlyTapSourceRoot 'jarvis_explorer_tap_surface_discovery_windows.cpp')
    )
    [IO.File]::ReadAllText($explorerXamlSurfaceDiscoveryHarnessPath)
) -join [Environment]::NewLine
$readme = [System.IO.File]::ReadAllText((Join-Path $root 'README.md'))
$baseline = $compatibility.validatedHosts[0]
$phase2ExpectedSourceIdentity = [ordered]@{
    m1Source = [pscustomobject]@{
        relativePath = 'mods/windows11/jarvis-native-taskbar.wh.cpp'
        fullPath = $stylerPath
    }
    protocolHeader = [pscustomobject]@{
        relativePath = 'mods/common/jarvis-resource-protocol.hpp'
        fullPath = $phase2ProtocolPath
    }
    labSource = [pscustomobject]@{
        relativePath = 'tests/native/common/jarvis_lifecycle_harness.cpp'
        fullPath = $phase2HarnessPath
    }
    runnerScript = [pscustomobject]@{
        relativePath = 'scripts/Test-LifecycleFaultLab.ps1'
        fullPath = $phase2FaultRunnerPath
    }
    receiptSchema = [pscustomobject]@{
        relativePath = 'config/offline-lifecycle-receipt.schema.json'
        fullPath = $phase2ReceiptSchemaPath
    }
    testProject = [pscustomobject]@{
        relativePath = 'scripts/Test-Project.ps1'
        fullPath = $testScriptPath
    }
}

Add-Check 'license.full-gpl-text' ($license.Contains('GNU GENERAL PUBLIC LICENSE') -and $license.Contains('Version 3, 29 June 2007')) 'LICENSE must contain the complete GPL v3 text.'
Add-Check 'phase2.task-present' $phase2TaskExists 'The Phase 2 offline lifecycle task must remain in the repository.'
$phase2TaskSafetyContract =
    $phase2Task.Contains('Live activation: **FORBIDDEN**') -and
    $phase2Task.Contains('不创建或消费 `active-module.txt`') -and
    $phase2Task.Contains('M1 保持 `build-only`') -and
    $phase2Task.Contains('不启动、安装、启用或配置 Windhawk') -and
    $phase2Task.Contains('不注入模块，不终止或重启 Explorer') -and
    $phase2Task.Contains('任何 live activation 必须在另一个任务中')
Add-Check 'phase2.task-safety-contract' $phase2TaskSafetyContract 'The Phase 2 task must retain every offline-only and no-live-activation boundary.'
$phase2EvidenceBoundary =
    $phase2Task.Contains('offlineEvidenceReady') -and
    $phase2Task.Contains('releaseReady=false') -and
    $phase2Task.Contains('activationPermitted=false') -and
    $phase2Task.Contains('liveExplorer=not-run') -and
    $phase2Task.Contains('静态检查冒充实机证明')
Add-Check 'phase2.task-evidence-boundary' $phase2EvidenceBoundary 'The task must keep offline evidence, release, activation and live Explorer claims distinct.'
Add-Check 'phase2.receipt-schema-present' $phase2ReceiptSchemaExists 'The Phase 2 receipt must have a committed machine-readable JSON schema.'
$phase2SchemaContract =
    $null -ne $phase2ReceiptSchema -and
    $phase2ReceiptSchema.properties.schemaVersion.const -eq 1 -and
    $phase2ReceiptSchema.properties.receiptType.const -eq
        'jarvis2-offline-lifecycle-fault-lab' -and
    $phase2ReceiptSchema.properties.liveExplorer.const -eq 'not-run' -and
    $phase2ReceiptSchema.properties.releaseReady.const -eq $false -and
    $phase2ReceiptSchema.properties.activationPermitted.const -eq $false -and
    @($phase2ReceiptSchema.required) -contains 'offlineEvidenceReady' -and
    @($phase2ReceiptSchema.required) -contains 'sourceIdentity' -and
    @($phase2ReceiptSchema.required) -contains 'scenarios' -and
    $phase2ReceiptSchema.properties.sourceIdentity.additionalProperties -eq
        $false -and
    @($phase2ReceiptSchema.properties.sourceIdentity.required).Count -eq
        $phase2ExpectedSourceIdentity.Count -and
    @(
        $phase2ExpectedSourceIdentity.Keys |
            Where-Object {
                $_ -notin @(
                    $phase2ReceiptSchema.properties.sourceIdentity.required
                )
            }
    ).Count -eq 0 -and
    @($phase2ReceiptSchema.'$defs'.scenario.required) -contains
        'resourceEvents' -and
    $phase2ReceiptSchema.'$defs'.resourceEvent.additionalProperties -eq
        $false -and
    @($phase2ReceiptSchema.'$defs'.resourceEvent.required).Count -eq 4 -and
    @($phase2ReceiptSchema.'$defs'.resourceEvent.required) -contains
        'resourceId' -and
    @($phase2ReceiptSchema.'$defs'.resourceEvent.required) -contains
        'resourceKind' -and
    @($phase2ReceiptSchema.'$defs'.resourceEvent.required) -contains
        'action' -and
    @($phase2ReceiptSchema.'$defs'.resourceEvent.required) -contains
        'reasonCode' -and
    @($phase2ReceiptSchema.'$defs'.resourceEvent.properties.action.enum).Count -eq
        3 -and
    @(
        @(
            'create',
            'release',
            'retain'
        ) |
            Where-Object {
                $_ -notin @(
                    $phase2ReceiptSchema.'$defs'.resourceEvent.properties.action.enum
                )
            }
    ).Count -eq 0 -and
    @($phase2ReceiptSchema.'$defs'.resourceEvent.properties.reasonCode.enum) -contains
        'none' -and
    @($phase2ReceiptSchema.'$defs'.resourceEvent.properties.reasonCode.enum) -contains
        'external-uncertainty' -and
    @($phase2ReceiptSchema.'$defs'.resourceEvent.properties.reasonCode.enum) -contains
        'protocol-failure' -and
    @($phase2ReceiptSchema.'$defs'.scenario.properties.area.enum) -contains
        'module' -and
    @($phase2ReceiptSchema.allOf).Count -ge 1
Add-Check 'phase2.receipt-schema-v1' $phase2SchemaContract 'Receipt schema v1 must forbid live claims and require source-bound scenario evidence.'

$phase2SchemaSourceIdentityBound = $null -ne $phase2ReceiptSchema
if ($phase2SchemaSourceIdentityBound) {
    $phase2SchemaSourceIdentityProperties = (
        $phase2ReceiptSchema.properties.sourceIdentity.properties
    ).PSObject.Properties
    foreach ($entry in $phase2ExpectedSourceIdentity.GetEnumerator()) {
        $schemaProperty =
            $phase2SchemaSourceIdentityProperties[$entry.Key]
        if ($null -eq $schemaProperty) {
            $phase2SchemaSourceIdentityBound = $false
            continue
        }
        $constClauses = @(
            $schemaProperty.Value.allOf |
                Where-Object {
                    $null -ne $_.PSObject.Properties['properties'] -and
                    $null -ne $_.properties.PSObject.Properties[
                        'relativePath'
                    ] -and
                    $null -ne $_.properties.relativePath.PSObject.Properties[
                        'const'
                    ]
                }
        )
        if ($constClauses.Count -ne 1 -or
            [string]$constClauses[0].properties.relativePath.const -cne
                [string]$entry.Value.relativePath) {
            $phase2SchemaSourceIdentityBound = $false
        }
    }
}
Add-Check `
    'phase2.receipt-schema-keyed-source-identity' `
    $phase2SchemaSourceIdentityBound `
    'Every sourceIdentity key must bind one fixed repository-relative path, including Test-Project.ps1.'

Add-Check `
    'phase3.task-present-and-locked' `
    ((Test-Path -LiteralPath $phase3TaskPath -PathType Leaf) -and
     $phase3Task.Contains('Repository name: **JarvisV2**') -and
     $phase3Task.Contains('Internal runtime namespace: **JARVIS2**') -and
     $phase3Task.Contains('Live activation in this task: **FORBIDDEN**') -and
     $phase3Task.Contains('不创建、消费或修改 `active-module.txt`') -and
     $phase3Task.Contains('不安装、启动、配置或启用 Windhawk') -and
     $phase3Task.Contains('不加载任何模块，不终止或重启 Explorer') -and
     $phase3Task.Contains('M1 继续 `build-only`') -and
     $phase3Task.Contains(
         'clear-kill-switch --module jarvis-taskbar-icon-size --confirm')) `
    'Phase 3 must remain a locked publication/readiness task and preserve exact M2 approval as a separate gate.'

$publicationManifestContract =
    $publicationManifest.schemaVersion -eq 1 -and
    $publicationManifest.repositoryName -eq 'JarvisV2' -and
    $publicationManifest.internalRuntimeNamespace -eq 'JARVIS2' -and
    $publicationManifest.defaultBranch -eq 'main' -and
    $publicationManifest.license -eq 'GPL-3.0' -and
    @($publicationManifest.excludedRoots) -contains 'artifacts/' -and
    @($publicationManifest.excludedRoots) -contains 'tools/' -and
    @($publicationManifest.forbiddenExtensions) -contains '.dll' -and
    @($publicationManifest.forbiddenExtensions) -contains '.exe' -and
    $publicationManifest.publicCi.runsManagedBuild -and
    $publicationManifest.publicCi.runsPublicationBoundary -and
    -not $publicationManifest.publicCi.runsCanonicalNativeBuild
Add-Check `
    'phase3.publication-manifest-boundary' `
    $publicationManifestContract `
    'The public repository manifest must keep generated binaries/toolchains out and distinguish public CI from the canonical native build.'

$publicationScriptContract =
    $publicationScript.Contains(
        'git -C $root ls-files --cached --others --exclude-standard') -and
    $publicationScript.Contains('secretValuesPrinted = $false') -and
    $publicationScript.Contains('maxTrackedFileBytes') -and
    $publicationScript.Contains('candidate-reparse-point') -and
    $publicationScript.Contains('sensitive-pattern:') -and
    -not [regex]::IsMatch(
        $publicationScript,
        '(?i)\b(?:Invoke-WebRequest|Invoke-RestMethod|Start-Process)\b') -and
    -not [regex]::IsMatch(
        $publicationScript,
        '(?im)^\s*(?:&\s*)?git\s+(?:add|commit|push)\b')
Add-Check `
    'phase3.publication-script-readonly' `
    $publicationScriptContract `
    'Publication inspection must derive the Git candidate set, avoid network/process launch and never stage, commit or push.'

$m2ReadinessSchemaContract =
    $m2ReadinessSchema.properties.schemaVersion.const -eq 1 -and
    $m2ReadinessSchema.properties.receiptType.const -eq
        'jarvisv2-m2-live-readiness' -and
    $m2ReadinessSchema.properties.activationPermitted.const -eq $false -and
    $m2ReadinessSchema.properties.liveExplorer.const -eq 'not-run' -and
    $m2ReadinessSchema.properties.mutationPerformed.const -eq $false -and
    $m2ReadinessSchema.properties.requestedModule.const -eq
        'jarvis-taskbar-icon-size' -and
    @($m2ReadinessSchema.required) -contains 'hostActivation' -and
    $m2ReadinessSchema.properties.hostActivation.properties.state.const -eq
        'quarantined' -and
    $m2ReadinessSchema.properties.hostActivation.properties.reason.const -eq
        'windhawk-service-global-runtime-injection-observed-20260727' -and
    $m2ReadinessSchema.properties.hostActivation.properties.activationPermitted.const -eq
        $false -and
    $m2ReadinessSchema.properties.approval.properties.exactCommandApproved.const -eq
        $false -and
    $m2ReadinessSchema.properties.approval.properties.recoveryTerminalAvailable.const -eq
        $false -and
    $m2ReadinessSchema.properties.approval.properties.canExecuteNow.const -eq
        $false -and
    @($m2ReadinessSchema.properties.runtime.required) -contains
        'moduleNotEnumerableProcessCount' -and
    @($m2ReadinessSchema.properties.runtime.required) -contains
        'jarvisModuleMappingCount' -and
    @($m2ReadinessSchema.properties.runtime.required) -contains
        'acceptedBaseRuntimeMappingCount' -and
    @($m2ReadinessSchema.properties.runtime.required) -contains
        'unexpectedWindhawkRuntimeMappingCount' -and
    @($m2ReadinessSchema.properties.runtime.required) -contains
        'safetyRelevantModuleEnumerationErrorCount' -and
    @($m2ReadinessSchema.properties.runtime.required) -contains
        'nonTargetModuleEnumerationErrorCount' -and
    @($m2ReadinessSchema.properties.runtime.required) -contains
        'explorerModuleInspectionSucceeded'
Add-Check `
    'phase3.m2-readiness-schema-failclosed' `
    $m2ReadinessSchemaContract `
    'M2 readiness evidence must be read-only, non-live and incapable of granting execution.'

$m2ReadinessScriptContract =
    $m2ReadinessScript.Contains('& dotnet $supervisorDll inspect') -and
    $m2ReadinessScript.Contains('readyForExactApproval = $passed') -and
    $m2ReadinessScript.Contains('activationPermitted = $false') -and
    $m2ReadinessScript.Contains('mutationPerformed = $false') -and
    $m2ReadinessScript.Contains('canExecuteNow = $false') -and
    $m2ReadinessScript.Contains('explorerModuleInspectionSucceeded') -and
    $m2ReadinessScript.Contains(
        'safetyRelevantModuleEnumerationErrorCount') -and
    $m2ReadinessScript.Contains(
        'nonTargetModuleEnumerationErrorCount') -and
    $m2ReadinessScript.Contains(
        'safety-relevant-process-module-enumeration-incomplete') -and
    $m2ReadinessScript.Contains(
        '0AAD074CAF156200BE7A77E4615F9171CEA884CDE96BAF90397366C28C4F10A1') -and
    $m2ReadinessScript.Contains('jarvis-module-mapped') -and
    $m2ReadinessScript.Contains(
        'unexpected-windhawk-runtime-mapped') -and
    $m2ReadinessScript.Contains(
        "Add-Failure 'windhawk-host-activation-quarantined'") -and
    $m2ReadinessScript.Contains(
        'windhawk-service-global-runtime-injection-observed-20260727') -and
    $m2ReadinessScript.Contains(
        'Refusing to overwrite an existing readiness receipt') -and
    [regex]::Matches(
        $m2ReadinessScript,
        'clear-kill-switch').Count -eq 1 -and
    -not [regex]::IsMatch(
        $m2ReadinessScript,
        '(?im)^\s*&\s*dotnet[^\r\n]*(?:clear-kill-switch|arm-kill-switch|restart-explorer)') -and
    -not [regex]::IsMatch(
        $m2ReadinessScript,
        '(?i)\b(?:Start-Service|Stop-Service|Set-Service|Stop-Process|Restart-Computer|taskkill|sc\.exe)\b') -and
    -not $m2ReadinessScript.Contains('Windhawk\windhawk.exe')
Add-Check `
    'phase3.m2-readiness-script-no-activation' `
    $m2ReadinessScriptContract `
    'The M2 readiness script may inspect and quote the exact future command, but must not activate, mutate services or recover Explorer.'

$m2BaselineContract =
    $m2BaselineScript.Contains('& dotnet $supervisorDll inspect') -and
    $m2BaselineScript.Contains("phase = 'locked-pre-activation'") -and
    $m2BaselineScript.Contains('mutationPerformed = $false') -and
    $m2BaselineScript.Contains('activationPermitted = $false') -and
    $m2BaselineScript.Contains("liveExplorer = 'not-run'") -and
    $m2BaselineScript.Contains(
        'Refusing to overwrite an existing baseline receipt') -and
    -not [regex]::IsMatch(
        $m2BaselineScript,
        '(?im)^\s*&\s*dotnet[^\r\n]*(?:clear-kill-switch|arm-kill-switch|restart-explorer)') -and
    -not [regex]::IsMatch(
        $m2BaselineScript,
        '(?i)\b(?:Start-Service|Stop-Service|Set-Service|Stop-Process|taskkill)\b')
Add-Check `
    'phase3.m2-baseline-readonly' `
    $m2BaselineContract `
    'The M2 host baseline must sample only the verified locked Shell and must not activate or mutate the host.'

$m2RunbookContract =
    $m2Runbook.Contains(
        'QUARANTINED — DO NOT START WINDHAWK OR ACTIVATE') -and
    $m2Runbook.Contains('windhawk-host-activation-quarantined') -and
    $m2Runbook.Contains('exactCommandApproved=false') -and
    $m2Runbook.Contains('recoveryTerminalAvailable=false') -and
    $m2Runbook.Contains('canExecuteNow=false') -and
    $m2Runbook.Contains($m2ReadinessSchema.properties.approval.properties.exactCommand.const) -and
    $m2Runbook.Contains(
        '急停是**加载互锁和运行时静默请求**，不是结束进程的按钮。') -and
    [regex]::IsMatch(
        $m2Checklist,
        'Every row\s+starts as \*\*not run\*\*') -and
    $m2Checklist.Contains(
        'Do not continue the matrix after the first unexplained failure.')
Add-Check `
    'phase3.m2-human-gate-and-stop-matrix' `
    $m2RunbookContract `
    'The runbook and checklist must require a second recovery terminal, exact approval and immediate stop on the first unexplained failure.'

$publicCiContract =
    $publicCi.Contains('permissions:') -and
    $publicCi.Contains('contents: read') -and
    [regex]::Matches(
        $publicCi,
        '(?m)^\s*uses:\s+[^@\r\n]+@[A-Fa-f0-9]{40}\s*$').Count -eq 2 -and
    $publicCi.Contains('Test-PublicationBoundary.ps1') -and
    $publicCi.Contains('Test-ControlCenter.ps1') -and
    $publicCi.Contains('Test-NativeStyleLab.ps1') -and
    $publicCi.Contains('Test-DesktopStyleProbe.ps1') -and
    $publicCi.Contains('Test-DesktopStyleSession.ps1') -and
    $publicCi.Contains('Test-NativeWindowStyleSession.ps1') -and
    $publicCi.Contains('Test-ExplorerBridgeModel.ps1') -and
    $publicCi.Contains('Test-ExplorerBridgeCore.ps1') -and
    $publicCi.Contains('Test-ExplorerExactThreadTransport.ps1') -and
    $publicCi.Contains('Test-ExplorerCallWndProcBridge.ps1') -and
    $publicCi.Contains('Test-ExplorerFrameModel.ps1') -and
    $publicCi.Contains('Test-ExplorerPreviewModel.ps1') -and
    $publicCi.Contains('Test-ExplorerSurfaceProbe.ps1') -and
    $publicCi.Contains('Test-ExplorerTransportModel.ps1') -and
    $publicCi.Contains('Test-ExplorerReadOnlyTap.ps1') -and
    $publicCi.Contains('Test-ExplorerReadOnlyAdmission.ps1') -and
    $publicCi.Contains('Test-ExplorerInspectableAdapter.ps1') -and
    $publicCi.Contains('Test-ExplorerStyleTransaction.ps1') -and
    $publicCi.Contains('-StaticOnly') -and
    $publicCi.Contains('dotnet build') -and
    $publicCi.Contains('Canonical native compilation is intentionally not run') -and
    -not $publicCi.Contains('pull_request_target') -and
    -not [regex]::IsMatch(
        $publicCi,
        '(?i)(?:clear-kill-switch|restart-explorer|windhawk\.exe)')
Add-Check `
    'phase3.public-ci-minimum-permissions' `
    $publicCiContract `
    'Public CI must pin actions, use read-only contents permission, build managed code and never touch native activation.'

$publicNamingContract =
    $readme.StartsWith('# JarvisV2') -and
    $readme.Contains('内部运行时安全标识仍为 `JARVIS2`') -and
    $compatibility.project -eq 'JARVIS2' -and
    $compatibility.safety.stateGate -eq 'Local\JARVIS2.StateGate.v1' -and
    $compatibility.host.killSwitch -eq
        '%LOCALAPPDATA%\JARVIS2\disabled.flag'
Add-Check `
    'phase3.public-name-runtime-identity-stable' `
    $publicNamingContract `
    'JarvisV2 may change the public name only; all runtime safety paths and synchronization identities must stay JARVIS2.'

$phase4TaskContract =
    $phase4Task.Contains(
        'Live activation in this task: **FORBIDDEN UNTIL A SEPARATE EXACT APPROVAL**') -and
    $phase4Task.Contains(
        '`disabled.flag` 在全部开发与演练中保持 armed') -and
    $phase4Task.Contains(
        '`active-module.txt` 在全部开发与演练中保持 absent') -and
    $phase4Task.Contains('不启动、配置或启用 Windhawk') -and
    $phase4Task.Contains(
        '不执行 `clear-kill-switch`、不加载模块、不重启 Explorer') -and
    $phase4Task.Contains('恢复终端入口没有 `-ConfirmOpen` 时必须保持 inert') -and
    $phase4Task.Contains('M1 继续 build-only')
Add-Check `
    'phase4.task-locked-exact-gate' `
    $phase4TaskContract `
    'Phase 4 must stop at the separate exact approval gate while M1 remains build-only and the host remains locked.'

$phase4PlanSchemaRequired =
    @($m2SessionPlanSchema.required)
$phase4PlanSourceRequired =
    @($m2SessionPlanSchema.properties.sourceIdentity.required)
$phase4PlanSchemaContract =
    $m2SessionPlanSchema.properties.schemaVersion.const -eq 1 -and
    $m2SessionPlanSchema.properties.receiptType.const -eq
        'jarvisv2-m2-validation-session-plan' -and
    $m2SessionPlanSchema.properties.state.const -eq
        'awaiting-exact-approval' -and
    $m2SessionPlanSchema.properties.moduleId.const -eq
        'jarvis-taskbar-icon-size' -and
    $m2SessionPlanSchema.properties.activationPermitted.const -eq $false -and
    $m2SessionPlanSchema.properties.liveExplorer.const -eq 'not-run' -and
    $m2SessionPlanSchema.properties.mutationPerformed.const -eq $false -and
    $m2SessionPlanSchema.properties.recoveryTerminal.properties.launchPerformed.const -eq
        $false -and
    $m2SessionPlanSchema.properties.recoveryTerminal.properties.terminalAvailable.const -eq
        $false -and
    $m2SessionPlanSchema.properties.approval.properties.exactCommandApproved.const -eq
        $false -and
    $m2SessionPlanSchema.properties.approval.properties.canExecuteNow.const -eq
        $false -and
    $phase4PlanSchemaRequired -contains 'sourceIdentity' -and
    $phase4PlanSourceRequired -contains 'planner' -and
    $phase4PlanSourceRequired -contains 'recoveryTerminalScript' -and
    $phase4PlanSourceRequired -contains 'recoveryLeaseSchema' -and
    $phase4PlanSourceRequired -contains 'observerScript' -and
    $phase4PlanSourceRequired -contains 'nativeBuildReceipt' -and
    $phase4PlanSourceRequired -contains 'm2Source' -and
    $phase4PlanSourceRequired -contains 'supervisorAssembly'
Add-Check `
    'phase4.session-plan-schema-failclosed' `
    $phase4PlanSchemaContract `
    'The session plan must bind every controller and source while remaining non-live and incapable of granting execution.'

$phase4PlannerContract =
    $m2SessionPlanner.Contains(
        '& pwsh -NoLogo -NoProfile -File $readinessScript') -and
    $m2SessionPlanner.Contains(
        'artifacts\m2-validation-session-plans\runs') -and
    $m2SessionPlanner.Contains(
        'Refusing to overwrite an existing session plan.') -and
    $m2SessionPlanner.Contains('activationPermitted = $false') -and
    $m2SessionPlanner.Contains("liveExplorer = 'not-run'") -and
    $m2SessionPlanner.Contains('mutationPerformed = $false') -and
    $m2SessionPlanner.Contains('exactCommandApproved = $false') -and
    $m2SessionPlanner.Contains('canExecuteNow = $false') -and
    -not [regex]::IsMatch(
        $m2SessionPlanner,
        '(?im)^\s*&\s*dotnet[^\r\n]*(?:clear-kill-switch|arm-kill-switch|restart-explorer)') -and
    -not [regex]::IsMatch(
        $m2SessionPlanner,
        '(?i)\b(?:Start-Service|Stop-Service|Set-Service|Stop-Process|taskkill)\b')
Add-Check `
    'phase4.session-planner-no-activation' `
    $phase4PlannerContract `
    'The planner must consume fresh read-only evidence, bind a non-overwriting artifact and never execute recovery or activation.'

$phase4RecoveryDryRunBeforeLaunch =
    (Test-MarkersInOrder `
        -Text $m2RecoveryTerminal `
        -Markers @(
            '$dryRun = -not $ConfirmOpen',
            'if ($dryRun) {',
            'launchPerformed = $false',
            'terminalAvailable = $false',
            'return',
            '$startInfo = [Diagnostics.ProcessStartInfo]::new()',
            '$process = [Diagnostics.Process]::Start($startInfo)'
        ))
$phase4RecoveryTerminalContract =
    $phase4RecoveryDryRunBeforeLaunch -and
    $m2RecoveryTerminal.Contains(
        'JarvisV2 M2 recovery terminal - lease active; no command executed') -and
    $m2RecoveryTerminal.Contains(
        '--configuration Release --no-build -- arm-kill-switch') -and
    $m2RecoveryTerminal.Contains('UseShellExecute = $true') -and
    $m2RecoveryTerminal.Contains(
        'The host was rechecked in the locked state.') -and
    -not $m2RecoveryTerminal.Contains('clear-kill-switch') -and
    -not [regex]::IsMatch(
        $m2RecoveryTerminal,
        '(?im)^\s*&\s*dotnet[^\r\n]*(?:arm-kill-switch|restart-explorer)') -and
    -not [regex]::IsMatch(
        $m2RecoveryTerminal,
        '(?i)\b(?:Start-Service|Stop-Service|Set-Service|Stop-Process|taskkill)\b')
Add-Check `
    'phase4.recovery-terminal-default-inert' `
    $phase4RecoveryTerminalContract `
    'The recovery-terminal entry must recheck the locked plan, remain inert by default and only open a visible command display after exact ConfirmOpen.'

$phase5TaskContract =
    $phase5Task.Contains('Live activation in this task: **FORBIDDEN**') -and
    $phase5Task.Contains(
        '`disabled.flag` 在全部开发和故障注入中保持 armed') -and
    $phase5Task.Contains(
        '`active-module.txt` 在全部开发和故障注入中保持 absent') -and
    $phase5Task.Contains(
        '不执行 `clear-kill-switch` 或 `restart-explorer`') -and
    $phase5Task.Contains('不启动、配置、启用或停止 Windhawk') -and
    $phase5Task.Contains(
        '不加载任何 Windhawk 模块，不终止任何 Windows Shell 进程') -and
    $phase5Task.Contains('M1 继续 build-only') -and
    $phase5Task.Contains('stateDirectoryTouched=false')
Add-Check `
    'phase5.task-locked-recovery-lease-boundary' `
    $phase5TaskContract `
    'Phase 5 must remain a locked, offline recovery-lease task with no host or Shell mutation.'

$phase5ReviewContract =
    $phase5Review.Contains(
        'P0 — Recovery heartbeat conflicted with the native state-root watcher') -and
    $phase5Review.Contains(
        '%LOCALAPPDATA%\JARVIS2\Recovery\m2-recovery-terminal.json') -and
    $phase5Review.Contains(
        'P1 — Terminal loss after activation was not bounded') -and
    $phase5Review.Contains(
        'P1 — Lease and fixture paths followed reparse points') -and
    $phase5Review.Contains(
        'P1 — UTC plan timestamps were parsed as local time by PowerShell') -and
    $phase5Review.Contains(
        'P2 — Offline gates are intentionally serialized') -and
    $phase5Review.Contains(
        'M2 activation during review: **not performed**') -and
    $phase5Review.Contains(
        'explicit approval of the exact activation command plus loading only M2')
Add-Check `
    'phase5.safety-review-closure' `
    $phase5ReviewContract `
    'The Phase 5 review must record the watcher conflict, post-activation bound, reparse closure, serialized-gate constraint and separate live approval.'

$phase5LeaseSchemaRequired = @($m2RecoveryLeaseSchema.required)
$phase5LeaseSchemaContract =
    $m2RecoveryLeaseSchema.properties.schemaVersion.const -eq 1 -and
    $m2RecoveryLeaseSchema.properties.receiptType.const -eq
        'jarvisv2-m2-recovery-terminal-lease' -and
    $m2RecoveryLeaseSchema.properties.moduleId.const -eq
        'jarvis-taskbar-icon-size' -and
    @($m2RecoveryLeaseSchema.properties.state.enum).Count -eq 3 -and
    @($m2RecoveryLeaseSchema.properties.state.enum) -contains 'ready' -and
    @($m2RecoveryLeaseSchema.properties.state.enum) -contains 'closing' -and
    @($m2RecoveryLeaseSchema.properties.state.enum) -contains 'expired' -and
    $m2RecoveryLeaseSchema.properties.activationPermitted.const -eq $false -and
    $m2RecoveryLeaseSchema.properties.mutationPerformed.const -eq $false -and
    $phase5LeaseSchemaRequired -contains 'processStartTimeUtc' -and
    $phase5LeaseSchemaRequired -contains 'heartbeatAtUtc' -and
    $phase5LeaseSchemaRequired -contains 'heartbeatSequence' -and
    $phase5LeaseSchemaRequired -contains 'planSha256' -and
    $phase5LeaseSchemaRequired -contains 'planExpiresAtUtc'
Add-Check `
    'phase5.recovery-lease-schema-failclosed' `
    $phase5LeaseSchemaContract `
    'The lease schema must bind process identity, heartbeat, plan identity and non-activation boundaries.'

$phase5HeartbeatContract =
    $m2RecoveryTerminal.Contains(
        '$heartbeatIntervalMilliseconds = 1000') -and
    $m2RecoveryTerminal.Contains('$heartbeatFreshnessSeconds = 4') -and
    $m2RecoveryTerminal.Contains(
        "`$recoveryDirectory = Join-Path `$stateDirectory 'Recovery'") -and
    $m2RecoveryTerminal.Contains(
        '$temporaryPath = Join-Path $recoveryDirectory') -and
    $m2RecoveryTerminal.Contains(
        "[IO.File]::Move(`$temporaryPath, `$leasePath, `$true)") -and
    $m2RecoveryTerminal.Contains("while ([DateTime]::UtcNow -lt `$expiresAt)") -and
    $m2RecoveryTerminal.Contains("-State 'ready'") -and
    $m2RecoveryTerminal.Contains("-State 'closing'") -and
    $m2RecoveryTerminal.Contains("-State 'expired'") -and
    $m2RecoveryTerminal.Contains(
        'The recovery terminal did not publish a fresh lease within 8 seconds.') -and
    $m2RecoveryTerminal.Contains('function Convert-JsonUtcDateTime') -and
    ([regex]::Matches(
        $m2RecoveryTerminal,
        '\bConvert-JsonUtcDateTime\b').Count -eq 4) -and
    -not $m2RecoveryTerminal.Contains('[DateTime]::Parse(') -and
    $m2RecoveryTerminal.Contains('processStartTimeUtc') -and
    -not $m2RecoveryTerminal.Contains('clear-kill-switch') -and
    -not [regex]::IsMatch(
        $m2RecoveryTerminal,
        '(?im)^\s*&\s*dotnet[^\r\n]*(?:arm-kill-switch|restart-explorer)') -and
    -not [regex]::IsMatch(
        $m2RecoveryTerminal,
        '(?i)\b(?:Start-Service|Stop-Service|Set-Service|Stop-Process|taskkill)\b')
Add-Check `
    'phase5.recovery-terminal-heartbeat-inert' `
    $phase5HeartbeatContract `
    'The visible recovery terminal must atomically heartbeat, expire closed and never execute recovery or activation itself.'

$phase5UtcProbeText = '2026-07-26T18:54:15.6294166Z'
$phase5UtcProbeValue =
    ('{"timestamp":"' + $phase5UtcProbeText + '"}' |
        ConvertFrom-Json).timestamp
$phase5UtcProbe = if ($phase5UtcProbeValue -is [DateTime]) {
    ([DateTime]$phase5UtcProbeValue).ToUniversalTime()
}
else {
    [DateTimeOffset]::Parse(
        [string]$phase5UtcProbeValue,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind).UtcDateTime
}
$phase5UtcParsingContract =
    $phase5UtcProbe.Kind -eq [DateTimeKind]::Utc -and
    $phase5UtcProbe.ToString(
        'o',
        [Globalization.CultureInfo]::InvariantCulture) -eq
        $phase5UtcProbeText -and
    $m2ObservationRehearsal.Contains(
        'function Convert-JsonUtcDateTime') -and
    ([regex]::Matches(
        $m2ObservationRehearsal,
        '\bConvert-JsonUtcDateTime\b').Count -eq 2) -and
    -not $m2ObservationRehearsal.Contains('[DateTime]::Parse(')
Add-Check `
    'phase5.utc-timestamp-roundtrip' `
    $phase5UtcParsingContract `
    'Recovery and observation controllers must preserve Z timestamps as UTC instead of applying the local offset.'

$phase5SupervisorLeaseContract =
    $recoveryTerminalLeaseSource.Contains(
        'private const string LeaseDirectoryName = "Recovery"') -and
    $recoveryTerminalLeaseSource.Contains(
        'MaximumHeartbeatAge = TimeSpan.FromSeconds(4)') -and
    $recoveryTerminalLeaseSource.Contains('lease-heartbeat-stale') -and
    $recoveryTerminalLeaseSource.Contains('lease-process-start-mismatch') -and
    $recoveryTerminalLeaseSource.Contains(
        'plan-supervisor-assembly-not-running') -and
    $recoveryTerminalLeaseSource.Contains(
        'lease-fixture-path-invalid') -and
    $recoveryTerminalLeaseSource.Contains(
        'lease-path-reparse-point') -and
    $recoveryTerminalLeaseSource.Contains(
        'plan-source-hash-mismatch:{key}') -and
    $recoveryTerminalLeaseSource.Contains(
        'm2-validation-session-plans') -and
    [regex]::Matches(
        $killSwitchSource,
        'RecoveryTerminalLease\.RequireReady\(moduleId\)').Count -eq 2 -and
    $programSource.Contains('"inspect-recovery-terminal"') -and
    $programSource.Contains('ExitCodes.SafetyInterlock')
Add-Check `
    'phase5.supervisor-double-recovery-lease-gate' `
    $phase5SupervisorLeaseContract `
    'Supervisor must validate a fresh bound lease twice and expose only a read-only inspection command.'

$clearMethodStart = $programSource.IndexOf(
    'private static int RunClearKillSwitch',
    [StringComparison]::Ordinal)
$clearQuarantineIndex = $programSource.IndexOf(
    'if (KillSwitch.IsLiveActivationQuarantined)',
    $clearMethodStart,
    [StringComparison]::Ordinal)
$clearStateGateIndex = $programSource.IndexOf(
    'using StateGateLease lease = KillSwitch.AcquireStateGate()',
    $clearMethodStart,
    [StringComparison]::Ordinal)
$phase6HostQuarantineContract =
    $killSwitchSource.Contains(
        'private static readonly bool LiveActivationQuarantined = true') -and
    $killSwitchSource.Contains(
        'windhawk-service-global-runtime-injection-observed-20260727') -and
    $killSwitchSource.Contains('if (LiveActivationQuarantined)') -and
    $killSwitchSource.IndexOf(
        'if (LiveActivationQuarantined)',
        [StringComparison]::Ordinal) -lt
        $killSwitchSource.IndexOf(
            'RecoveryTerminalLease.RequireReady(moduleId)',
            [StringComparison]::Ordinal) -and
    $clearMethodStart -ge 0 -and
    $clearQuarantineIndex -gt $clearMethodStart -and
    $clearStateGateIndex -gt $clearQuarantineIndex -and
    $programSource.Contains('error = "live_activation_quarantined"') -and
    $programSource.Contains(
        'Quarantined after prohibited Windhawk global-runtime injection')
Add-Check `
    'phase6.windhawk-host-activation-quarantined' `
    $phase6HostQuarantineContract `
    'Supervisor must reject clear-kill-switch before state-gate acquisition while the Windhawk service host is quarantined.'

$phase6OfflineModelSourceContract =
    $phase6Task.Contains(
        '- [x] Build only an offline/mockable launcher skeleton first.') -and
    $phase6Adr.Contains('Status: accepted for offline modelling only') -and
    $phase6Adr.Contains('Live implementation: prohibited') -and
    $phase6Adr.Contains(
        'Thread-specific `SetWindowsHookEx`') -and
    $explorerHostModelSource.Contains(
        'thread-specific-window-hook-review-candidate') -and
    $explorerHostModelSource.Contains(
        'ExpectedSelectionMode = "shell-window-exact"') -and
    $explorerHostModelSource.Contains(
        'ExpectedModuleContract = "standalone-explicit-init-v1"') -and
    $explorerHostModelSource.Contains('ExecutionSupported = false') -and
    $explorerHostModelSource.Contains('ActivationPermitted = false') -and
    -not [regex]::IsMatch(
        $explorerHostModelSource,
        '(?i)\b(?:DllImport|LibraryImport|OpenProcess|CreateRemoteThread|' +
        'VirtualAllocEx|WriteProcessMemory|SetWindowsHookEx|' +
        'NtQueueApcThread|StartService|ServiceController|' +
        'Microsoft\.Win32\.Registry|System\.Diagnostics\.Process)\b') -and
    $explorerHostPlanSchema.properties.executionSupported.const -eq $false -and
    $explorerHostPlanSchema.properties.activationPermitted.const -eq $false -and
    $explorerHostPlanSchema.properties.liveExplorer.const -eq 'not-run' -and
    $explorerHostPlanSchema.properties.mutationPerformed.const -eq $false
Add-Check `
    'phase6.explorer-host-model-static-offline' `
    $phase6OfflineModelSourceContract `
    'The replacement-host skeleton must remain a fixture-only single PID/TID policy model with no live process or injection API.'

$explorerHostAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $explorerHostModelAuditPath 2>&1
)
$explorerHostAuditExitCode = $LASTEXITCODE
$explorerHostAudit = $null
try {
    $explorerHostAudit =
        ($explorerHostAuditOutput -join [Environment]::NewLine) |
        ConvertFrom-Json -Depth 30
}
catch {
    $explorerHostAudit = $null
}
$phase6OfflineModelAuditPassed =
    $explorerHostAuditExitCode -eq 0 -and
    $null -ne $explorerHostAudit -and
    $explorerHostAudit.result -eq 'passed' -and
    $explorerHostAudit.checkCount -eq 20 -and
    $explorerHostAudit.passedCount -eq 20 -and
    -not $explorerHostAudit.executionSupported -and
    -not $explorerHostAudit.activationPermitted -and
    $explorerHostAudit.liveExplorer -eq 'not-run' -and
    -not $explorerHostAudit.mutationPerformed
Add-Check `
    'phase6.explorer-host-model-executable-audit' `
    $phase6OfflineModelAuditPassed `
    'The 20-case host-model matrix must pass while every receipt remains non-executable, non-live and non-mutating.'

$phase7ControlCenterStaticContract =
    $phase7Task.Contains(
        'Status: **COMPLETE — LOCKED / OFFLINE**') -and
    $phase7Task.Contains(
        'must never be described as a live modified taskbar or Explorer surface') -and
    $controlCenterSource.Contains('<OutputType>WinExe</OutputType>') -and
    $controlCenterSource.Contains('<UseWPF>true</UseWPF>') -and
    $controlCenterSource.Contains('SHELL // LOCKED') -and
    $controlCenterSource.Contains('Text="Conversation"') -and
    $controlCenterSource.Contains('Text="PI RUNTIME"') -and
    $controlCenterSource.Contains(
        'Writes: desktop-owner approval only') -and
    $controlCenterSource.Contains('Text="{Binding ProposalLabel}"') -and
    $controlCenterSource.Contains(
        'Content="{Binding ApproveActionLabel}"') -and
    $piAgentHostConversationStateSource.Contains(
        '"NEW UTF-8 FILE PROPOSAL"') -and
    $piAgentHostConversationStateSource.Contains(
        '"EXACT TEXT REPLACEMENT"') -and
    $piAgentHostConversationStateSource.Contains(
        '"MULTI-HUNK PATCH / {PatchHunks.Count} EXACT CHANGES"') -and
    $piAgentHostConversationStateSource.Contains(
        '"MULTI-FILE CHANGE SET / {FileChanges.Count} FILES"') -and
    $piAgentHostConversationStateSource.Contains('"CREATE ONCE"') -and
    $piAgentHostConversationStateSource.Contains('"APPROVE ONCE"') -and
    $piAgentHostConversationStateSource.Contains('"APPLY PATCH ONCE"') -and
    $piAgentHostConversationStateSource.Contains(
        '"APPLY CHANGE SET ONCE"') -and
    $piAgentHostConversationStateSource.Contains('"REJECT ALL"') -and
    $piAgentHostConversationStateSource.Contains('ReviewSegments') -and
    $controlCenterSource.Contains('ItemsSource="{Binding ReviewSegments}"') -and
    $controlCenterSource.Contains(
        'Content="{Binding RejectActionLabel}"') -and
    $controlCenterSource.Contains('PiAgentDesktopRuntime.StartAsync') -and
    $controlCenterSource.Contains('SAFE SHUTDOWN') -and
    $controlCenterSource.Contains('impeccable:surface-seed:32fb29e4') -and
    -not [regex]::IsMatch(
        $controlCenterSource,
        '(?i)\b(?:DllImport|LibraryImport|OpenProcess|CreateRemoteThread|' +
        'VirtualAllocEx|WriteProcessMemory|SetWindowsHookEx|' +
        'NtQueueApcThread|StartService|ServiceController|' +
        'Microsoft\.Win32\.Registry|System\.Diagnostics\.Process)\b') -and
    -not [regex]::IsMatch(
        $controlCenterMainWindowSource,
        '(?i)(?:Topmost\s*=\s*"True"|AllowsTransparency\s*=\s*"True"|' +
        'WindowState\s*=\s*"Maximized"|ShowInTaskbar\s*=\s*"False")')
Add-Check `
    'phase7.control-center-static-review-gated' `
    $phase7ControlCenterStaticContract `
    'The visible Control Center must remain an ordinary review-gated Pi conversation window with explicit owner-only replace/patch/create/change-set decisions, complete bounded review, shell lock, orderly shutdown and no shell mutation API.'

$controlCenterAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $controlCenterAuditPath `
        -DotnetPath $DotnetPath `
        -NodePath $NodePath 2>&1
)
$controlCenterAuditExitCode = $LASTEXITCODE
$controlCenterAudit = $null
try {
    $controlCenterAudit =
        ($controlCenterAuditOutput -join [Environment]::NewLine) |
        ConvertFrom-Json -Depth 30
}
catch {
    $controlCenterAudit = $null
}
$phase7ControlCenterAuditPassed =
    $controlCenterAuditExitCode -eq 0 -and
    $null -ne $controlCenterAudit -and
    $controlCenterAudit.result -eq 'passed' -and
    $controlCenterAudit.checkCount -eq 18 -and
    $controlCenterAudit.passedCount -eq 18 -and
    $controlCenterAudit.conversationSupported -and
    -not $controlCenterAudit.productionAuthenticationConfigured -and
    -not $controlCenterAudit.executionSupported -and
    -not $controlCenterAudit.activationPermitted -and
    $controlCenterAudit.liveExplorer -eq 'not-run' -and
    -not $controlCenterAudit.mutationPerformed
Add-Check `
    'phase7.control-center-executable-audit' `
    $phase7ControlCenterAuditPassed `
    'The Control Center safety/build audit must pass all eighteen checks, including reviewed iteration, encrypted recent-session resume, in-app admission, local runtime lifecycle and streamed-provider probes, without enabling shell execution.'

$phase7BridgeStaticContract =
    $explorerBridgeSource.Contains(
        'JARVIS_EXPLORER_BRIDGE_ABI_VERSION = 1U') -and
    $explorerBridgeSource.Contains(
        'jarvis_bridge_model_query_contract') -and
    $explorerBridgeSource.Contains(
        'jarvis_bridge_model_initialize') -and
    $explorerBridgeSource.Contains(
        'jarvis_bridge_model_quiesce') -and
    $explorerBridgeSource.Contains(
        'jarvis_bridge_model_query') -and
    $explorerBridgeSource.Contains('.activation_permitted = 0U') -and
    $explorerBridgeSource.Contains('.mutation_performed = 0U') -and
    $explorerBridgeSource.Contains('.live_explorer_touched = 0U') -and
    -not [regex]::IsMatch(
        $explorerBridgeSource,
        '(?i)\b(?:windows\.h|DllMain|__declspec\s*\(\s*dllexport|' +
        'LoadLibrary|OpenProcess|CreateRemoteThread|VirtualAllocEx|' +
        'WriteProcessMemory|SetWindowsHookEx|UnhookWindowsHookEx|' +
        'NtQueueApcThread|TerminateProcess)\b')
Add-Check `
    'phase7.explorer-bridge-contract-static-offline' `
    $phase7BridgeStaticContract `
    'Bridge ABI v1 must expose only an offline fail-closed model, never a DLL export, hook, loader or process API.'

$explorerBridgeAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $explorerBridgeAuditPath 2>&1
)
$explorerBridgeAuditExitCode = $LASTEXITCODE
$explorerBridgeAudit = $null
try {
    $explorerBridgeAudit =
        ($explorerBridgeAuditOutput -join [Environment]::NewLine) |
        ConvertFrom-Json -Depth 30
}
catch {
    $explorerBridgeAudit = $null
}
$phase7BridgeAuditPassed =
    $explorerBridgeAuditExitCode -eq 0 -and
    $null -ne $explorerBridgeAudit -and
    $explorerBridgeAudit.result -eq 'passed' -and
    -not $explorerBridgeAudit.staticOnly -and
    $explorerBridgeAudit.checkCount -eq 6 -and
    $explorerBridgeAudit.passedCount -eq 6 -and
    -not $explorerBridgeAudit.executionSupported -and
    -not $explorerBridgeAudit.activationPermitted -and
    $explorerBridgeAudit.liveExplorer -eq 'not-run' -and
    -not $explorerBridgeAudit.mutationPerformed
Add-Check `
    'phase7.explorer-bridge-contract-executable-audit' `
    $phase7BridgeAuditPassed `
    'The portable 16-case bridge fault matrix must compile and pass while every response remains non-live and non-mutating.'

$phase8NativeStyleStaticContract =
    $phase8Task.Contains(
        'OWN-PROCESS LIVE / EXPLORER READ-ONLY') -and
    $phase8Task.Contains(
        'Changing the real Explorer desktop view is not authorized') -and
    $nativeStyleLabSource.Contains(
        'new WindowInteropHelper(ownedWindow).Handle') -and
    $nativeStyleLabSource.Contains(
        '[DllImport("dwmapi.dll", ExactSpelling = true)]') -and
    [regex]::Matches(
        $nativeStyleLabSource,
        '\[DllImport\(').Count -eq 2 -and
    -not [regex]::IsMatch(
        $nativeStyleLabSource,
        '(?i)\b(?:user32|kernel32|OpenProcess|CreateRemoteThread|' +
        'VirtualAllocEx|WriteProcessMemory|SetWindowsHookEx|SendMessage|' +
        'PostMessage|SetWindowLong|SetWindowPos|FindWindow|EnumWindows|' +
        'System\.Diagnostics\.Process|ServiceController|' +
        'Microsoft\.Win32\.Registry)\b')
Add-Check `
    'phase8.native-style-lab-own-hwnd-static' `
    $phase8NativeStyleStaticContract `
    'The native style lab may apply reviewed DWM attributes only to the HWND owned by its own ordinary window.'

$nativeStyleAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $nativeStyleLabAuditPath 2>&1
)
$nativeStyleAuditExitCode = $LASTEXITCODE
$nativeStyleAudit = $null
try {
    $nativeStyleAudit =
        ($nativeStyleAuditOutput -join [Environment]::NewLine) |
        ConvertFrom-Json -Depth 30
}
catch {
    $nativeStyleAudit = $null
}
$phase8NativeStyleAuditPassed =
    $nativeStyleAuditExitCode -eq 0 -and
    $null -ne $nativeStyleAudit -and
    $nativeStyleAudit.result -eq 'passed' -and
    $nativeStyleAudit.checkCount -eq 6 -and
    $nativeStyleAudit.passedCount -eq 6 -and
    $nativeStyleAudit.scope -eq 'own-process-hwnd-only' -and
    -not $nativeStyleAudit.explorerMutationSupported -and
    -not $nativeStyleAudit.activationPermitted -and
    -not $nativeStyleAudit.mutationPerformed
Add-Check `
    'phase8.native-style-lab-executable-audit' `
    $phase8NativeStyleAuditPassed `
    'The own-process style lab audit must pass all six checks without enabling any Explorer mutation path.'

$phase8DesktopProbeStaticContract =
    $desktopStyleProbeSource.Contains(
        '"exact-shell-defview-child"') -and
    $desktopStyleProbeSource.Contains(
        'mutationSupported = false') -and
    $desktopStyleProbeSource.Contains(
        'liveExplorer = "read-only-inspection"') -and
    [regex]::Matches(
        $desktopStyleProbeSource,
        '\[DllImport\(').Count -eq 5 -and
    -not [regex]::IsMatch(
        $desktopStyleProbeSource,
        '(?i)\b(?:SendMessage|PostMessage|SetWindowLong|SetWindowPos|' +
        'MoveWindow|ShowWindow|DestroyWindow|OpenProcess|' +
        'CreateRemoteThread|VirtualAllocEx|WriteProcessMemory|' +
        'SetWindowsHookEx|TerminateProcess|System\.Diagnostics\.Process|' +
        'ServiceController|Microsoft\.Win32\.Registry)\b')
Add-Check `
    'phase8.desktop-style-probe-readonly-static' `
    $phase8DesktopProbeStaticContract `
    'The desktop probe must expose only exact read-only window discovery and a hard non-mutation receipt.'

$desktopProbeAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $desktopStyleProbeAuditPath `
        -StaticOnly 2>&1
)
$desktopProbeAuditExitCode = $LASTEXITCODE
$desktopProbeAudit = $null
try {
    $desktopProbeAudit =
        ($desktopProbeAuditOutput -join [Environment]::NewLine) |
        ConvertFrom-Json -Depth 30
}
catch {
    $desktopProbeAudit = $null
}
$phase8DesktopProbeAuditPassed =
    $desktopProbeAuditExitCode -eq 0 -and
    $null -ne $desktopProbeAudit -and
    $desktopProbeAudit.result -eq 'passed' -and
    $desktopProbeAudit.staticOnly -and
    $desktopProbeAudit.checkCount -eq 4 -and
    $desktopProbeAudit.passedCount -eq 4 -and
    -not $desktopProbeAudit.executionSupported -and
    -not $desktopProbeAudit.mutationSupported -and
    -not $desktopProbeAudit.activationPermitted -and
    -not $desktopProbeAudit.mutationPerformed -and
    $desktopProbeAudit.liveExplorer -eq 'not-run'
Add-Check `
    'phase8.desktop-style-probe-static-audit' `
    $phase8DesktopProbeAuditPassed `
    'Canonical project checks must audit the desktop probe without inspecting live Explorer.'

$phase8DesktopSessionStaticContract =
    $phase8DesktopSessionTask.Contains(
        'LIVE APPLY NOT YET AUTHORIZED') -and
    $phase8DesktopSessionTask.Contains(
        'LVM_GETTEXTCOLOR') -and
    $phase8DesktopSessionTask.Contains(
        'LVM_SETTEXTCOLOR') -and
    $desktopStyleSessionSource.Contains(
        'MessageTimeoutMilliseconds = 250') -and
    $desktopStyleSessionSource.Contains(
        'MaximumTtlSeconds = 60') -and
    $desktopStyleSessionSource.Contains(
        '--confirm-live-desktop-text-color') -and
    $desktopStyleSessionSource.Contains(
        'store.Prepare(journal);') -and
    $desktopStyleSessionSource.Contains(
        'RollBackExactTarget(') -and
    [regex]::Matches(
        $desktopStyleSessionSource,
        '\[DllImport\(').Count -eq 7 -and
    $desktopStyleSessionSource.Contains(
        'RedrawExactFolderView(') -and
    -not $desktopStyleSessionSource.Contains('HWND_BROADCAST') -and
    -not [regex]::IsMatch(
        $desktopStyleSessionSource,
        '(?i)\b(?:PostMessage|SetWindowLong|SetWindowPos|MoveWindow|' +
        'ShowWindow|DestroyWindow|OpenProcess|CreateRemoteThread|' +
        'VirtualAllocEx|WriteProcessMemory|SetWindowsHookEx|' +
        'TerminateProcess|System\.Diagnostics\.Process|' +
        'ServiceController|Microsoft\.Win32\.Registry|' +
        'DwmSetWindowAttribute|SystemParametersInfo)\b')
Add-Check `
    'phase8.desktop-style-session-narrow-static' `
    $phase8DesktopSessionStaticContract `
    'The desktop style session must remain a journaled, bounded, text-color-only ListView experiment.'

$desktopSessionAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $desktopStyleSessionAuditPath `
        -StaticOnly 2>&1
)
$desktopSessionAuditExitCode = $LASTEXITCODE
$desktopSessionAudit = $null
try {
    $desktopSessionAudit =
        ($desktopSessionAuditOutput -join [Environment]::NewLine) |
        ConvertFrom-Json -Depth 30
}
catch {
    $desktopSessionAudit = $null
}
$phase8DesktopSessionAuditPassed =
    $desktopSessionAuditExitCode -eq 0 -and
    $null -ne $desktopSessionAudit -and
    $desktopSessionAudit.result -eq 'passed' -and
    $desktopSessionAudit.staticOnly -and
    $desktopSessionAudit.checkCount -eq 11 -and
    $desktopSessionAudit.passedCount -eq 11 -and
    -not $desktopSessionAudit.liveMutationRun -and
    -not $desktopSessionAudit.activationPermitted -and
    -not $desktopSessionAudit.mutationPerformed -and
    $desktopSessionAudit.liveExplorer -eq 'not-run'
Add-Check `
    'phase8.desktop-style-session-static-audit' `
    $phase8DesktopSessionAuditPassed `
    'Canonical project checks must audit all desktop session guards without reading or mutating live Explorer.'

$phase8NativeWindowSessionStaticContract =
    $phase8NativeWindowSessionTask.Contains(
        'LIVE APPLY REQUIRES SEPARATE EXACT APPROVAL') -and
    $phase8NativeWindowSessionTask.Contains(
        'pixel-identical') -and
    $phase8NativeWindowSessionTask.Contains(
        'DWMWA_COLOR_DEFAULT') -and
    $nativeWindowStyleSessionSource.Contains(
        'BorderColor = 34') -and
    $nativeWindowStyleSessionSource.Contains(
        'CaptionColor = 35') -and
    $nativeWindowStyleSessionSource.Contains(
        'TextColor = 36') -and
    $nativeWindowStyleSessionSource.Contains(
        '--baseline-system-default') -and
    $nativeWindowStyleSessionSource.Contains(
        '--confirm-live-native-window-style') -and
    $nativeWindowStyleSessionSource.Contains(
        'store.Prepare(journal);') -and
    $nativeWindowStyleSessionSource.Contains(
        'ResetExactTarget(') -and
    [regex]::Matches(
        $nativeWindowStyleSessionSource,
        '\[DllImport\(').Count -eq 7 -and
    -not [regex]::IsMatch(
        $nativeWindowStyleSessionSource,
        '(?i)\b(?:OpenProcess|CreateRemoteThread|VirtualAllocEx|' +
        'WriteProcessMemory|SetWindowsHookEx|TerminateProcess|' +
        'SendMessage|PostMessage|SetWindowLong|SetWindowPos|' +
        'MoveWindow|ShowWindow|DestroyWindow|' +
        'System\.Diagnostics\.Process|ServiceController|' +
        'Microsoft\.Win32\.Registry|SetWindowCompositionAttribute)\b')
Add-Check `
    'phase8.native-window-style-session-narrow-static' `
    $phase8NativeWindowSessionStaticContract `
    'The native Explorer window session must remain a bounded, exact-HWND, DWM-color-only experiment with system-default reset.'

$nativeWindowSessionAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $nativeWindowStyleSessionAuditPath `
        -StaticOnly 2>&1
)
$nativeWindowSessionAuditExitCode = $LASTEXITCODE
$nativeWindowSessionAudit = $null
try {
    $nativeWindowSessionAudit =
        ($nativeWindowSessionAuditOutput -join [Environment]::NewLine) |
        ConvertFrom-Json -Depth 30
}
catch {
    $nativeWindowSessionAudit = $null
}
$phase8NativeWindowSessionAuditPassed =
    $nativeWindowSessionAuditExitCode -eq 0 -and
    $null -ne $nativeWindowSessionAudit -and
    $nativeWindowSessionAudit.result -eq 'passed' -and
    $nativeWindowSessionAudit.staticOnly -and
    $nativeWindowSessionAudit.checkCount -eq 9 -and
    $nativeWindowSessionAudit.passedCount -eq 9 -and
    -not $nativeWindowSessionAudit.liveMutationRun -and
    -not $nativeWindowSessionAudit.activationPermitted -and
    -not $nativeWindowSessionAudit.mutationPerformed -and
    $nativeWindowSessionAudit.liveExplorer -eq 'not-run'
Add-Check `
    'phase8.native-window-style-session-static-audit' `
    $phase8NativeWindowSessionAuditPassed `
    'Canonical checks must audit the temporary Explorer DWM session without touching a live window.'

$phase9FrameModelStaticContract =
    $phase9Task.Contains(
        'OFFLINE MODEL COMPLETE — LIVE XAML CONNECTION NOT AUTHORIZED') -and
    $phase9Task.Contains('No live XAML connection exists.') -and
    $explorerFrameModelSource.Contains(
        'public const string TabStrip = "tab-strip"') -and
    $explorerFrameModelSource.Contains(
        'public const string CommandBar = "command-bar"') -and
    $explorerFrameModelSource.Contains(
        'public const string NavigationPane = "navigation-pane"') -and
    $explorerFrameModelSource.Contains(
        'public const string Background = "Background"') -and
    $explorerFrameModelSource.Contains(
        'public const string Foreground = "Foreground"') -and
    $explorerFrameModelSource.Contains(
        'public const string BorderBrush = "BorderBrush"') -and
    $explorerFrameModelSource.Contains(
        'offline-fixture-candidate-pending-live-discovery') -and
    $explorerFrameModelSource.Contains(
        'int last = _applied.Count - 1;') -and
    $explorerFrameModelSource.Contains(
        'FrameTransactionState.RestoreRequired') -and
    -not [regex]::IsMatch(
        $explorerFrameModelSource,
        '(?i)\b(?:DllImport|LibraryImport|ComImport|Marshal\.|' +
        'InitializeXamlDiagnosticsEx|IXamlDiagnostics|IVisualTreeService|' +
        'OpenProcess|CreateRemoteThread|VirtualAllocEx|WriteProcessMemory|' +
        'SetWindowsHookEx|LoadLibrary|StartService|ServiceController|' +
        'Microsoft\.Win32\.Registry|System\.Diagnostics\.Process)\b')
Add-Check `
    'phase9.explorer-frame-model-static-offline' `
    $phase9FrameModelStaticContract `
    'The Phase 9 model must remain fixture-only, cover exactly three native frame roles, snapshot originals and contain no live XAML or process transport.'

$explorerFrameAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $explorerFrameModelAuditPath 2>&1
)
$explorerFrameAuditExitCode = $LASTEXITCODE
$explorerFrameAudit = $null
try {
    $explorerFrameAudit =
        ($explorerFrameAuditOutput -join [Environment]::NewLine) |
        ConvertFrom-Json -Depth 30
}
catch {
    $explorerFrameAudit = $null
}
$phase9FrameModelAuditPassed =
    $explorerFrameAuditExitCode -eq 0 -and
    $null -ne $explorerFrameAudit -and
    $explorerFrameAudit.result -eq 'passed' -and
    $explorerFrameAudit.checkCount -eq 7 -and
    $explorerFrameAudit.passedCount -eq 7 -and
    -not $explorerFrameAudit.executionSupported -and
    -not $explorerFrameAudit.activationPermitted -and
    $explorerFrameAudit.liveExplorer -eq 'not-run' -and
    -not $explorerFrameAudit.mutationPerformed
Add-Check `
    'phase9.explorer-frame-model-executable-audit' `
    $phase9FrameModelAuditPassed `
    'The 29-case frame transaction matrix and seven-check audit must pass without creating a live Explorer execution path.'

$fileExplorerStylerLock = @(
    $upstreamLock.dependencies |
        Where-Object name -eq 'Windows 11 File Explorer Styler'
)
$phase10ProfileStaticContract =
    $phase10Task.Contains(
        'OFFLINE DEVELOPMENT COMPLETE — VISUAL APPROVAL NOT REQUESTED') -and
    $fileExplorerStylerLock.Count -eq 1 -and
    $fileExplorerStylerLock[0].version -eq '1.5' -and
    $fileExplorerStylerLock[0].auditedCommit -eq
        '109589023dde428deaee2fe80e4ce446283a7935' -and
    $fileExplorerStylerLock[0].gitBlob -eq
        '6f67b714c271db1235a5f937c30c5cae55b180bf' -and
    $fileExplorerStylerLock[0].sourceSize -eq 326922 -and
    $fileExplorerStylerLock[0].sourceSha256 -eq
        'ECD6189A76439518E84938F4CA42FDB7F78AA1CCE3151EE0FE93638918D2DCED' -and
    $explorerFrameSelectorProfile.lifecycleState -eq
        'offline-candidate' -and
    $explorerFrameSelectorProfile.liveEvidence -eq 'not-run' -and
    -not $explorerFrameSelectorProfile.executionSupported -and
    -not $explorerFrameSelectorProfile.activationPermitted -and
    -not $explorerFrameSelectorProfile.mutationPerformed -and
    $explorerFrameSelectorSchema.properties.executionSupported.const -eq
        $false -and
    $explorerFrameSelectorSchema.properties.activationPermitted.const -eq
        $false -and
    $explorerFrameSelectorSchema.properties.mutationPerformed.const -eq
        $false
Add-Check `
    'phase10.gpl-selector-profile-static-offline' `
    $phase10ProfileStaticContract `
    'Phase 10 must pin the GPL File Explorer Styler source and keep the exact three-surface candidate schema offline and non-authorizing.'

$explorerPreviewAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $explorerPreviewModelAuditPath 2>&1
)
$explorerPreviewAuditExitCode = $LASTEXITCODE
$explorerPreviewAudit = $null
try {
    $explorerPreviewAudit =
        ($explorerPreviewAuditOutput -join [Environment]::NewLine) |
        ConvertFrom-Json -Depth 30
}
catch {
    $explorerPreviewAudit = $null
}
$phase10PreviewAuditPassed =
    $explorerPreviewAuditExitCode -eq 0 -and
    $null -ne $explorerPreviewAudit -and
    $explorerPreviewAudit.result -eq 'passed' -and
    $explorerPreviewAudit.checkCount -eq 8 -and
    $explorerPreviewAudit.passedCount -eq 8 -and
    -not $explorerPreviewAudit.executionSupported -and
    -not $explorerPreviewAudit.activationPermitted -and
    $explorerPreviewAudit.liveExplorer -eq 'not-run' -and
    -not $explorerPreviewAudit.mutationPerformed
Add-Check `
    'phase10.selector-preview-model-executable-audit' `
    $phase10PreviewAuditPassed `
    'The real candidate compiler and 43-case preview-plan matrix must pass without exposing a live style transport.'

$explorerSurfaceProbeAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $explorerSurfaceProbeAuditPath 2>&1
)
$explorerSurfaceProbeAuditExitCode = $LASTEXITCODE
$explorerSurfaceProbeAudit = $null
try {
    $explorerSurfaceProbeAudit =
        ($explorerSurfaceProbeAuditOutput -join [Environment]::NewLine) |
        ConvertFrom-Json -Depth 30
}
catch {
    $explorerSurfaceProbeAudit = $null
}
$phase10SurfaceProbeAuditPassed =
    $explorerSurfaceProbeAuditExitCode -eq 0 -and
    $null -ne $explorerSurfaceProbeAudit -and
    $explorerSurfaceProbeAudit.result -eq 'passed' -and
    $explorerSurfaceProbeAudit.checkCount -eq 6 -and
    $explorerSurfaceProbeAudit.passedCount -eq 6 -and
    -not $explorerSurfaceProbeAudit.liveInspectionRun -and
    -not $explorerSurfaceProbeAudit.executionSupported -and
    -not $explorerSurfaceProbeAudit.mutationSupported -and
    -not $explorerSurfaceProbeAudit.activationPermitted -and
    $explorerSurfaceProbeAudit.liveExplorer -eq 'not-run' -and
    -not $explorerSurfaceProbeAudit.mutationPerformed
Add-Check `
    'phase10.exact-readonly-surface-probe-static-audit' `
    $phase10SurfaceProbeAuditPassed `
    'The exact-HWND UIA topology probe must pass its six static checks without running a live inspection.'

$phase11TransportStaticContract =
    $phase11Task.Contains(
        'OFFLINE TRANSPORT CORE COMPLETE — NO TAP DLL OR LIVE CONNECTION'
    ) -and
    $explorerTransportContract.schemaVersion -eq 1 -and
    $explorerTransportContract.contractId -eq
        'jarvis-explorer-xaml-transport-v1' -and
    $explorerTransportContract.lifecycleState -eq 'offline-model-only' -and
    $explorerTransportContract.connectionCandidate.api -eq
        'InitializeXamlDiagnosticsEx' -and
    $explorerTransportContract.connectionCandidate.targetSelection -eq
        'caller-supplied-exact-pid-only' -and
    -not $explorerTransportContract.connectionCandidate.liveConnectionImplemented -and
    -not $explorerTransportContract.targetIdentity.processEnumerationAllowed -and
    -not $explorerTransportContract.targetIdentity.windowEnumerationAllowed -and
    $explorerTransportContract.targetIdentity.identityRecheckBeforeEveryCommand -and
    $explorerTransportContract.capability.oneShot -and
    -not $explorerTransportContract.capability.selfApprovalAllowed -and
    $explorerTransportContract.surfacePolicy.requiredOriginalJournalEntryCount -eq 9 -and
    -not $explorerTransportContract.executionSupported -and
    -not $explorerTransportContract.readyForLiveConnection -and
    -not $explorerTransportContract.readyForExactApproval -and
    -not $explorerTransportContract.activationPermitted -and
    $explorerTransportContract.liveExplorer -eq 'not-run' -and
    -not $explorerTransportContract.mutationPerformed -and
    $explorerTransportContractSchema.properties.executionSupported.const -eq
        $false -and
    $explorerTransportContractSchema.properties.readyForLiveConnection.const -eq
        $false -and
    $explorerTransportContractSchema.properties.activationPermitted.const -eq
        $false -and
    $explorerTransportModelSource.Contains(
        'JARVIS_EXPLORER_TRANSPORT_ABI_VERSION = 1U'
    ) -and
    $explorerTransportModelSource.Contains(
        'static_assert(sizeof(jarvis_transport_response) == 64U)'
    )
Add-Check `
    'phase11.xaml-transport-contract-static-offline' `
    $phase11TransportStaticContract `
    'Phase 11 must keep the exact-PID XAML transport ABI machine-bound, model-only, non-enumerating and incapable of live connection or self-approval.'

$explorerTransportAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $explorerTransportModelAuditPath 2>&1
)
$explorerTransportAuditExitCode = $LASTEXITCODE
try {
    $explorerTransportAudit = (
        $explorerTransportAuditOutput -join [Environment]::NewLine
    ) | ConvertFrom-Json -Depth 30
}
catch {
    $explorerTransportAudit = $null
}
$phase11TransportAuditPassed =
    $explorerTransportAuditExitCode -eq 0 -and
    $null -ne $explorerTransportAudit -and
    $explorerTransportAudit.result -eq 'passed' -and
    $explorerTransportAudit.checkCount -eq 12 -and
    $explorerTransportAudit.passedCount -eq 12 -and
    $explorerTransportAudit.scenarioCount -eq 85 -and
    $explorerTransportAudit.scenarioPassedCount -eq 85 -and
    -not $explorerTransportAudit.executionSupported -and
    -not $explorerTransportAudit.activationPermitted -and
    $explorerTransportAudit.liveExplorer -eq 'not-run' -and
    -not $explorerTransportAudit.mutationPerformed
Add-Check `
    'phase11.xaml-transport-model-executable-audit' `
    $phase11TransportAuditPassed `
    'The portable exact-target transport state machine must pass 85/85 fault scenarios while every receipt remains non-live and non-authorizing.'

$phase12ReadOnlyTapStaticContract =
    $phase12Task.Contains(
        'OFFLINE TAP BUILD COMPLETE — DLL NEVER LOADED'
    ) -and
    $explorerReadOnlyTapContract.schemaVersion -eq 1 -and
    $explorerReadOnlyTapContract.contractId -eq
        'jarvis-explorer-readonly-tap-offline-build-v1' -and
    $explorerReadOnlyTapContract.lifecycleState -eq
        'offline-build-only' -and
    $explorerReadOnlyTapContract.tap.liveCompileSwitchValue -eq 0 -and
    $explorerReadOnlyTapContract.tap.setSiteResult -eq
        'E_ACCESSDENIED' -and
    -not $explorerReadOnlyTapContract.tap.dllLoadedDuringValidation -and
    $explorerReadOnlyTapContract.controller.mode -eq 'describe-only' -and
    $explorerReadOnlyTapContract.controller.existingDiagnosticsConsumerPolicy -eq
        'reject' -and
    $explorerReadOnlyTapContract.controller.endpointAttemptLimit -eq 0 -and
    -not $explorerReadOnlyTapContract.controller.tapDllLoadSupported -and
    -not $explorerReadOnlyTapContract.propertyReadSupported -and
    -not $explorerReadOnlyTapContract.executionSupported -and
    -not $explorerReadOnlyTapContract.readyForLiveConnection -and
    -not $explorerReadOnlyTapContract.readyForExactApproval -and
    -not $explorerReadOnlyTapContract.activationPermitted -and
    $explorerReadOnlyTapContract.liveExplorer -eq 'not-run' -and
    -not $explorerReadOnlyTapContract.mutationPerformed -and
    $explorerReadOnlyTapContractSchema.additionalProperties -eq
        $false -and
    $explorerReadOnlyTapContractSchema.properties.executionSupported.const -eq
        $false -and
    $explorerReadOnlyTapSource.Contains(
        '#if JARVIS_ENABLE_LIVE_XAML_READONLY != 0'
    ) -and
    $explorerReadOnlyTapSource.Contains(
        'static_assert(JARVIS_ENABLE_LIVE_XAML_READONLY == 0)'
    ) -and
    $explorerReadOnlyTapSource.Contains('return E_ACCESSDENIED;') -and
    -not $explorerReadOnlyTapSource.Contains(
        'InitializeXamlDiagnosticsEx('
    )
Add-Check `
    'phase12.readonly-tap-static-offline-contract' `
    $phase12ReadOnlyTapStaticContract `
    'Phase 12 must remain a disk-only AMD64 TAP build with SetSite permanently refused, a describe-only controller, zero endpoint/load support and no live diagnostics call.'

$explorerReadOnlyTapAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $explorerReadOnlyTapAuditPath 2>&1
)
$explorerReadOnlyTapAuditExitCode = $LASTEXITCODE
try {
    $explorerReadOnlyTapAudit = (
        $explorerReadOnlyTapAuditOutput -join [Environment]::NewLine
    ) | ConvertFrom-Json -Depth 30
}
catch {
    $explorerReadOnlyTapAudit = $null
}
$phase12ReadOnlyTapAuditPassed =
    $explorerReadOnlyTapAuditExitCode -eq 0 -and
    $null -ne $explorerReadOnlyTapAudit -and
    $explorerReadOnlyTapAudit.result -eq 'passed' -and
    $explorerReadOnlyTapAudit.checkCount -eq 18 -and
    $explorerReadOnlyTapAudit.passedCount -eq 18 -and
    $explorerReadOnlyTapAudit.scenarioCount -eq 38 -and
    $explorerReadOnlyTapAudit.scenarioPassedCount -eq 38 -and
    $explorerReadOnlyTapAudit.tapDllBuilt -and
    $explorerReadOnlyTapAudit.controllerBuilt -and
    $explorerReadOnlyTapAudit.controllerExecutedDescribeOnly -and
    -not $explorerReadOnlyTapAudit.tapDllLoaded -and
    -not $explorerReadOnlyTapAudit.liveConnectionCompiled -and
    -not $explorerReadOnlyTapAudit.executionSupported -and
    -not $explorerReadOnlyTapAudit.activationPermitted -and
    $explorerReadOnlyTapAudit.liveExplorer -eq 'not-run' -and
    -not $explorerReadOnlyTapAudit.mutationPerformed
Add-Check `
    'phase12.readonly-tap-offline-build-and-pe-audit' `
    $phase12ReadOnlyTapAuditPassed `
    'The portable TAP/controller build must pass 18/18 checks and 38/38 protocol scenarios, inspect exact exports/imports, and prove the DLL was never loaded.'

$phase13AdmissionStaticContract =
    $phase13Task.Contains(
        'OFFLINE MODELS COMPLETE — NO ENDPOINT ATTEMPT OR PROPERTY READ'
    ) -and
    $explorerReadOnlyAdmissionContract.schemaVersion -eq 1 -and
    $explorerReadOnlyAdmissionContract.contractId -eq
        'jarvis-explorer-readonly-admission-fingerprint-v1' -and
    $explorerReadOnlyAdmissionContract.lifecycleState -eq
        'offline-model-only' -and
    $explorerReadOnlyAdmissionContract.admission.existingDiagnosticsConsumerCountRequired -eq
        0 -and
    $explorerReadOnlyAdmissionContract.admission.endpointCandidateCountRequired -eq
        1 -and
    $explorerReadOnlyAdmissionContract.admission.runtimeEndpointAttemptLimit -eq
        0 -and
    @($explorerReadOnlyAdmissionContract.admission.requiredBinaryHashes).Count -eq
        4 -and
    $explorerReadOnlyAdmissionContract.admission.requiredTapExportCount -eq
        2 -and
    $explorerReadOnlyAdmissionContract.admission.oneShotPlanConsumedOnAdmission -and
    $explorerReadOnlyAdmissionContract.admission.completeBindByteMatchRequired -and
    $explorerReadOnlyAdmissionContract.fingerprint.surfaceCount -eq 3 -and
    $explorerReadOnlyAdmissionContract.fingerprint.propertyCount -eq 3 -and
    $explorerReadOnlyAdmissionContract.fingerprint.observationCount -eq 9 -and
    @($explorerReadOnlyAdmissionContract.fingerprint.allowedValueKinds).Count -eq
        2 -and
    -not $explorerReadOnlyAdmissionContract.fingerprint.propertyReadSupported -and
    -not $explorerReadOnlyAdmissionContract.integration.modelEntryPointsExported -and
    -not $explorerReadOnlyAdmissionContract.integration.endpointAttemptedDuringValidation -and
    -not $explorerReadOnlyAdmissionContract.integration.tapDllLoadedDuringValidation -and
    -not $explorerReadOnlyAdmissionContract.executionSupported -and
    -not $explorerReadOnlyAdmissionContract.readyForLiveConnection -and
    -not $explorerReadOnlyAdmissionContract.readyForExactApproval -and
    -not $explorerReadOnlyAdmissionContract.activationPermitted -and
    $explorerReadOnlyAdmissionContract.liveExplorer -eq 'not-run' -and
    -not $explorerReadOnlyAdmissionContract.mutationPerformed -and
    $explorerReadOnlyAdmissionContractSchema.additionalProperties -eq
        $false -and
    $explorerReadOnlyAdmissionSource.Contains(
        'static_assert(sizeof(jarvis_tap_admission_request) == 792U)'
    ) -and
    $explorerReadOnlyAdmissionSource.Contains(
        'static_assert(sizeof(jarvis_tap_fingerprint_request) == 176U)'
    ) -and
    $explorerReadOnlyAdmissionSource.Contains(
        'instance->bind = request->bind'
    ) -and
    $explorerReadOnlyAdmissionSource.Contains(
        'JARVIS_TAP_FINGERPRINT_RESULT_VALUE_UNSUPPORTED'
    ) -and
    $explorerReadOnlyAdmissionSource.Contains(
        'kSha256RoundConstants'
    ) -and
    -not $explorerReadOnlyAdmissionSource.Contains(
        'InitializeXamlDiagnosticsEx'
    )
Add-Check `
    'phase13.admission-fingerprint-static-offline-contract' `
    $phase13AdmissionStaticContract `
    'Phase 13 must require zero consumers and one offline endpoint candidate, consume the full exact bind once, fingerprint only nine canonical values, and retain every non-live claim.'

$explorerReadOnlyAdmissionAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $explorerReadOnlyAdmissionAuditPath 2>&1
)
$explorerReadOnlyAdmissionAuditExitCode = $LASTEXITCODE
try {
    $explorerReadOnlyAdmissionAudit = (
        $explorerReadOnlyAdmissionAuditOutput -join [Environment]::NewLine
    ) | ConvertFrom-Json -Depth 30
}
catch {
    $explorerReadOnlyAdmissionAudit = $null
}
$phase13AdmissionAuditPassed =
    $explorerReadOnlyAdmissionAuditExitCode -eq 0 -and
    $null -ne $explorerReadOnlyAdmissionAudit -and
    $explorerReadOnlyAdmissionAudit.result -eq 'passed' -and
    $explorerReadOnlyAdmissionAudit.checkCount -eq 11 -and
    $explorerReadOnlyAdmissionAudit.passedCount -eq 11 -and
    $explorerReadOnlyAdmissionAudit.scenarioCount -eq 50 -and
    $explorerReadOnlyAdmissionAudit.scenarioPassedCount -eq 50 -and
    $explorerReadOnlyAdmissionAudit.firstFingerprintSha256 -eq
        '00542DB9887A4CE9FA17AD0B42EC164D5E38FDD3BFE410D9517B2814CC264560' -and
    -not $explorerReadOnlyAdmissionAudit.endpointAttempted -and
    -not $explorerReadOnlyAdmissionAudit.tapDllLoaded -and
    -not $explorerReadOnlyAdmissionAudit.propertyReadSupported -and
    -not $explorerReadOnlyAdmissionAudit.liveConnectionCompiled -and
    -not $explorerReadOnlyAdmissionAudit.executionSupported -and
    -not $explorerReadOnlyAdmissionAudit.activationPermitted -and
    $explorerReadOnlyAdmissionAudit.liveExplorer -eq 'not-run' -and
    -not $explorerReadOnlyAdmissionAudit.mutationPerformed
Add-Check `
    'phase13.admission-fingerprint-executable-audit' `
    $phase13AdmissionAuditPassed `
    'The portable admission/fingerprint core must pass 11/11 checks and 50/50 fault scenarios with the independently frozen SHA-256 vector and no endpoint, DLL or property access.'

$phase14AdapterStaticContract =
    $phase14Task.Contains(
        'OFFLINE PROJECTION MODEL COMPLETE — NO IINSPECTABLE READ'
    ) -and
    $explorerInspectableAdapterContract.schemaVersion -eq 1 -and
    $explorerInspectableAdapterContract.contractId -eq
        'jarvis-explorer-inspectable-adapter-v1' -and
    $explorerInspectableAdapterContract.lifecycleState -eq
        'offline-projection-model-only' -and
    $explorerInspectableAdapterContract.compileGate.requiredValue -eq 0 -and
    -not $explorerInspectableAdapterContract.compileGate.livePropertyReadCompiled -and
    $explorerInspectableAdapterContract.projection.snapshotBytes -eq 192 -and
    $explorerInspectableAdapterContract.projection.acceptedValueOrigin -eq
        'local' -and
    $explorerInspectableAdapterContract.projection.exactRuntimeClassNameMatchRequiredForObject -and
    $explorerInspectableAdapterContract.projection.maximumOpacityMillionths -eq
        1000000 -and
    $explorerInspectableAdapterContract.fingerprint.canonicalValueCountRequired -eq
        9 -and
    -not $explorerInspectableAdapterContract.integration.adapterEntryPointsExported -and
    -not $explorerInspectableAdapterContract.integration.iInspectableReadAttemptedDuringValidation -and
    -not $explorerInspectableAdapterContract.integration.endpointAttemptedDuringValidation -and
    -not $explorerInspectableAdapterContract.integration.tapDllLoadedDuringValidation -and
    -not $explorerInspectableAdapterContract.propertyReadSupported -and
    -not $explorerInspectableAdapterContract.executionSupported -and
    -not $explorerInspectableAdapterContract.readyForLiveConnection -and
    -not $explorerInspectableAdapterContract.readyForExactApproval -and
    -not $explorerInspectableAdapterContract.activationPermitted -and
    $explorerInspectableAdapterContract.liveExplorer -eq 'not-run' -and
    -not $explorerInspectableAdapterContract.mutationPerformed -and
    $explorerInspectableAdapterContractSchema.additionalProperties -eq
        $false -and
    $explorerInspectableAdapterSource.Contains(
        'static_assert(sizeof(jarvis_tap_runtime_property_snapshot) == 192U)'
    ) -and
    $explorerInspectableAdapterSource.Contains(
        'snapshot->exact_runtime_class_name_matched != 1U'
    ) -and
    $explorerInspectableAdapterSource.Contains(
        'instance->fingerprint.state ='
    ) -and
    -not $explorerInspectableAdapterSource.Contains(
        'InitializeXamlDiagnosticsEx'
    )
Add-Check `
    'phase14.inspectable-adapter-static-offline-contract' `
    $phase14AdapterStaticContract `
    'Phase 14 must accept only bounded local null or exact solid-color projections, own one Phase 13 fingerprint, and keep the live read gate closed.'

$explorerInspectableAdapterAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $explorerInspectableAdapterAuditPath 2>&1
)
$explorerInspectableAdapterAuditExitCode = $LASTEXITCODE
try {
    $explorerInspectableAdapterAudit = (
        $explorerInspectableAdapterAuditOutput -join [Environment]::NewLine
    ) | ConvertFrom-Json -Depth 30
}
catch {
    $explorerInspectableAdapterAudit = $null
}
$phase14AdapterAuditPassed =
    $explorerInspectableAdapterAuditExitCode -eq 0 -and
    $null -ne $explorerInspectableAdapterAudit -and
    $explorerInspectableAdapterAudit.result -eq 'passed' -and
    $explorerInspectableAdapterAudit.checkCount -eq 11 -and
    $explorerInspectableAdapterAudit.passedCount -eq 11 -and
    $explorerInspectableAdapterAudit.scenarioCount -eq 29 -and
    $explorerInspectableAdapterAudit.scenarioPassedCount -eq 29 -and
    -not $explorerInspectableAdapterAudit.iInspectableReadAttempted -and
    -not $explorerInspectableAdapterAudit.propertyReadSupported -and
    -not $explorerInspectableAdapterAudit.endpointAttempted -and
    -not $explorerInspectableAdapterAudit.tapDllLoaded -and
    -not $explorerInspectableAdapterAudit.executionSupported -and
    -not $explorerInspectableAdapterAudit.activationPermitted -and
    $explorerInspectableAdapterAudit.liveExplorer -eq 'not-run' -and
    -not $explorerInspectableAdapterAudit.mutationPerformed
Add-Check `
    'phase14.inspectable-adapter-executable-audit' `
    $phase14AdapterAuditPassed `
    'The portable projection adapter must pass 11/11 checks and 29/29 fault scenarios without a COM object, property read, endpoint attempt or DLL load.'

$phase15TransactionStaticContract =
    $phase15Task.Contains(
        'OFFLINE TRANSACTION MODEL COMPLETE — NO PLATFORM WRITE'
    ) -and
    $explorerStyleTransactionContract.schemaVersion -eq 1 -and
    $explorerStyleTransactionContract.contractId -eq
        'jarvis-explorer-style-transaction-v1' -and
    $explorerStyleTransactionContract.lifecycleState -eq
        'offline-reversible-transaction-model-only' -and
    $explorerStyleTransactionContract.compileGate.requiredValue -eq 0 -and
    -not $explorerStyleTransactionContract.compileGate.livePropertyWriteCompiled -and
    $explorerStyleTransactionContract.prepare.originalValueCountRequired -eq
        9 -and
    $explorerStyleTransactionContract.prepare.styledValueCountRequired -eq
        9 -and
    $explorerStyleTransactionContract.prepare.previewDurationMilliseconds -eq
        60000 -and
    $explorerStyleTransactionContract.apply.writeAttemptSetsDirtyBeforeResult -and
    $explorerStyleTransactionContract.apply.readAfterWriteVerificationRequired -and
    $explorerStyleTransactionContract.restore.order -eq
        'strict-reverse-last-dirty-first' -and
    $explorerStyleTransactionContract.restore.failedRestoreRemainsDirty -and
    $explorerStyleTransactionContract.restore.restoredRequiresDirtyMaskZero -and
    -not $explorerStyleTransactionContract.integration.transactionEntryPointsExported -and
    -not $explorerStyleTransactionContract.integration.platformWriteAttemptedDuringValidation -and
    -not $explorerStyleTransactionContract.integration.endpointAttemptedDuringValidation -and
    -not $explorerStyleTransactionContract.integration.tapDllLoadedDuringValidation -and
    -not $explorerStyleTransactionContract.propertyReadSupported -and
    -not $explorerStyleTransactionContract.propertyWriteSupported -and
    -not $explorerStyleTransactionContract.executionSupported -and
    -not $explorerStyleTransactionContract.readyForLiveConnection -and
    -not $explorerStyleTransactionContract.readyForExactApproval -and
    -not $explorerStyleTransactionContract.activationPermitted -and
    $explorerStyleTransactionContract.liveExplorer -eq 'not-run' -and
    -not $explorerStyleTransactionContract.mutationPerformed -and
    $explorerStyleTransactionContractSchema.additionalProperties -eq
        $false -and
    $explorerStyleTransactionSource.Contains(
        'static_assert(sizeof(jarvis_tap_style_transaction_instance) == 1072U)'
    ) -and
    $explorerStyleTransactionSource.Contains(
        'instance->dirty_mask |= 1U << index'
    ) -and
    $explorerStyleTransactionSource.Contains(
        'HighestDirtyIndex('
    ) -and
    -not $explorerStyleTransactionSource.Contains(
        'InitializeXamlDiagnosticsEx'
    )
Add-Check `
    'phase15.style-transaction-static-offline-contract' `
    $phase15TransactionStaticContract `
    'Phase 15 must snapshot all nine values, dirty every reported write attempt, verify each value and restore strictly in reverse while the live write gate stays closed.'

$explorerStyleTransactionAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $explorerStyleTransactionAuditPath 2>&1
)
$explorerStyleTransactionAuditExitCode = $LASTEXITCODE
try {
    $explorerStyleTransactionAudit = (
        $explorerStyleTransactionAuditOutput -join [Environment]::NewLine
    ) | ConvertFrom-Json -Depth 30
}
catch {
    $explorerStyleTransactionAudit = $null
}
$phase15TransactionAuditPassed =
    $explorerStyleTransactionAuditExitCode -eq 0 -and
    $null -ne $explorerStyleTransactionAudit -and
    $explorerStyleTransactionAudit.result -eq 'passed' -and
    $explorerStyleTransactionAudit.checkCount -eq 13 -and
    $explorerStyleTransactionAudit.passedCount -eq 13 -and
    $explorerStyleTransactionAudit.scenarioCount -eq 65 -and
    $explorerStyleTransactionAudit.scenarioPassedCount -eq 65 -and
    $explorerStyleTransactionAudit.simulatedWriteAttempts -and
    -not $explorerStyleTransactionAudit.platformWriteAttempted -and
    -not $explorerStyleTransactionAudit.propertyWriteSupported -and
    -not $explorerStyleTransactionAudit.propertyReadSupported -and
    -not $explorerStyleTransactionAudit.endpointAttempted -and
    -not $explorerStyleTransactionAudit.tapDllLoaded -and
    -not $explorerStyleTransactionAudit.executionSupported -and
    -not $explorerStyleTransactionAudit.activationPermitted -and
    $explorerStyleTransactionAudit.liveExplorer -eq 'not-run' -and
    -not $explorerStyleTransactionAudit.mutationPerformed
Add-Check `
    'phase15.style-transaction-executable-audit' `
    $phase15TransactionAuditPassed `
    'The reversible transaction core must pass 13/13 checks and 65/65 fault scenarios while every write remains simulated and no endpoint or DLL is touched.'

$phase16ReadBridgeStaticContract =
    $phase16Task.Contains(
        'REAL INTERFACE REVIEW OBJECT COMPLETE — UNLINKED AND NOT RUN'
    ) -and
    $explorerXamlReadBridgeContract.schemaVersion -eq 1 -and
    $explorerXamlReadBridgeContract.contractId -eq
        'jarvis-explorer-xaml-read-bridge-review-v1' -and
    $explorerXamlReadBridgeContract.lifecycleState -eq
        'unlinked-review-object-only' -and
    $explorerXamlReadBridgeContract.compileGate.reviewObjectValue -eq
        1 -and
    $explorerXamlReadBridgeContract.compileGate.shippingTapValue -eq
        0 -and
    -not $explorerXamlReadBridgeContract.compileGate.reviewObjectLinkedIntoTap -and
    $explorerXamlReadBridgeContract.readBoundary.siteInterface -eq
        'IXamlDiagnostics' -and
    $explorerXamlReadBridgeContract.readBoundary.serviceInterface -eq
        'IVisualTreeService2' -and
    $explorerXamlReadBridgeContract.readBoundary.requiredValueOrigin -eq
        'BaseValueSourceLocal' -and
    $explorerXamlReadBridgeContract.projection.outputSnapshotBytes -eq
        192 -and
    $explorerXamlReadBridgeContract.projection.exactRuntimeClassNameRequired -and
    $explorerXamlReadBridgeContract.ownership.releaseAttemptAndCompletionCountsMustMatch -and
    $explorerXamlReadBridgeContract.integration.portablePolicyHarnessScenarioCount -eq
        56 -and
    -not $explorerXamlReadBridgeContract.integration.windowsInterfaceReviewObjectExecuted -and
    -not $explorerXamlReadBridgeContract.integration.windowsInterfaceReviewObjectLinked -and
    $explorerXamlReadBridgeContract.approval.status -eq
        'blocked-fresh-host-package-required' -and
    -not $explorerXamlReadBridgeContract.approval.exactCommandGenerated -and
    -not $explorerXamlReadBridgeContract.propertyReadSupported -and
    -not $explorerXamlReadBridgeContract.propertyWriteSupported -and
    -not $explorerXamlReadBridgeContract.executionSupported -and
    -not $explorerXamlReadBridgeContract.readyForLiveConnection -and
    -not $explorerXamlReadBridgeContract.readyForExactApproval -and
    -not $explorerXamlReadBridgeContract.activationPermitted -and
    $explorerXamlReadBridgeContract.liveExplorer -eq 'not-run' -and
    -not $explorerXamlReadBridgeContract.mutationPerformed -and
    $explorerXamlReadBridgeContractSchema.additionalProperties -eq
        $false -and
    $explorerXamlReadBridgeSource.Contains(
        '#define JARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE 0'
    ) -and
    $explorerXamlReadBridgeSource.Contains(
        'service->GetPropertyValuesChain('
    ) -and
    $explorerXamlReadBridgeSource.Contains(
        'diagnostics->GetIInspectableFromHandle('
    ) -and
    $explorerXamlReadBridgeSource.Contains(
        'JARVIS_TAP_XAML_READ_RESULT_FOREIGN_OUTCOME_UNCERTAIN'
    ) -and
    -not $explorerXamlReadBridgeSource.Contains(
        'InitializeXamlDiagnosticsEx('
    ) -and
    -not $explorerXamlReadBridgeSource.Contains('SetProperty(') -and
    -not $explorerXamlReadBridgeSource.Contains('ClearProperty(')
Add-Check `
    'phase16.xaml-read-bridge-static-unlinked-contract' `
    $phase16ReadBridgeStaticContract `
    'Phase 16 must compile only a separate real-interface read review object, retain local exact-type and ownership gates, and remain unlinked and unapproved.'

$explorerXamlReadBridgeAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $explorerXamlReadBridgeAuditPath 2>&1
)
$explorerXamlReadBridgeAuditExitCode = $LASTEXITCODE
try {
    $explorerXamlReadBridgeAudit = (
        $explorerXamlReadBridgeAuditOutput -join [Environment]::NewLine
    ) | ConvertFrom-Json -Depth 30
}
catch {
    $explorerXamlReadBridgeAudit = $null
}
$phase16ReadBridgeAuditPassed =
    $explorerXamlReadBridgeAuditExitCode -eq 0 -and
    $null -ne $explorerXamlReadBridgeAudit -and
    $explorerXamlReadBridgeAudit.result -eq 'passed' -and
    $explorerXamlReadBridgeAudit.checkCount -eq 15 -and
    $explorerXamlReadBridgeAudit.passedCount -eq 15 -and
    $explorerXamlReadBridgeAudit.scenarioCount -eq 56 -and
    $explorerXamlReadBridgeAudit.scenarioPassedCount -eq 56 -and
    $explorerXamlReadBridgeAudit.policyHarnessBuilt -and
    $explorerXamlReadBridgeAudit.windowsReviewObjectBuilt -and
    -not $explorerXamlReadBridgeAudit.windowsReviewObjectExecuted -and
    $explorerXamlReadBridgeAudit.disabledObjectBuilt -and
    -not $explorerXamlReadBridgeAudit.endpointAttempted -and
    -not $explorerXamlReadBridgeAudit.tapDllLoaded -and
    -not $explorerXamlReadBridgeAudit.propertyReadSupported -and
    -not $explorerXamlReadBridgeAudit.propertyWriteSupported -and
    -not $explorerXamlReadBridgeAudit.executionSupported -and
    -not $explorerXamlReadBridgeAudit.readyForLiveConnection -and
    -not $explorerXamlReadBridgeAudit.readyForExactApproval -and
    -not $explorerXamlReadBridgeAudit.activationPermitted -and
    $explorerXamlReadBridgeAudit.liveExplorer -eq 'not-run' -and
    -not $explorerXamlReadBridgeAudit.mutationPerformed
Add-Check `
    'phase16.xaml-read-bridge-compile-and-policy-audit' `
    $phase16ReadBridgeAuditPassed `
    'The separate Windows read object must compile warning-free while 56/56 synthetic foreign-call observations pass without executing it or touching Explorer.'

$phase17SurfaceDiscoveryStaticContract =
    $phase17Task.Contains(
        'BOUNDED DISCOVERY CORE COMPLETE — CALLBACK UNLINKED AND NOT RUN'
    ) -and
    $explorerXamlSurfaceDiscoveryContract.schemaVersion -eq 1 -and
    $explorerXamlSurfaceDiscoveryContract.contractId -eq
        'jarvis-explorer-xaml-surface-discovery-review-v1' -and
    $explorerXamlSurfaceDiscoveryContract.lifecycleState -eq
        'offline-core-and-unlinked-callback-review-object' -and
    @($explorerXamlSurfaceDiscoveryContract.selectors).Count -eq 3 -and
    $explorerXamlSurfaceDiscoveryContract.boundedModel.maximumNodeCount -eq
        512 -and
    $explorerXamlSurfaceDiscoveryContract.boundedModel.maximumEventCount -eq
        2048 -and
    $explorerXamlSurfaceDiscoveryContract.boundedModel.maximumAncestorDepth -eq
        64 -and
    $explorerXamlSurfaceDiscoveryContract.boundedModel.fixedCapacity -and
    -not $explorerXamlSurfaceDiscoveryContract.boundedModel.heapAllocationRequired -and
    $explorerXamlSurfaceDiscoveryContract.callbackReview.interface -eq
        'IVisualTreeServiceCallback2' -and
    -not $explorerXamlSurfaceDiscoveryContract.callbackReview.linkedIntoTap -and
    -not $explorerXamlSurfaceDiscoveryContract.callbackReview.executed -and
    -not $explorerXamlSurfaceDiscoveryContract.callbackReview.subscriptionAttempted -and
    $explorerXamlSurfaceDiscoveryContract.readSession.requestCount -eq 9 -and
    $explorerXamlSurfaceDiscoveryContract.readSession.feedsPhase16ReadRequest -and
    -not $explorerXamlSurfaceDiscoveryContract.hostReviewPackage.exactCommandGenerated -and
    -not $explorerXamlSurfaceDiscoveryContract.callbackReviewObjectLinked -and
    -not $explorerXamlSurfaceDiscoveryContract.callbackReviewObjectExecuted -and
    -not $explorerXamlSurfaceDiscoveryContract.readyForLiveConnection -and
    -not $explorerXamlSurfaceDiscoveryContract.readyForExactApproval -and
    -not $explorerXamlSurfaceDiscoveryContract.executionSupported -and
    -not $explorerXamlSurfaceDiscoveryContract.activationPermitted -and
    $explorerXamlSurfaceDiscoveryContract.liveExplorer -eq 'not-run' -and
    -not $explorerXamlSurfaceDiscoveryContract.mutationPerformed -and
    $explorerXamlSurfaceDiscoveryContractSchema.additionalProperties -eq
        $false -and
    $explorerXamlSurfaceDiscoverySource.Contains(
        '#define JARVIS_COMPILE_REVIEWED_XAML_SURFACE_CALLBACK 0'
    ) -and
    $explorerXamlSurfaceDiscoverySource.Contains(
        'IVisualTreeServiceCallback2'
    ) -and
    $explorerXamlSurfaceDiscoverySource.Contains(
        'jarvis_tap_surface_discovery_build_read_request('
    ) -and
    -not $explorerXamlSurfaceDiscoverySource.Contains(
        'InitializeXamlDiagnosticsEx('
    ) -and
    -not $explorerXamlSurfaceDiscoverySource.Contains(
        'AdviseVisualTreeChange('
    ) -and
    -not $explorerXamlSurfaceDiscoverySource.Contains('SetProperty(') -and
    -not $explorerXamlSurfaceDiscoverySource.Contains('ClearProperty(')
Add-Check `
    'phase17.xaml-surface-discovery-static-unlinked-contract' `
    $phase17SurfaceDiscoveryStaticContract `
    'Phase 17 must bind the exact three candidate selectors to a fixed-capacity fail-closed discovery core and compile only an unlinked callback review object.'

$explorerXamlSurfaceDiscoveryAuditOutput = @(
    & pwsh `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $explorerXamlSurfaceDiscoveryAuditPath 2>&1
)
$explorerXamlSurfaceDiscoveryAuditExitCode = $LASTEXITCODE
try {
    $explorerXamlSurfaceDiscoveryAudit = (
        $explorerXamlSurfaceDiscoveryAuditOutput -join [Environment]::NewLine
    ) | ConvertFrom-Json -Depth 30
}
catch {
    $explorerXamlSurfaceDiscoveryAudit = $null
}
$phase17SurfaceDiscoveryAuditPassed =
    $explorerXamlSurfaceDiscoveryAuditExitCode -eq 0 -and
    $null -ne $explorerXamlSurfaceDiscoveryAudit -and
    $explorerXamlSurfaceDiscoveryAudit.result -eq 'passed' -and
    $explorerXamlSurfaceDiscoveryAudit.checkCount -eq 16 -and
    $explorerXamlSurfaceDiscoveryAudit.passedCount -eq 16 -and
    $explorerXamlSurfaceDiscoveryAudit.scenarioCount -eq 58 -and
    $explorerXamlSurfaceDiscoveryAudit.scenarioPassedCount -eq 58 -and
    $explorerXamlSurfaceDiscoveryAudit.harnessBuilt -and
    $explorerXamlSurfaceDiscoveryAudit.windowsReviewObjectBuilt -and
    -not $explorerXamlSurfaceDiscoveryAudit.windowsCallbackExecuted -and
    $explorerXamlSurfaceDiscoveryAudit.disabledObjectBuilt -and
    -not $explorerXamlSurfaceDiscoveryAudit.hostReviewPackageExecuted -and
    -not $explorerXamlSurfaceDiscoveryAudit.callbackSubscriptionAttempted -and
    -not $explorerXamlSurfaceDiscoveryAudit.propertyReadAttempted -and
    -not $explorerXamlSurfaceDiscoveryAudit.propertyWriteSupported -and
    -not $explorerXamlSurfaceDiscoveryAudit.executionSupported -and
    -not $explorerXamlSurfaceDiscoveryAudit.readyForLiveConnection -and
    -not $explorerXamlSurfaceDiscoveryAudit.readyForExactApproval -and
    -not $explorerXamlSurfaceDiscoveryAudit.activationPermitted -and
    $explorerXamlSurfaceDiscoveryAudit.liveExplorer -eq 'not-run' -and
    -not $explorerXamlSurfaceDiscoveryAudit.mutationPerformed
Add-Check `
    'phase17.xaml-surface-discovery-compile-and-fault-audit' `
    $phase17SurfaceDiscoveryAuditPassed `
    'The fixed-capacity discovery core must pass 58/58 synthetic topology scenarios and the real callback object must compile without being linked, subscribed or executed.'

$phase5NativeLeaseWatchdogContract =
    $iconSize.Contains(
        'L"\\Recovery\\m2-recovery-terminal.json"') -and
    $iconSize.Contains(
        'constexpr DWORD kRecoveryLeasePollIntervalMs = 1000') -and
    $iconSize.Contains(
        'constexpr ULONGLONG kRecoveryLeaseMaxAgeTicks') -and
    $iconSize.Contains('bool IsRecoveryLeaseHeartbeatFresh()') -and
    $iconSize.Contains(
        'FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT') -and
    $iconSize.Contains(
        'waitResult == WAIT_TIMEOUT') -and
    $iconSize.Contains(
        'LatchRuntimeBlocked(L"recovery-terminal heartbeat expired")') -and
    $iconSize.Contains(
        'FindFirstChangeNotificationW(') -and
    $iconSize.Contains(
        'g_stateDirectoryPath.data(), FALSE, FILE_NOTIFY_CHANGE_FILE_NAME')
Add-Check `
    'phase5.native-recovery-lease-watchdog' `
    $phase5NativeLeaseWatchdogContract `
    'M2 must keep recovery heartbeats below the non-recursive state-root watch and latch pass-through when the heartbeat expires.'

$phase5LabSchemaContract =
    $m2RecoveryLeaseLabSchema.properties.schemaVersion.const -eq 1 -and
    $m2RecoveryLeaseLabSchema.properties.receiptType.const -eq
        'jarvisv2-m2-recovery-lease-lab' -and
    $m2RecoveryLeaseLabSchema.properties.mode.const -eq
        'offline-read-only-inspection' -and
    $m2RecoveryLeaseLabSchema.properties.scenarioCount.const -eq 7 -and
    $m2RecoveryLeaseLabSchema.properties.activationPermitted.const -eq $false -and
    $m2RecoveryLeaseLabSchema.properties.liveExplorer.const -eq 'not-run' -and
    $m2RecoveryLeaseLabSchema.properties.mutationPerformed.const -eq $false -and
    $m2RecoveryLeaseLabSchema.properties.stateDirectoryTouched.const -eq $false
Add-Check `
    'phase5.recovery-lease-lab-schema' `
    $phase5LabSchemaContract `
    'The lab receipt must remain offline, read-only and incapable of claiming live Explorer evidence.'

$phase5LabScriptContract =
    $m2RecoveryLeaseLab.Contains("ScenarioId 'fresh-valid'") -and
    $m2RecoveryLeaseLab.Contains("ScenarioId 'stale-heartbeat'") -and
    $m2RecoveryLeaseLab.Contains("ScenarioId 'closing-state'") -and
    $m2RecoveryLeaseLab.Contains("ScenarioId 'plan-hash-mismatch'") -and
    $m2RecoveryLeaseLab.Contains("ScenarioId 'process-start-mismatch'") -and
    $m2RecoveryLeaseLab.Contains("ScenarioId 'source-identity-drift'") -and
    $m2RecoveryLeaseLab.Contains(
        "id = 'recovery-child-path-isolation'") -and
    $m2RecoveryLeaseLab.Contains('--lease-path $fixtureLeasePath') -and
    $m2RecoveryLeaseLab.Contains('stateDirectoryTouched = $false') -and
    -not [regex]::IsMatch(
        $m2RecoveryLeaseLab,
        '(?im)^\s*&\s*dotnet[^\r\n]*(?:clear-kill-switch|arm-kill-switch|restart-explorer)') -and
    -not [regex]::IsMatch(
        $m2RecoveryLeaseLab,
        '(?i)\b(?:Start-Service|Stop-Service|Set-Service|Stop-Process|taskkill)\b')
Add-Check `
    'phase5.recovery-lease-lab-fault-matrix' `
    $phase5LabScriptContract `
    'The lab must cover six deterministic lease failures plus non-recursive recovery-child path isolation using only read-only fixture paths.'

$phase4ObservationFaults =
    @($m2ObservationSchema.properties.faultInjection.enum)
$phase4ObservationSchemaContract =
    $m2ObservationSchema.properties.schemaVersion.const -eq 1 -and
    $m2ObservationSchema.properties.receiptType.const -eq
        'jarvisv2-m2-observation-rehearsal' -and
    $m2ObservationSchema.properties.mode.const -eq 'locked-rehearsal' -and
    $m2ObservationSchema.properties.activationPermitted.const -eq $false -and
    $m2ObservationSchema.properties.liveExplorer.const -eq 'not-run' -and
    $m2ObservationSchema.properties.mutationPerformed.const -eq $false -and
    $phase4ObservationFaults.Count -eq 7 -and
    $phase4ObservationFaults -contains 'none' -and
    $phase4ObservationFaults -contains 'kill-switch-missing' -and
    $phase4ObservationFaults -contains 'permit-present' -and
    $phase4ObservationFaults -contains 'windhawk-running' -and
    $phase4ObservationFaults -contains 'explorer-changed' -and
    $phase4ObservationFaults -contains 'module-mapped' -and
    $phase4ObservationFaults -contains 'elevated-cpu'
Add-Check `
    'phase4.observation-schema-stop-matrix' `
    $phase4ObservationSchemaContract `
    'The locked rehearsal receipt must distinguish the normal path from every required simulated stop condition.'

$phase4ObservationScriptContract =
    $m2ObservationRehearsal.Contains(
        '-ScriptPath $readinessScript') -and
    $m2ObservationRehearsal.Contains(
        '-ScriptPath $baselineScript') -and
    $m2ObservationRehearsal.Contains('$actualHost = [ordered]@{') -and
    $m2ObservationRehearsal.Contains('$evaluationState = [ordered]@{') -and
    $m2ObservationRehearsal.Contains(
        'injected-fault-did-not-trigger-expected-stop') -and
    $m2ObservationRehearsal.Contains(
        'artifacts\m2-observation-rehearsal\runs') -and
    $m2ObservationRehearsal.Contains('activationPermitted = $false') -and
    $m2ObservationRehearsal.Contains("liveExplorer = 'not-run'") -and
    $m2ObservationRehearsal.Contains('mutationPerformed = $false') -and
    -not [regex]::IsMatch(
        $m2ObservationRehearsal,
        '(?im)^\s*&\s*dotnet[^\r\n]*(?:clear-kill-switch|arm-kill-switch|restart-explorer)') -and
    -not [regex]::IsMatch(
        $m2ObservationRehearsal,
        '(?i)\b(?:Start-Service|Stop-Service|Set-Service|Stop-Process|taskkill)\b')
Add-Check `
    'phase4.observation-rehearsal-readonly' `
    $phase4ObservationScriptContract `
    'Fault injection must alter only an evaluation copy while locked host sampling and all receipts remain read-only.'

foreach ($module in @(
    [pscustomobject]@{ Id = 'jarvis-native-taskbar'; Text = $styler },
    [pscustomobject]@{ Id = 'jarvis-taskbar-icon-size'; Text = $iconSize }
)) {
    Test-Pattern "module.$($module.Id).id" $module.Text ("(?m)^// @id\s+{0}\s*$" -f [regex]::Escape($module.Id)) 'Windhawk mod id must match the module allowlist.'
    Test-Pattern "module.$($module.Id).license" $module.Text '(?m)^// @license\s+GPL-3\.0\s*$' 'Windhawk metadata must declare GPL-3.0.'
    Test-Pattern "module.$($module.Id).host" $module.Text '(?m)^// @include\s+%SystemRoot%\\explorer\.exe\s*$' 'The only injection host must be Windows Explorer.'
    Test-Pattern "module.$($module.Id).architecture" $module.Text '(?m)^// @architecture\s+amd64\s*$' 'Only the audited AMD64 architecture is allowed.'
    $killSwitchContract =
        $module.Text.Contains('JARVIS2\\disabled.flag') -or
        ($module.Text.Contains('kStateDirectorySuffix') -and
         $module.Text.Contains('kKillSwitchSuffix') -and
         $module.Text.Contains('disabled.flag'))
    Add-Check "module.$($module.Id).kill-switch" $killSwitchContract 'Every in-process module needs the common emergency switch.'
    $activationPermitContract =
        $module.Text.Contains('active-module.txt') -and
        $module.Text.Contains(('"{0}"' -f $module.Id)) -and
        $module.Text.Contains('FILE_FLAG_OPEN_REPARSE_POINT') -and
        $module.Text.Contains('FileDispositionInfo') -and
        [regex]::IsMatch($module.Text, '5(?:ULL)?\s*\*\s*60') -and
        $module.Text.Contains('Local\\JARVIS2.StateGate.v1')
    Add-Check "module.$($module.Id).activation-permit" $activationPermitContract 'Every module must use the same fresh, exact, one-shot active-module.txt contract under the state gate.'
    Test-NoPattern "module.$($module.Id).no-legacy-permit-payload" $module.Text 'JARVIS2-ACTIVATION-PERMIT-V1|activation-permits\\' 'Legacy per-module permit files and multi-line payloads are forbidden.'
    Test-NoPattern "module.$($module.Id).no-layered-overlay" $module.Text 'WS_EX_LAYERED|UpdateLayeredWindow|SetLayeredWindowAttributes' 'Native modules must not create a layered overlay.'
    Test-NoPattern "module.$($module.Id).no-self-restart" $module.Text 'taskkill|TerminateProcess|ShellExecuteW\(|CreateProcessW?\(' 'Injected modules must leave Explorer recovery to the supervisor.'
    Test-Pattern "module.$($module.Id).stable-link-timestamp" $module.Text '(?m)^// @compilerOptions [^\r\n]*-Wl,--no-insert-timestamp(?:\s|$)' 'Every native module must suppress the known volatile PE and debug-directory timestamps.'
}

Test-Pattern 'styler.build-gate' $styler ("constexpr DWORD kValidatedBuild = {0};" -f $compatibility.host.minimumWindowsBuild) 'M1 source build gate must match compatibility.json.'
Test-Pattern 'styler.ubr-gate' $styler ("constexpr DWORD kValidatedUbr = {0};" -f $baseline.ubr) 'M1 source UBR gate must match compatibility.json.'
Test-Pattern 'styler.explorer-version-gate' $styler 'kValidatedExplorerVersion\{10, 0, 26100, 8875\}' 'M1 checks the Explorer product version.'
Test-Pattern 'styler.taskbar-version-gate' $styler 'kValidatedTaskbarViewVersion\{2605, 22000, 400, 0\}' 'M1 checks Taskbar.View product version.'
Test-NoPattern 'styler.no-stats-call' $styler '(?m)^\s*StartStatsTimer\(\);\s*$' 'The JARVIS2 fork must not start inherited theme telemetry.'

$stylerInitSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'BOOL Wh_ModInit()' `
    -EndMarker 'void Wh_ModAfterInit()'
$stylerBeforeUninitSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void Wh_ModBeforeUninit()' `
    -EndMarker 'void Wh_ModUninit()'
$stylerUninitSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void Wh_ModUninit()' `
    -EndMarker 'void Wh_ModSettingsChanged()'
$stylerSettingsChangedIndex = $styler.IndexOf(
    'void Wh_ModSettingsChanged()',
    [StringComparison]::Ordinal
)
$stylerSettingsChangedSection = if ($stylerSettingsChangedIndex -ge 0) {
    $styler.Substring($stylerSettingsChangedIndex)
}
else {
    ''
}
$stylerVisualTreeChangeSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'HRESULT VisualTreeWatcher::OnVisualTreeChange' `
    -EndMarker 'HRESULT VisualTreeWatcher::OnElementStateChanged'
$stylerSetSiteSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'HRESULT WindhawkTAP::SetSite' `
    -EndMarker 'HRESULT WindhawkTAP::GetSite'
$stylerGetSiteSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'HRESULT WindhawkTAP::GetSite' `
    -EndMarker '#pragma endregion  // tap_cpp'
$stylerInitializeForThreadSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'bool InitializeForCurrentThread(' `
    -EndMarker 'void InitializeSettingsAndTap()'
$stylerUninitializeForThreadSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'HRESULT UninitializeForCurrentThread() noexcept' `
    -EndMarker 'bool UninitializeSettingsAndTap()'
$stylerUiLifecycleSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'HRESULT CompleteUiThreadCleanupOnCurrentThread(' `
    -EndMarker 'void InitializeSettingsAndTap()'
$stylerInitializeSettingsSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void InitializeSettingsAndTap()' `
    -EndMarker 'using RunFromWindowThreadProc_t'
$stylerOnWindowCreatedSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void OnWindowCreated(HWND hWnd,' `
    -EndMarker 'enum class JarvisActivationState'
$stylerXamlHostEnumerationSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'constexpr std::size_t kMaxXamlHostWindowSnapshot' `
    -EndMarker 'HWND FindCurrentProcessTaskbarWnd()'
$stylerAfterInitSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void Wh_ModAfterInit()' `
    -EndMarker 'void Wh_ModBeforeUninit()'
$stylerDestroyWindowSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'BOOL WINAPI DestroyWindow_Hook(' `
    -EndMarker 'PFN_INITIALIZE_XAML_DIAGNOSTICS_EX InitializeXamlDiagnosticsEx_Original'
$stylerInjectTapSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'class InjectWindhawkTapFlagGuard' `
    -EndMarker '#pragma endregion  // api_cpp'
$stylerEnergySaverCallbackSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void CALLBACK XamlBlurBrush::OnEnergySaverRegistryChanged' `
    -EndMarker 'XamlBlurBrush::~XamlBlurBrush()'
$stylerActivationLatchSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void LatchJarvisActivationQuiesced(PCWSTR reason)' `
    -EndMarker 'using CreateWindowExW_t'
$stylerComExportSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker '_Use_decl_annotations_ STDAPI DllGetClassObject(' `
    -EndMarker '#pragma endregion  // module_cpp'
$stylerApplyCustomizationsSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void ApplyCustomizations(InstanceHandle handle,' `
    -EndMarker 'void CleanupCustomizations' `
    -UseLastStart
$stylerPropagateSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void PropagateStyleVariableChange' `
    -EndMarker 'void SetStyleVariableIfChangedAndPropagate'
$stylerRefreshResourcesSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void RefreshThemeResourceEntries()' `
    -EndMarker 'std::vector<ResourceVariableEntry> ProcessResourceVariablesFromSettings'
$stylerCaptureCallbacksSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void SetUpCapturesForElement' `
    -EndMarker 'void RestoreCapturesForElement'
$stylerVisualStateCallbacksSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void ApplyCustomizationsForVisualStateGroup' `
    -EndMarker 'void RestoreCustomizationsForVisualStateGroup'
$stylerSizeWorkaroundSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void HookFirstTaskbarFrameLayoutWorkaround' `
    -EndMarker 'void UnhookFirstTaskbarFrameLayoutWorkaround'
$stylerSetOrClearSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void SetOrClearValue' `
    -EndMarker 'std::wstring EscapeXmlAttribute'
$stylerXamlBlurConnectedSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void XamlBlurBrush::OnConnected()' `
    -EndMarker 'void XamlBlurBrush::OnDisconnected()'
$stylerXamlBlurDestructorSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'XamlBlurBrush::~XamlBlurBrush()' `
    -EndMarker 'void XamlBlurBrush::OnConnected()'
$stylerXamlBlurDisconnectedSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void XamlBlurBrush::OnDisconnected()' `
    -EndMarker 'void XamlBlurBrush::RefreshThemeTint()'
$stylerXamlBlurClassSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'class XamlBlurBrush :' `
    -EndMarker 'struct XamlBlurBrushRegistryWaitContext'
$stylerXamlBlurConstructorSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'XamlBlurBrush::XamlBlurBrush(' `
    -EndMarker 'void CALLBACK XamlBlurBrush::OnEnergySaverRegistryChanged'
$stylerXamlShouldUseFallbackSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'bool XamlBlurBrush::ShouldUseFallback() const' `
    -EndMarker 'void XamlBlurBrush::RefreshBrush()'
$stylerXamlRegistryBundleSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'KernelCapabilityCloseOutcome CloseOrRetainRegistryKey(' `
    -EndMarker 'void LogRetainedKernelCapabilityReceipts() noexcept'
$stylerXamlRegistryKeyAcquireSection = Get-SourceSlice `
    -Text $stylerXamlBlurConstructorSection `
    -StartMarker 'm_powerKeyOwnerIdentity = ReserveKernelCapability(' `
    -EndMarker 'm_regNotifyEventOwnerIdentity = ReserveKernelCapability('
$stylerXamlRegistryEventAcquireSection = Get-SourceSlice `
    -Text $stylerXamlBlurConstructorSection `
    -StartMarker 'm_regNotifyEventOwnerIdentity = ReserveKernelCapability(' `
    -EndMarker 'm_regWaitOwnerIdentity = ReserveKernelCapability('
$stylerXamlRegistryWaitAcquireSection = Get-SourceSlice `
    -Text $stylerXamlBlurConstructorSection `
    -StartMarker 'm_regWaitOwnerIdentity = ReserveKernelCapability(' `
    -EndMarker 'RequirePermanentUnloadSafetyPin('
$stylerXamlConstructorRetainedBundleSection = Get-SourceSlice `
    -Text $stylerXamlRegistryWaitAcquireSection `
    -StartMarker 'const bool bundleRetained =' `
    -EndMarker '            return;'
$stylerXamlDestructorRetainedBundleSection = Get-SourceSlice `
    -Text $stylerXamlBlurDestructorSection `
    -StartMarker 'const bool bundleRetained =' `
    -EndMarker '                    m_regWaitHandle = nullptr;'
$stylerProjectedCallbackHelperSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'template <typename Callback>' `
    -EndMarker 'template <typename... Args>'
$stylerElementMutationGuardSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'class ScopedElementPropertyMutation {' `
    -EndMarker 'thread_local std::list<'
$stylerSetupImageTrackingSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void SetupImageBrushTracking(' `
    -EndMarker 'void SetOrClearValue('
$stylerModuleReferenceOwnerSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'class NoThrowModuleReferenceOwner {' `
    -EndMarker 'HMODULE GetCurrentModuleHandle()'
$stylerNoThrowGitOwnerSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'class NoThrowGlobalInterfaceTableOwner {' `
    -EndMarker 'std::atomic<std::uint64_t> g_externalComReferenceReleaseFailures'
$stylerRetainUnknownComSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void RetainUnknownExternalComOutcome(PCWSTR reason) noexcept' `
    -EndMarker 'HRESULT QueryExternalComInterfaceNoThrow('
$stylerProvisionalGuardSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'class ProvisionalGitCookieGuard {' `
    -EndMarker 'bool RetryProvisionalGitQuarantine() noexcept'
$stylerRetryProvisionalSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'bool RetryProvisionalGitQuarantineFromInitializedApartment() noexcept' `
    -EndMarker 'void LogProvisionalGitQuarantineReceipts() noexcept'
$stylerEmergencyWatcherSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void VisualTreeWatcher::EmergencyRetainAndFailClosed(' `
    -EndMarker 'bool VisualTreeWatcher::AcceptProtocolStatus('
$stylerRetainGitSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void VisualTreeWatcher::RetainVisualTreeServiceGit(' `
    -EndMarker 'VisualTreeWatcher::CloseVisualTreeServiceFromCurrentApartment()'
$stylerGetGitSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'HRESULT VisualTreeWatcher::GetVisualTreeServiceForCurrentApartment(' `
    -EndMarker 'bool VisualTreeWatcher::WaitForVisualTreeServiceLeases('
$stylerFactorySection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'struct SimpleFactory :' `
    -EndMarker '#pragma endregion  // simplefactory_hpp'
$stylerCleanupStepsSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'UiThreadCleanupExecution RunUiThreadCleanupSteps(' `
    -EndMarker 'HRESULT CompleteUiThreadCleanupOnCurrentThread('
$stylerStatsTimerSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'PTP_TIMER g_statsTimer' `
    -EndMarker 'void StopStatsTimer()'
$stylerReleaseValidatedModuleSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void ReleaseValidatedTaskbarViewModule() noexcept' `
    -EndMarker 'bool IsCurrentProcessTheVerifiedDesktopShell()'
$stylerHostBinarySection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'bool AreHostBinaryVersionsAllowed()' `
    -EndMarker 'bool IsWindowsBuildAllowed()'

$permitConsumeIndex = $stylerInitSection.IndexOf(
    'ConsumeActivationPermit(',
    [StringComparison]::Ordinal
)
$watcherStartIndex = $stylerInitSection.IndexOf(
    'StartKillSwitchWatcher()',
    [StringComparison]::Ordinal
)
$setFunctionHookIndex = $stylerInitSection.IndexOf(
    'WindhawkUtils::SetFunctionHook(',
    [StringComparison]::Ordinal
)
$authorizeCasIndex = $stylerInitSection.IndexOf(
    'JarvisActivationState expectedActivationState =',
    [StringComparison]::Ordinal
)
$activateCasIndex = $stylerInitSection.IndexOf(
    'JarvisActivationState expectedState = JarvisActivationState::kAuthorized;',
    [StringComparison]::Ordinal
)
$stylerInitOrderValid =
    $permitConsumeIndex -ge 0 -and
    $authorizeCasIndex -gt $permitConsumeIndex -and
    $watcherStartIndex -gt $authorizeCasIndex -and
    $setFunctionHookIndex -gt $watcherStartIndex -and
    $activateCasIndex -gt $setFunctionHookIndex
$stylerAuthorizedToActiveCas = [regex]::IsMatch(
    $stylerInitSection,
    '(?s)JarvisActivationState\s+expectedState\s*=\s*JarvisActivationState::kAuthorized\s*;\s*if\s*\(\s*!g_jarvisActivationState\.compare_exchange_strong\(\s*expectedState,\s*JarvisActivationState::kActive\b'
)

$stylerNoNewXamlWorkClauses = @(
    [pscustomobject]@{
        name = 'state-directory-watcher'
        passed =
            $styler.Contains('constexpr wchar_t kStateDirectorySuffix[]') -and
            $styler.Contains('DWORD WINAPI KillSwitchWatcherThread') -and
            $styler.Contains('FindFirstChangeNotificationW(') -and
            $styler.Contains('FILE_NOTIFY_CHANGE_FILE_NAME') -and
            $styler.Contains('LatchJarvisActivationQuiesced(')
    }
    [pscustomobject]@{
        name = 'permit-watcher-hook-cas-order'
        passed = $stylerInitOrderValid -and $stylerAuthorizedToActiveCas
    }
    [pscustomobject]@{
        name = 'no-unconditional-active-store'
        passed = -not [regex]::IsMatch(
            $styler,
            'g_jarvisActivationState\s*\.\s*store\s*\(\s*JarvisActivationState::kActive\b'
        )
    }
    [pscustomobject]@{
        name = 'watcher-stop-before-and-during-uninit'
        passed =
            $stylerBeforeUninitSection.Contains('StopKillSwitchWatcher()') -and
            $stylerUninitSection.Contains('StopKillSwitchWatcher()')
    }
    [pscustomobject]@{
        name = 'no-force-thread-termination'
        passed = -not [regex]::IsMatch($styler, '\bTerminateThread\s*\(')
    }
    [pscustomobject]@{
        name = 'initialize-current-thread-guard'
        passed = $stylerInitializeForThreadSection.Contains(
            'IsJarvisActivationActive()'
        )
    }
    [pscustomobject]@{
        name = 'initialize-settings-and-tap-guard'
        passed = $stylerInitializeSettingsSection.Contains(
            'IsJarvisActivationActive()'
        )
    }
    [pscustomobject]@{
        name = 'window-created-guard'
        passed = $stylerOnWindowCreatedSection.Contains(
            'IsJarvisActivationActive()'
        )
    }
    [pscustomobject]@{
        name = 'tap-site-guard'
        passed = $stylerSetSiteSection.Contains('IsJarvisActivationActive()')
    }
    [pscustomobject]@{
        name = 'visual-tree-add-guard'
        passed = [regex]::IsMatch(
            $stylerVisualTreeChangeSection,
            'mutationType\s*==\s*Add\s*&&\s*!IsJarvisActivationActive\(\)'
        )
    }
    [pscustomobject]@{
        name = 'apply-customizations-guard'
        passed = $stylerApplyCustomizationsSection.Contains(
            'IsJarvisActivationActive()'
        )
    }
    [pscustomobject]@{
        name = 'style-variable-propagation-guard'
        passed = $stylerPropagateSection.Contains('IsJarvisActivationActive()')
    }
    [pscustomobject]@{
        name = 'resource-refresh-guard'
        passed = $stylerRefreshResourcesSection.Contains(
            'IsJarvisActivationActive()'
        )
    }
    [pscustomobject]@{
        name = 'capture-property-callback-guard'
        passed = [regex]::IsMatch(
            $stylerCaptureCallbacksSection,
            '(?s)propertyChangedToken\s*=\s*elementDo\.RegisterPropertyChangedCallback\(.*?noexcept\s*\{.*?RunProjectedCallbackNoThrow\(.*?!IsJarvisActivationActive\(\).*?SetStyleVariableIfChangedAndPropagate'
        )
    }
    [pscustomobject]@{
        name = 'capture-size-callback-guard'
        passed = [regex]::IsMatch(
            $stylerCaptureCallbacksSection,
            '(?s)captureSizeChangedToken\s*=\s*element\.SizeChanged\(.*?noexcept\s*\{.*?RunProjectedCallbackNoThrow\(.*?!IsJarvisActivationActive\(\).*?SetStyleVariableIfChangedAndPropagate'
        )
    }
    [pscustomobject]@{
        name = 'property-reapply-callback-guard'
        passed = [regex]::IsMatch(
            $stylerVisualStateCallbacksSection,
            '(?s)propertyCustomizationState\.propertyChangedToken\s*=.*?RegisterPropertyChangedCallback\(.*?noexcept\s*\{.*?RunProjectedCallbackNoThrow\(.*?!IsJarvisActivationActive\(\).*?Re-applying style'
        )
    }
    [pscustomobject]@{
        name = 'visual-state-reapply-callback-guard'
        passed = [regex]::IsMatch(
            $stylerVisualStateCallbacksSection,
            '(?s)visualStateGroup\.CurrentStateChanged\(.*?noexcept\s*\{.*?RunProjectedCallbackNoThrow\(.*?!IsJarvisActivationActive\(\).*?Re-applying all styles'
        )
    }
    [pscustomobject]@{
        name = 'layout-size-callback-guard'
        passed = [regex]::IsMatch(
            $stylerSizeWorkaroundSection,
            '(?s)g_workaroundSizeChangedRevoker\s*=\s*element\.SizeChanged\(.*?noexcept\s*\{.*?RunProjectedCallbackNoThrow\(.*?!IsJarvisActivationActive\(\).*?ScopedElementPropertyMutation.*?SetOrClearValue'
        )
    }
    [pscustomobject]@{
        name = 'delayed-setter-callback-guard'
        passed = [regex]::IsMatch(
            $stylerSetOrClearSection,
            '(?s)TryRunAsync\(.*?noexcept\s*\{.*?RunProjectedCallbackNoThrow\(.*?Running delayed SetValue for.*?if\s*\(\s*IsJarvisActivationActive\(\)\s*\).*?ScopedElementPropertyMutation.*?elementDo\.SetValue'
        )
    }
    [pscustomobject]@{
        name = 'xaml-blur-reconnect-callback-guard'
        passed = [regex]::IsMatch(
            $stylerXamlBlurConnectedSection,
            '(?s)TapLifecycleScope\s+callbackScope\(\s*true,\s*TapLifecycleEntryKind::kCallback\s*\).*?if\s*\(\s*!callbackScope\s*\|\|\s*!IsJarvisActivationActive\(\)\s*\).*?CompositionBrush\('
        )
    }
    [pscustomobject]@{
        name = 'remote-image-tracking-activation-guard'
        passed =
            [regex]::IsMatch(
                $stylerSetupImageTrackingSection,
                '(?s)void\s+SetupImageBrushTracking\(.*?\{\s*if\s*\(\s*!IsJarvisActivationActive\(\)\s*\)\s*\{\s*return\s*;'
            ) -and
            [regex]::IsMatch(
                $stylerSetOrClearSection,
                '(?s)bool\s+trackRemoteImages\s*=\s*true.*?if\s*\(\s*trackRemoteImages\s*\).*?SetupImageBrushTracking\('
            ) -and
            [regex]::Matches(
                $styler,
                '(?s)originalValue\).*?/\*trackRemoteImages=\*/false'
            ).Count -ge 2
    }
)
$stylerNoNewXamlWorkFailures = @(
    $stylerNoNewXamlWorkClauses |
        Where-Object { -not $_.passed } |
        ForEach-Object { $_.name }
)
$stylerNoNewXamlWorkDetail =
    'M1 must latch into a file-I/O-free no-new-XAML-work state; this gate does not prove UI-thread restoration.'
if ($stylerNoNewXamlWorkFailures.Count -gt 0) {
    $stylerNoNewXamlWorkDetail +=
        ' Missing clauses: ' + ($stylerNoNewXamlWorkFailures -join ', ') + '.'
}
Add-Check `
    'styler.no-new-xaml-work' `
    ($stylerNoNewXamlWorkFailures.Count -eq 0) `
    $stylerNoNewXamlWorkDetail

$stylerWatcherClassSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'class VisualTreeWatcher :' `
    -EndMarker '#pragma endregion  // visualtreewatcher_hpp'
$stylerWatcherConstructorSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'VisualTreeWatcher::VisualTreeWatcher(' `
    -EndMarker 'VisualTreeWatcher::~VisualTreeWatcher()'
$stylerWatcherDestructorSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'VisualTreeWatcher::~VisualTreeWatcher()' `
    -EndMarker 'bool VisualTreeWatcher::AdviseWorkerStarted() const'
$stylerWatcherThreadMethodsSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'bool VisualTreeWatcher::AdviseWorkerStarted() const' `
    -EndMarker 'bool VisualTreeWatcher::UnadviseVisualTreeChange()'
$stylerWatcherUnadviseSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'bool VisualTreeWatcher::UnadviseVisualTreeChange()' `
    -EndMarker 'HRESULT VisualTreeWatcher::OnVisualTreeChange'
$stylerWatcherGlobalsSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'winrt::com_ptr<VisualTreeWatcher> g_visualTreeWatcher;' `
    -EndMarker '// {C85D8CC7-5463-40E8-A432-F5916B6427E5}'
$stylerTapLifecycleSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'enum class TapLifecycleEntryKind' `
    -EndMarker 'class VisualTreeWatcher :'
$stylerRunFromWindowThreadSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'using RunFromWindowThreadProc_t' `
    -EndMarker 'void OnWindowCreated(HWND hWnd,'
$stylerUnloadSafetyPinSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'std::atomic<HMODULE> g_unloadSafetyModulePin' `
    -EndMarker 'struct JarvisFileVersion'
$stylerStartKillSwitchWatcherSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'bool StartKillSwitchWatcher()' `
    -EndMarker 'bool StopKillSwitchWatcher()'
$stylerStopKillSwitchWatcherSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'bool StopKillSwitchWatcher()' `
    -EndMarker 'UiUniqueHandle AcquireJarvisStateGate()'
$stylerKernelCapabilitySection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'enum class RetainedKernelCapabilityKind' `
    -EndMarker 'class NoThrowModuleReferenceOwner {'
$stylerUiUniqueHandleSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'class UiUniqueHandle {' `
    -EndMarker 'constexpr std::uint32_t kUiThreadCapability'
$stylerStateGateSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'UiUniqueHandle AcquireJarvisStateGate()' `
    -EndMarker 'bool IsEmergencyKillSwitchArmed()'
$stylerActivationPermitSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'UiUniqueHandle OpenValidatedActivationPermit()' `
    -EndMarker 'bool HasExpectedProductVersion('
$stylerCurrentUiThreadIdentitySection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'bool GetCurrentUiThreadIdentity(' `
    -EndMarker 'std::shared_ptr<UiThreadRuntimeRecord>'
$stylerUiHandleCapabilitySection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'struct UiThreadRuntimeRecord {' `
    -EndMarker 'struct UiThreadCleanupExecution {'
$stylerWatcherStopEventAcquisitionSection = Get-SourceSlice `
    -Text $stylerStartKillSwitchWatcherSection `
    -StartMarker 'g_killSwitchWatcherStopEventOwnerIdentity =' `
    -EndMarker 'g_killSwitchWatcherChangeNotificationOwnerIdentity ='
$stylerWatcherChangeAcquisitionSection = Get-SourceSlice `
    -Text $stylerStartKillSwitchWatcherSection `
    -StartMarker 'g_killSwitchWatcherChangeNotificationOwnerIdentity =' `
    -EndMarker 'g_killSwitchWatcherThreadOwnerIdentity ='
$stylerWatcherThreadAcquisitionSection = Get-SourceSlice `
    -Text $stylerStartKillSwitchWatcherSection `
    -StartMarker 'g_killSwitchWatcherThreadOwnerIdentity =' `
    -EndMarker 'g_killSwitchWatcherThreadExitConfirmed = false;'
$stylerStyleVariableStateSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'struct StyleVariableState {' `
    -EndMarker 'thread_local bool g_elementPropertyModifying;'

$adviseAddRefIndex = $stylerWatcherConstructorSection.IndexOf(
    'AddRef();',
    [StringComparison]::Ordinal
)
$adviseCreateThreadIndex = $stylerWatcherConstructorSection.IndexOf(
    'm_adviseThread = CreateThread(',
    [StringComparison]::Ordinal
)
$pinAcquireIndex = $stylerInitSection.IndexOf(
    'AcquireUnloadSafetyModulePin()',
    [StringComparison]::Ordinal
)
$watcherStartFailureMatch = [regex]::Match(
    $stylerInitSection,
    '(?s)if\s*\(\s*!StartKillSwitchWatcher\(\)\s*\)\s*\{.*?ReleaseUnloadSafetyModulePin\(\)\s*;.*?return FALSE\s*;\s*\}'
)
$stylerInitAfterWatcherStarted = if ($watcherStartFailureMatch.Success) {
    $stylerInitSection.Substring(
        $watcherStartFailureMatch.Index + $watcherStartFailureMatch.Length
    )
}
else {
    ''
}
$postWatcherPinReleaseCount = [regex]::Matches(
    $stylerInitAfterWatcherStarted,
    '\bReleaseUnloadSafetyModulePin\s*\(\s*\)'
).Count
$postWatcherGuardedPinReleaseCount = [regex]::Matches(
    $stylerInitAfterWatcherStarted,
    '(?s)if\s*\(\s*StopKillSwitchWatcher\(\)\s*\)\s*\{\s*ReleaseUnloadSafetyModulePin\(\)\s*;'
).Count
$uninitWindowDispatchCount = [regex]::Matches(
    $stylerUninitSection,
    '\bRunFromWindowThread\s*\('
).Count
$checkedUninitWindowDispatchCount = [regex]::Matches(
    $stylerUninitSection,
    'if\s*\(\s*!RunFromWindowThread\s*\('
).Count

$stylerThreadedUnloadClauses = @(
    [pscustomobject]@{
        name = 'advise-ref-before-owned-thread'
        passed =
            $adviseAddRefIndex -ge 0 -and
            $adviseCreateThreadIndex -gt $adviseAddRefIndex -and
            $stylerWatcherClassSection.Contains(
                'HANDLE m_adviseThread = nullptr;'
            ) -and
            $stylerWatcherClassSection.Contains(
                'class WorkerReferenceGuard'
            ) -and
            $stylerWatcherClassSection.Contains(
                'm_watcher->Release();'
            ) -and
            $stylerWatcherConstructorSection.Contains(
                'WorkerReferenceGuard workerReference(watcher);'
            )
    }
    [pscustomobject]@{
        name = 'advise-and-unadvise-exception-containment'
        passed =
            [regex]::IsMatch(
                $stylerWatcherConstructorSection,
                '(?s)m_adviseThread\s*=\s*CreateThread\(.*?\[\]\(LPVOID lpParam\)\s+noexcept\s*->\s*DWORD.*?WorkerReferenceGuard\s+workerReference\(watcher\).*?try\s*\{.*?if\s*\(\s*!lifecycleScope\s*\).*?RetainVisualTreeServiceGit\(.*?AdviseVisualTreeChange\(\s*watcher\s*\).*?catch\s*\(\s*\.\.\.\s*\).*?RetainVisualTreeServiceGit\(.*?FailClosedForeignAbiException\('
            ) -and
            $stylerWatcherClassSection.Contains(
                'HRESULT CloseVisualTreeServiceFromCurrentApartment() noexcept;'
            ) -and
            [regex]::IsMatch(
                $stylerWatcherUnadviseSection,
                '(?s)m_unadviseThread\s*=\s*CreateThread\(.*?\[\]\(LPVOID lpParam\)\s+noexcept\s*->\s*DWORD.*?WorkerReferenceGuard\s+workerReference\(watcher\).*?try\s*\{.*?if\s*\(\s*!lifecycleScope\s*\).*?RetainVisualTreeServiceGit\(.*?CloseVisualTreeServiceFromCurrentApartment\(\).*?catch\s*\(\s*\.\.\.\s*\).*?RetainVisualTreeServiceGit\(.*?FailClosedForeignAbiException\('
            )
    }
    [pscustomobject]@{
        name = 'advise-worker-join-before-uninit'
        passed = [regex]::IsMatch(
            $stylerBeforeUninitSection,
            '(?s)SnapshotAllVisualTreeWatchers\(\).*?WaitForAdviseThread\(\s*5000\s*\)'
        )
    }
    [pscustomobject]@{
        name = 'watcher-member-and-global-locks'
        passed =
            $stylerWatcherClassSection.Contains(
                'mutable std::mutex m_adviseThreadMutex;'
            ) -and
            $stylerWatcherClassSection.Contains(
                'mutable std::mutex m_unadviseThreadMutex;'
            ) -and
            [regex]::Matches(
                $stylerWatcherThreadMethodsSection,
                'lock\(m_adviseThreadMutex\)'
            ).Count -ge 2 -and
            [regex]::Matches(
                $stylerWatcherUnadviseSection,
                'lock\(m_unadviseThreadMutex\)'
            ).Count -ge 1 -and
            $stylerWatcherThreadMethodsSection.Contains(
                'bool VisualTreeWatcher::WaitForUnadviseThread'
            ) -and
            $stylerWatcherGlobalsSection.Contains(
                'std::mutex g_visualTreeWatcherMutex;'
            ) -and
            [regex]::Matches(
                $stylerWatcherGlobalsSection,
                'lock\s*\(\s*g_visualTreeWatcherMutex\s*\)'
            ).Count -ge 3
    }
    [pscustomobject]@{
        name = 'bounded-heap-window-thread-dispatch'
        passed =
            $stylerRunFromWindowThreadSection.Contains(
                'struct RunFromWindowThreadContext'
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'WindowThreadDispatchCompactReceipt'
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'std::array<WindowThreadDispatchCompactReceipt, 64>'
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'jarvis::resource_protocol::DispatchSlot protocol;'
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'g_nextWindowThreadDispatchId.fetch_add('
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'RegisterWindowThreadDispatchResources(hook, context)'
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'ClaimRunFromWindowThreadContext('
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'cwp->lParam, callbackDispatchId'
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'CancelRunFromWindowThreadContext('
            ) -and
            [regex]::IsMatch(
                $stylerRunFromWindowThreadSection,
                '(?s)if\s*\(\s*g_pendingWindowThreadContext\s*!=\s*context.*?context->dispatchId\s*!=\s*dispatchId\s*\)\s*\{\s*return\s+(?:nullptr|\{\})\s*;'
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'constexpr UINT kWindowThreadDispatchTimeoutMs = 5000;'
            ) -and
            $stylerRunFromWindowThreadSection.Contains('SendMessageTimeoutW(') -and
            $stylerRunFromWindowThreadSection.Contains(
                'SMTO_ABORTIFHUNG | SMTO_BLOCK | SMTO_ERRORONEXIT'
            ) -and
            -not [regex]::IsMatch(
                $stylerRunFromWindowThreadSection,
                '\bSendMessageW\s*\('
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'g_windowThreadDispatchPoisoned.store(true'
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'IsWindowThreadDispatchPoisoned() ||'
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'Cross-thread dispatch parameters aren''t supported'
            ) -and
            [regex]::Matches(
                $stylerRunFromWindowThreadSection,
                'IsSupportedJarvisBootstrapWindow\('
            ).Count -ge 3 -and
            -not $stylerRunFromWindowThreadSection.Contains(
                'std::vector<RunFromWindowThreadContext'
            ) -and
            -not $stylerRunFromWindowThreadSection.Contains(
                'std::vector<HHOOK>'
            )
    }
    [pscustomobject]@{
        name = 'unadvise-external-com-bounded-worker'
        passed =
            $stylerWatcherClassSection.Contains(
                'HANDLE m_unadviseThread = nullptr;'
            ) -and
            $stylerWatcherClassSection.Contains(
                'std::atomic<HRESULT> m_unadviseResult{E_PENDING};'
            ) -and
            $stylerWatcherUnadviseSection.Contains('AddRef();') -and
            $stylerWatcherUnadviseSection.Contains(
                'm_unadviseThread = CreateThread('
            ) -and
            $stylerWatcherUnadviseSection.Contains(
                'CloseVisualTreeServiceFromCurrentApartment()'
            ) -and
            $stylerWatcherUnadviseSection.Contains(
                'WorkerReferenceGuard workerReference(watcher);'
            ) -and
            $stylerWatcherUnadviseSection.Contains(
                'WaitForUnadviseThread(5000)'
            ) -and
            $stylerWatcherUnadviseSection.Contains(
                'VisualTreeWatcher Unadvise worker timed out'
            ) -and
            [regex]::IsMatch(
                $stylerWatcherThreadMethodsSection,
                '(?s)GetCurrentThreadId\(\)\s*==\s*m_unadviseThreadId.*?m_unadviseResult\.load\(.*?\)\s*==\s*E_PENDING.*?return false\s*;.*?CloseOrRetainKernelCapability\(\s*&m_unadviseThread,.*?VisualTreeUnadviseThread.*?if\s*\(\s*!m_unadviseThread\s*\)\s*\{\s*m_unadviseThreadId\s*=\s*0'
            ) -and
            $stylerUninitSection.Contains(
                'if (!UninitializeSettingsAndTap())'
            )
    }
    [pscustomobject]@{
        name = 'worker-com-git-apartment-marshalling'
        passed =
            $stylerWatcherClassSection.Contains(
                'GitLifecycle m_visualTreeServiceGit;'
            ) -and
            $stylerWatcherClassSection.Contains(
                'class VisualTreeServiceLease'
            ) -and
            -not $stylerWatcherClassSection.Contains(
                'winrt::non_agile'
            ) -and
            -not $stylerWatcherClassSection.Contains(
                'm_XamlDiagnostics'
            ) -and
            -not $stylerWatcherClassSection.Contains(
                'std::atomic<DWORD> m_visualTreeServiceGitCookie'
            ) -and
             [regex]::IsMatch(
                 $stylerWatcherClassSection,
                 '(?s)void\s+Reset\(\)\s+noexcept\s*\{.*?m_service\.detach\(\).*?proxy->Release\(\).*?catch\s*\(\s*\.\.\.\s*\).*?m_lifecycle\s*=\s*nullptr.*?return\s*;.*?ReleaseLease\(m_ticket\)'
             ) -and
            $styler.Contains('ProvisionalGitCookieGuard') -and
            $styler.Contains('ReserveProvisionalGitSlot') -and
            $styler.Contains('PublishCookie(gitCookie)') -and
            $styler.Contains('RegisterInterfaceInGlobal(') -and
            $styler.Contains('GetInterfaceFromGlobal(') -and
            $styler.Contains('RevokeInterfaceFromGlobal(') -and
            $styler.Contains('m_visualTreeServiceGit.CloseAdmission()') -and
            $styler.Contains('WaitForVisualTreeServiceLeases(5000)') -and
            $stylerWatcherClassSection.Contains(
                'class ComApartmentBalance'
            ) -and
            $stylerWatcherClassSection.Contains(
                '~ComApartmentBalance() noexcept'
            ) -and
            $stylerWatcherClassSection.Contains('CoUninitialize();') -and
            [regex]::Matches(
                $stylerWatcherConstructorSection +
                    $stylerWatcherUnadviseSection,
                'ComApartmentBalance\s+comBalance\(hr\)'
            ).Count -eq 2 -and
            [regex]::Matches(
                $stylerWatcherConstructorSection +
                    $stylerWatcherUnadviseSection,
                'CoInitializeEx\('
            ).Count -eq 2 -and
            -not ($stylerWatcherConstructorSection +
                $stylerWatcherUnadviseSection).Contains(
                    'CoUninitialize();'
                ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)RetryProvisionalGitQuarantineFromInitializedApartment\(\)\s+noexcept.*?try\s*\{.*?CoInitializeEx\(nullptr,\s*COINIT_MULTITHREADED\).*?catch\s*\(\s*\.\.\.\s*\).*?try\s*\{\s*allRevoked\s*=\s*RetryProvisionalGitQuarantine\(\).*?catch\s*\(\s*\.\.\.\s*\).*?if\s*\(\s*balanceInitialization\s*\)\s*\{\s*try\s*\{\s*CoUninitialize\(\).*?catch\s*\(\s*\.\.\.\s*\).*?balanceConfirmed\s*=\s*false.*?return\s+allRevoked\s*&&\s*balanceConfirmed'
            ) -and
            $stylerWatcherConstructorSection.Contains(
                'GetVisualTreeServiceForCurrentApartment('
            ) -and
            $stylerWatcherUnadviseSection.Contains(
                'CloseVisualTreeServiceFromCurrentApartment()'
            ) -and
            -not [regex]::IsMatch(
                $styler,
                'watcher->m_XamlDiagnostics\s*\.as<IVisualTreeService3>'
            )
    }
    [pscustomobject]@{
        name = 'visual-tree-callback-self-ref-and-close-gate'
        passed =
            [regex]::IsMatch(
                $stylerWatcherClassSection,
                '(?s)class\s+CallbackReferenceGuard.*?watcher->AddRef\(\).*?catch\s*\(\s*\.\.\.\s*\).*?FailClosedForeignAbiException\(.*?~CallbackReferenceGuard\(\)\s+noexcept.*?m_watcher->Release\(\).*?catch\s*\(\s*\.\.\.\s*\).*?FailClosedForeignAbiException\('
            ) -and
            [regex]::IsMatch(
                $stylerVisualTreeChangeSection,
                '(?s)CallbackReferenceGuard\s+keepAlive\(this\).*?if\s*\(\s*!keepAlive\s*\).*?TapLifecycleScope\s+lifecycleScope\(\s*true,\s*TapLifecycleEntryKind::kCallback.*?if\s*\(\s*!lifecycleScope\s*\)\s*\{\s*return S_OK'
            ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)OnElementStateChanged\(.*?\)\s+noexcept\s+try\s*\{.*?CallbackReferenceGuard\s+keepAlive\(this\).*?if\s*\(\s*!keepAlive\s*\).*?TapLifecycleScope\s+lifecycleScope\(\s*true,\s*TapLifecycleEntryKind::kCallback.*?if\s*\(\s*!lifecycleScope\s*\)\s*\{\s*return S_OK'
            ) -and
            [regex]::IsMatch(
                $stylerWatcherDestructorSection,
                '(?s)VisualTreeWatcher::~VisualTreeWatcher\(\)\s+noexcept\s*\{\s*.*?try\s*\{.*?\}\s*catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
            ) -and
            -not [regex]::IsMatch(
                $styler,
                'VisualTreeWatcher::~VisualTreeWatcher\(\)\s+noexcept\s+try'
            ) -and
            $phase2Harness.Contains(
                'git.internal-self-reference-noexcept'
            )
    }
    [pscustomobject]@{
        name = 'blur-brush-external-callback-fail-safe'
        passed =
            [regex]::IsMatch(
                $styler,
                '(?s)struct\s+XamlBlurBrushRegistryWaitContext\s*\{\s*winrt::weak_ref<XamlBlurBrush>\s+brush\s*;\s*HKEY\s+powerKey\{nullptr\}\s*;\s*HANDLE\s+notifyEvent\{nullptr\}\s*;\s*HANDLE\s+waitHandle\{nullptr\}\s*;'
            ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)RegisterWaitForSingleObject\(.*?OnEnergySaverRegistryChanged,\s*m_regWaitContext,'
            ) -and
            -not [regex]::IsMatch(
                $styler,
                '(?s)RegisterWaitForSingleObject\(.*?OnEnergySaverRegistryChanged,\s*this,'
            ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)OnEnergySaverRegistryChanged\(.*?\)\s+noexcept\s+try\s*\{.*?XamlBlurBrushRegistryCallbackScope\s+currentCallbackScope\(waitContext\).*?waitContext->brush\.get\(\).*?if\s*\(\s*!self\s*\).*?waitContext->powerKey.*?waitContext->notifyEvent.*?TryEnqueue\(.*?\[weakThis\]\(\)\s+noexcept.*?RunProjectedCallbackNoThrow\(.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
            ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)thread_local\s+XamlBlurBrushRegistryWaitContext\*\s*g_currentXamlBlurBrushRegistryWaitContext.*?class\s+XamlBlurBrushRegistryCallbackScope'
            ) -and
            [regex]::IsMatch(
                $stylerXamlBlurDestructorSection,
                '(?s)~XamlBlurBrush\(\)\s+noexcept\s*\{.*?UnregisterWaitEx\(m_regWaitHandle,\s*nullptr\).*?UnregisterWaitEx\(\s*m_regWaitHandle,\s*INVALID_HANDLE_VALUE\).*?RequirePermanentUnloadSafetyPin\(.*?m_regNotifyEvent\s*=\s*nullptr\s*;.*?m_powerKey\s*=\s*nullptr\s*;.*?m_regWaitContext\s*=\s*nullptr\s*;.*?catch\s*\(\s*\.\.\.\s*\)\s*\{.*?FailClosedForeignAbiException\('
            ) -and
            [regex]::Matches(
                $stylerXamlBlurDestructorSection,
                'RunProjectedCallbackNoThrow\('
            ).Count -ge 6 -and
            -not [regex]::IsMatch(
                $styler,
                'XamlBlurBrush::~XamlBlurBrush\(\)\s+noexcept\s+try'
            ) -and
            [regex]::IsMatch(
                $stylerXamlBlurDisconnectedSection,
                'TapLifecycleScope\s+callbackScope\(\s*false,\s*TapLifecycleEntryKind::kCallback'
            )
    }
    [pscustomobject]@{
        name = 'all-uninit-window-dispatches-checked'
        passed =
            $uninitWindowDispatchCount -eq 0 -and
            $checkedUninitWindowDispatchCount -eq 0 -and
            $stylerUninitSection.Contains(
                'CleanupAllRegisteredUiThreads(5000)'
            ) -and
            -not $stylerUninitSection.Contains('GetTaskbarUiWnd()') -and
            -not $stylerUninitSection.Contains('GetXamlHostWnds()')
    }
    [pscustomobject]@{
        name = 'tap-lifecycle-close-and-drain'
        passed =
            $stylerTapLifecycleSection.Contains(
                'std::condition_variable g_tapLifecycleCv;'
            ) -and
            $stylerTapLifecycleSection.Contains(
                'size_t g_tapLifecycleOperationsInFlight;'
            ) -and
            $stylerTapLifecycleSection.Contains(
                'size_t g_tapLifecycleCallbacksInFlight;'
            ) -and
            $stylerTapLifecycleSection.Contains('bool OpenTapLifecycle()') -and
            $stylerTapLifecycleSection.Contains('void CloseTapLifecycle()') -and
            $stylerTapLifecycleSection.Contains(
                'bool WaitForTapLifecycleIdle(DWORD timeoutMs)'
            ) -and
            [regex]::IsMatch(
                $stylerActivationLatchSection,
                '(?s)void\s+LatchJarvisActivationQuiesced\(PCWSTR reason\)\s+noexcept\s*\{.*?compare_exchange_weak\(.*?JarvisActivationState::kQuiesced.*?try\s*\{\s*CloseTapLifecycle\(\).*?catch\s*\(\s*\.\.\.\s*\)'
            ) -and
            $stylerInitSection.Contains('if (!OpenTapLifecycle())') -and
            [regex]::IsMatch(
                $stylerBeforeUninitSection,
                '(?s)CloseTapLifecycle\(\).*?WaitForTapLifecycleIdle\(\s*5000\s*\).*?SnapshotAllVisualTreeWatchers\(\)'
            ) -and
            [regex]::Matches(
                $stylerUninitSection,
                'WaitForTapLifecycleIdle\(\s*5000\s*\)'
            ).Count -ge 3
    }
    [pscustomobject]@{
        name = 'tap-entrypoints-and-callbacks-accounted'
        passed =
            [regex]::IsMatch(
                $stylerSetSiteSection,
                '(?s)TapLifecycleScope\s+lifecycleScope\(\s*pUnkSite\s*!=\s*nullptr.*?InstallVisualTreeWatcherIfEmpty'
            ) -and
            [regex]::IsMatch(
                $stylerInitializeSettingsSection,
                '(?s)RequirePermanentUnloadSafetyPin\(.*?InjectWindhawkTAP\(\)'
            ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)HRESULT\s+InjectWindhawkTAP\(\)\s+noexcept\s+try\s*\{\s*TapLifecycleScope\s+lifecycleScope\(\s*true'
            ) -and
            [regex]::Matches(
                $stylerVisualTreeChangeSection,
                'TapLifecycleEntryKind::kCallback'
            ).Count -ge 1 -and
            $styler.Contains(
                'std::atomic<bool> g_inInjectWindhawkTAP{false};'
            ) -and
            $stylerSetSiteSection.Contains('std::lock_guard<std::mutex> lock(siteMutex);') -and
            [regex]::IsMatch(
                $styler,
                '(?s)DllGetClassObject\(.*?TapLifecycleScope.*?RequirePermanentUnloadSafetyPin\(.*?TAP COM class factory'
            )
    }
    [pscustomobject]@{
        name = 'windhawk-detour-callbacks-accounted-and-pinned'
        passed =
            [regex]::IsMatch(
                $stylerOnWindowCreatedSection,
                '(?s)OnWindowCreated\(.*?\)\s+noexcept\s+try\s*\{.*?\}\s*catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
            ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)CreateWindowExW_Hook\(.*?TapLifecycleScope\s+lifecycleScope\(\s*false,\s*TapLifecycleEntryKind::kCallback'
            ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)CreateWindowInBand_Hook\(.*?TapLifecycleScope\s+lifecycleScope\(\s*false,\s*TapLifecycleEntryKind::kCallback'
            ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)CreateWindowInBandEx_Hook\(.*?TapLifecycleScope\s+lifecycleScope\(\s*false,\s*TapLifecycleEntryKind::kCallback'
            ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)InitializeXamlDiagnosticsEx_Hook\(.*?TapLifecycleScope\s+lifecycleScope\(\s*false,\s*TapLifecycleEntryKind::kCallback'
            ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)LoadLibraryExW_Hook\(.*?TapLifecycleScope\s+lifecycleScope\(\s*false,\s*TapLifecycleEntryKind::kCallback'
            ) -and
            $stylerInitSection.Contains(
                'L"Windhawk detour callbacks were published"'
            )
    }
    [pscustomobject]@{
        name = 'window-hook-retention-and-retry'
        passed =
            $stylerRunFromWindowThreadSection.Contains(
                'HHOOK g_trackedWindowThreadHook = nullptr;'
            ) -and
            $stylerRunFromWindowThreadSection.Contains(
                'RunFromWindowThreadContext* g_pendingWindowThreadContext = nullptr;'
            ) -and
            [regex]::IsMatch(
                $stylerRunFromWindowThreadSection,
                '(?s)SetWindowsHookEx\(.*?RequirePermanentUnloadSafetyPin\(.*?RegisterWindowThreadDispatchResources\(hook,\s*context\)'
            ) -and
            [regex]::IsMatch(
                $stylerRunFromWindowThreadSection,
                '(?s)bool\s+RemoveTrackedWindowThreadHook\(.*?HHOOK hook,.*?RunFromWindowThreadContext\* context.*?UnhookWindowsHookEx\(hook\).*?ForgetWindowThreadHook\(hook,\s*context\).*?CompleteHookRemoval'
            ) -and
            $stylerBeforeUninitSection.Contains(
                'RetryTrackedWindowThreadHooks()'
            ) -and
            $stylerUninitSection.Contains(
                'RetryTrackedWindowThreadHooks()'
            ) -and
            $stylerUninitSection.Contains(
                'HasTrackedWindowThreadHooks()'
            ) -and
            $stylerUninitSection.Contains(
                'LogWindowThreadDispatchReceipts()'
            )
    }
    [pscustomobject]@{
        name = 'setsite-transition-and-retired-watcher-barrier'
        passed =
            $stylerWatcherGlobalsSection.Contains(
                'std::atomic<bool> g_tapTransitionInProgress{false};'
            ) -and
            $stylerWatcherGlobalsSection.Contains(
                'class TapTransitionScope'
            ) -and
            $stylerWatcherGlobalsSection.Contains(
                'HasRetiredVisualTreeWatcherOwnersLocked()'
            ) -and
            $stylerSetSiteSection.Contains(
                'TapTransitionScope transitionScope;'
            ) -and
            $stylerSetSiteSection.Contains(
                'Rejecting re-entrant or concurrent TAP SetSite transition'
            ) -and
            $stylerSetSiteSection.Contains(
                'std::shared_ptr<SiteHolder> oldSite;'
            ) -and
            [regex]::IsMatch(
                $stylerSetSiteSection,
                '(?s)lock\(siteMutex\).*?oldSite\s*=\s*std::move\(site\).*?site\s*=\s*std::move\(newSite\).*?\}\s*// Releasing a COM site.*?oldSite\.reset\(\)'
            ) -and
            $stylerGetSiteSection.Contains(
                'return currentSite->QueryInterfaceNoThrow(riid, ppvSite);'
            ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)bool\s+UninitializeSettingsAndTap\(\)\s*\{\s*TapTransitionScope\s+transitionScope'
            ) -and
            -not $stylerSetSiteSection.Contains(
                'FreeLibrary(GetCurrentModuleHandle())'
            )
    }
    [pscustomobject]@{
        name = 'style-variable-callback-address-stability'
        passed =
            $stylerStyleVariableStateSection.Contains(
                'thread_local std::list<StyleVariableState> g_styleVariableState;'
            ) -and
            -not $stylerStyleVariableStateSection.Contains(
                'g_styleVariableState.remove_if'
            ) -and
            -not $stylerUninitializeForThreadSection.Contains(
                'g_styleVariableState.clear()'
            ) -and
            -not [regex]::IsMatch(
                $stylerUninitializeForThreadSection,
                'state\.(?:variables|consumers)\.clear\(\)'
            ) -and
            $styler.Contains(
                'bounded thread-local nodes remain until their UI thread exits'
            )
    }
    [pscustomobject]@{
        name = 'element-callback-shared-lifetime-and-drain'
        passed =
            [regex]::IsMatch(
                $styler,
                '(?s)thread_local\s+std::unordered_map<\s*InstanceHandle,\s*std::shared_ptr<ElementCustomizationState>>\s+g_elementsCustomizationState'
            ) -and
            [regex]::IsMatch(
                $styler,
                '(?s)std::list<std::pair<.*?std::shared_ptr<\s*ElementCustomizationStateForVisualStateGroup>>>'
            ) -and
            [regex]::IsMatch(
                $stylerVisualStateCallbacksSection,
                '(?s)\[elementCustomizationStateForVisualStateGroup,\s*property\].*?RunProjectedCallbackNoThrow\(.*?TapLifecycleScope\s+callbackScope\(\s*true,\s*TapLifecycleEntryKind::kCallback'
            ) -and
            [regex]::IsMatch(
                $stylerVisualStateCallbacksSection,
                '(?s)\[state,\s*elementWeakRef,\s*propertyOverrides,\s*handle,.*?elementCustomizationStateForVisualStateGroup\].*?RunProjectedCallbackNoThrow\(.*?TapLifecycleScope\s+callbackScope\(\s*true,\s*TapLifecycleEntryKind::kCallback'
            ) -and
            [regex]::Matches(
                $styler,
                'TapLifecycleScope\s+callbackScope\(\s*true,\s*TapLifecycleEntryKind::kCallback'
            ).Count -ge 18 -and
            [regex]::Matches(
                $stylerVisualStateCallbacksSection,
                'RunProjectedCallbackNoThrow\('
            ).Count -ge 2 -and
            $stylerUninitSection.Contains(
                'bool callbacksDrainedAfterUnadvise = WaitForTapLifecycleIdle(5000);'
            ) -and
            [regex]::IsMatch(
                $stylerUninitSection,
                '(?s)if\s*\(\s*callbacksDrainedAfterUnadvise\s*\).*?CleanupAllRegisteredUiThreads\(\s*5000\s*\).*?else\s*\{.*?Skipping destructive UI cleanup'
            ) -and
            $stylerUninitSection.Contains(
                'callbacksDrainedAfterUnadvise && cleanupWorkDrained'
            )
    }
    [pscustomobject]@{
        name = 'settings-hot-reload-fails-closed'
        passed =
            $stylerSettingsChangedSection.Contains(
                'LatchJarvisActivationQuiesced('
            ) -and
            $stylerSettingsChangedSection.Contains(
                'settings changes require a full authorized reload'
            ) -and
            -not $stylerSettingsChangedSection.Contains('LoadSettings()') -and
            -not $stylerSettingsChangedSection.Contains(
                'InitializeSettingsAndTap()'
            ) -and
            -not $stylerSettingsChangedSection.Contains(
                'RunFromWindowThread('
            )
    }
    [pscustomobject]@{
        name = 'permanent-pin-is-serialized-and-irrevocable'
        passed =
            $stylerUnloadSafetyPinSection.Contains(
                'std::atomic_flag g_unloadSafetyPinDecisionGate'
            ) -and
            -not $stylerUnloadSafetyPinSection.Contains(
                'g_unloadSafetyPinMutex'
            ) -and
            $stylerUnloadSafetyPinSection.Contains(
                'class UnloadSafetyPinDecisionGuard'
            ) -and
            $stylerUnloadSafetyPinSection.Contains(
                'g_unloadSafetyPinEpoch'
            ) -and
            $stylerUnloadSafetyPinSection.Contains(
                'g_unloadSafetyPinAcquireTicket'
            ) -and
            $stylerUnloadSafetyPinSection.Contains(
                'g_unloadSafetyPinReleaseTicket'
            ) -and
            $stylerUnloadSafetyPinSection.Contains(
                'g_unloadSafetyUnconfirmedReleaseOwnerEpoch'
            ) -and
            [regex]::Matches(
                $stylerUnloadSafetyPinSection,
                'UnloadSafetyPinDecisionGuard\s+decisionGuard'
            ).Count -ge 3 -and
            [regex]::IsMatch(
                $stylerUnloadSafetyPinSection,
                '(?s)g_unloadSafetyModulePin\.exchange\(.*?g_unloadSafetyPinReleaseTicket\s*=\s*releaseTicket;.*?g_unloadSafetyPinReleaseOwner\s*=\s*module;.*?\}\s*.*?FreeLibrary\(module\).*?g_unloadSafetyPinReleaseTicket\s*==\s*releaseTicket.*?g_unloadSafetyPinReleaseOwner\s*==\s*module'
            ) -and
            -not [regex]::IsMatch(
                $styler,
                'g_permanentUnloadSafetyPinRequired\s*\.\s*store\s*\(\s*false'
            )
    }
    [pscustomobject]@{
        name = 'com-lockserver-balanced-and-underflow-safe'
        passed =
            $stylerFactorySection.Contains(
                'g_simpleFactoryServerLockBalance'
            ) -and
            [regex]::IsMatch(
                $stylerFactorySection,
                '(?s)LockServer\(BOOL lock\)\s+noexcept\s+override\s+try.*?if\s*\(\s*lock\s*\).*?\+\+winrt::get_module_lock\(\).*?compare_exchange_(?:weak|strong)\(.*?balance\s*\+\s*1.*?while\s*\(\s*balance\s*!=\s*0\s*\).*?compare_exchange_(?:weak|strong)\(.*?balance\s*-\s*1.*?--winrt::get_module_lock\(\).*?unbalanced unlock.*?ERROR_INVALID_STATE'
            ) -and
            -not [regex]::IsMatch(
                $stylerFactorySection,
                '(?s)if\s*\(\s*lock\s*\).*?\+\+winrt::get_module_lock\(\).*?\belse\b.*?--winrt::get_module_lock\(\)'
            )
    }
    [pscustomobject]@{
        name = 'module-pin-before-kill-watcher'
        passed =
            $stylerUnloadSafetyPinSection.Contains(
                'std::atomic<HMODULE> g_unloadSafetyModulePin'
            ) -and
            $stylerUnloadSafetyPinSection.Contains('GetModuleHandleExW(') -and
            -not $stylerUnloadSafetyPinSection.Contains(
                'GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT'
            ) -and
            $pinAcquireIndex -ge 0 -and
            $watcherStartIndex -gt $pinAcquireIndex
    }
    [pscustomobject]@{
        name = 'post-watcher-failures-retain-pin-until-stop'
        passed =
            $watcherStartFailureMatch.Success -and
            $postWatcherPinReleaseCount -gt 0 -and
            $postWatcherGuardedPinReleaseCount -eq
                $postWatcherPinReleaseCount -and
            -not $stylerBeforeUninitSection.Contains(
                'ReleaseUnloadSafetyModulePin()'
            )
    }
    [pscustomobject]@{
        name = 'final-pin-release-requires-unload-safe'
        passed =
            $stylerUninitSection.Contains(
                'bool unloadSafe = !IsPermanentUnloadSafetyPinRequired();'
            ) -and
            [regex]::IsMatch(
                $stylerUninitSection,
                '(?s)if\s*\(\s*winrt::get_module_lock\(\)\s*\).*?unloadSafe\s*=\s*false\s*;.*?if\s*\(\s*unloadSafe\s*\)\s*\{\s*if\s*\(\s*!ReleaseUnloadSafetyModulePin\(\)'
            ) -and
            $stylerUninitSection.Contains(
                'retained its unload-safety module pin'
            )
    }
    [pscustomobject]@{
        name = 'kill-watcher-lock-and-no-pin-release'
        passed =
            $styler.Contains('std::mutex g_killSwitchWatcherMutex;') -and
            $stylerStartKillSwitchWatcherSection.Contains(
                'lock(g_killSwitchWatcherMutex)'
            ) -and
            $stylerStopKillSwitchWatcherSection.Contains(
                'lock(g_killSwitchWatcherMutex)'
            ) -and
            -not $stylerStopKillSwitchWatcherSection.Contains(
                'ReleaseUnloadSafetyModulePin'
            )
    }
)
$stylerThreadedUnloadFailures = @(
    $stylerThreadedUnloadClauses |
        Where-Object { -not $_.passed } |
        ForEach-Object { $_.name }
)
$stylerThreadedUnloadDetail =
    'M1 threaded teardown must close and drain TAP work, track temporary hooks, and retain a real module pin permanently after external callback publication or whenever cleanup is uncertain; this static gate still does not prove UI restoration or a live unload.'
if ($stylerThreadedUnloadFailures.Count -gt 0) {
    $stylerThreadedUnloadDetail +=
        ' Missing clauses: ' + ($stylerThreadedUnloadFailures -join ', ') + '.'
}
Add-Check `
    'styler.threaded-unload-fail-safe' `
    ($stylerThreadedUnloadFailures.Count -eq 0) `
    $stylerThreadedUnloadDetail

$phase2FactoryLockServerBalanced = @(
    $stylerThreadedUnloadClauses |
        Where-Object {
            $_.name -eq 'com-lockserver-balanced-and-underflow-safe'
        }
)[0].passed
Add-Check `
    'styler.factory-lockserver-balanced' `
    $phase2FactoryLockServerBalanced `
    'IClassFactory LockServer must publish a separately balanced server lock, reject unmatched FALSE without decrementing the C++/WinRT module lock, and never underflow.'

$tapLifecyclePrimitiveContained =
    [regex]::IsMatch(
        $stylerTapLifecycleSection,
        '(?s)TapLifecycleScope\(bool requiresAcceptance,\s*TapLifecycleEntryKind kind\)\s+noexcept.*?try\s*\{.*?std::lock_guard<std::mutex>\s+lock\(g_tapLifecycleMutex\).*?catch\s*\(\s*\.\.\.\s*\)\s*\{.*?FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerTapLifecycleSection,
        '(?s)~TapLifecycleScope\(\)\s+noexcept\s*\{.*?try\s*\{.*?std::lock_guard<std::mutex>\s+lock\(g_tapLifecycleMutex\).*?catch\s*\(\s*\.\.\.\s*\)\s*\{.*?FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)void\s+FailClosedForeignAbiException\(PCWSTR reason\)\s+noexcept\s*\{.*?LatchJarvisActivationQuiesced\(reason\)\s*;.*?RequirePermanentUnloadSafetyPin\(reason\)\s*;.*?try\s*\{.*?Wh_Log\(.*?catch\s*\(\s*\.\.\.\s*\)'
    ) -and
    [regex]::IsMatch(
        $stylerActivationLatchSection,
        '(?s)void\s+LatchJarvisActivationQuiesced\(PCWSTR reason\)\s+noexcept\s*\{.*?compare_exchange_weak\(.*?JarvisActivationState::kQuiesced.*?try\s*\{\s*CloseTapLifecycle\(\).*?catch\s*\(\s*\.\.\.\s*\).*?try\s*\{\s*Wh_Log\('
    ) -and
    [regex]::IsMatch(
        $stylerUnloadSafetyPinSection,
        '(?s)void\s+RequirePermanentUnloadSafetyPin\(PCWSTR reason\)\s+noexcept\s*\{.*?g_permanentUnloadSafetyPinRequired\.exchange\(\s*true,\s*std::memory_order_acq_rel\).*?ClaimUnloadSafetyPinTicketUnderDecisionGate\(\).*?\}\s*HMODULE\s+independentlyAcquired.*?GetModuleHandleExW\(.*?g_unloadSafetyPinAcquireTicket\s*==\s*acquireTicket.*?UnloadSafetyPinAcquirePurpose::Permanent.*?PublishPermanentUnloadSafetyPinStateUnderDecisionGate\(.*?if\s*\(\s*alreadyRequired\s*&&\s*pinProven\s*\)\s*\{\s*return\s*;'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)HRESULT\s+WindhawkTAP::GetSite\(.*?\)\s+noexcept.*?TapLifecycleScope\s+lifecycleScope\(\s*false,\s*TapLifecycleEntryKind::kOperation\).*?if\s*\(\s*!lifecycleScope\s*\)\s*\{\s*return HRESULT_FROM_WIN32\(ERROR_CANCELLED\)'
    )
Add-Check `
    'styler.tap-lifecycle-primitive-exception-contained' `
    $tapLifecyclePrimitiveContained `
    'TapLifecycleScope admission and destruction must contain lock/counter failures, reject untracked COM access, and independently pin and quiesce instead of terminating across a foreign ABI.'

$foreignAbiHookSections = @(
    [pscustomobject]@{
        name = 'CreateWindowExW_Hook'
        original = 'CreateWindowExW_Original'
        expectedOriginalCalls = 2
        expectedCompletedCalls = 1
        returnKind = 'window'
        source = Get-SourceSlice `
            -Text $styler `
            -StartMarker 'HWND WINAPI CreateWindowExW_Hook(' `
            -EndMarker 'using CreateWindowInBand_t'
    }
    [pscustomobject]@{
        name = 'CreateWindowInBand_Hook'
        original = 'CreateWindowInBand_Original'
        expectedOriginalCalls = 2
        expectedCompletedCalls = 1
        returnKind = 'window'
        source = Get-SourceSlice `
            -Text $styler `
            -StartMarker 'HWND WINAPI CreateWindowInBand_Hook(' `
            -EndMarker 'using CreateWindowInBandEx_t'
    }
    [pscustomobject]@{
        name = 'CreateWindowInBandEx_Hook'
        original = 'CreateWindowInBandEx_Original'
        expectedOriginalCalls = 2
        expectedCompletedCalls = 1
        returnKind = 'window'
        source = Get-SourceSlice `
            -Text $styler `
            -StartMarker 'HWND WINAPI CreateWindowInBandEx_Hook(' `
            -EndMarker 'using DestroyWindow_t'
    }
    [pscustomobject]@{
        name = 'DestroyWindow_Hook'
        original = 'DestroyWindow_Original'
        expectedOriginalCalls = 3
        expectedCompletedCalls = 3
        returnKind = 'destroy'
        source = $stylerDestroyWindowSection
    }
    [pscustomobject]@{
        name = 'InitializeXamlDiagnosticsEx_Hook'
        original = 'InitializeXamlDiagnosticsEx_Original'
        expectedOriginalCalls = 3
        expectedCompletedCalls = 2
        returnKind = 'hresult'
        source = Get-SourceSlice `
            -Text $styler `
            -StartMarker 'InitializeXamlDiagnosticsEx_Hook(' `
            -EndMarker 'bool HookInitializeXamlDiagnosticsExIfNeeded('
    }
    [pscustomobject]@{
        name = 'LoadLibraryExW_Hook'
        original = 'LoadLibraryExW_Original'
        expectedOriginalCalls = 2
        expectedCompletedCalls = 1
        returnKind = 'module'
        source = Get-SourceSlice `
            -Text $styler `
            -StartMarker 'HMODULE WINAPI LoadLibraryExW_Hook(' `
            -EndMarker 'constexpr std::size_t kMaxXamlHostWindowSnapshot'
    }
)
$foreignAbiHookFirewallFailures = @(
    $foreignAbiHookSections |
        Where-Object {
            $escapedOriginal = [regex]::Escape($_.original)
            $commonContract =
                $_.source.Contains('bool originalAttempted = false;') -and
                $_.source.Contains('bool originalCompleted = false;') -and
                [regex]::Matches(
                    $_.source,
                    ($escapedOriginal + '\s*\(')
                ).Count -eq $_.expectedOriginalCalls -and
                [regex]::Matches(
                    $_.source,
                    ('(?s)originalAttempted\s*=\s*true\s*;.*?' +
                     $escapedOriginal +
                     '\s*\(.*?originalCompleted\s*=\s*true\s*;')
                ).Count -eq $_.expectedCompletedCalls -and
                [regex]::IsMatch(
                    $_.source,
                    '(?s)try\s*\{.*?TapLifecycleScope\s+lifecycleScope\(.*?originalAttempted\s*=\s*true\s*;.*?_Original\(.*?originalCompleted\s*=\s*true\s*;'
                ) -and
                [regex]::IsMatch(
                    $_.source,
                    '(?s)catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\(.*?if\s*\(\s*originalCompleted\s*\).*?return.*?if\s*\(\s*originalAttempted\s*\).*?return.*?try\s*\{\s*originalAttempted\s*=\s*true\s*;.*?_Original\(.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
                )

            $returnContract = $false
            if ($_.returnKind -eq 'window') {
                $returnContract =
                    $_.source.Contains('if (lifecycleScope && hWnd &&') -and
                    [regex]::IsMatch(
                        $_.source,
                        '(?s)originalCompleted\s*=\s*true\s*;\s*createError\s*=\s*GetLastError\(\).*?\}\s*SetLastError\(createError\)\s*;\s*return hWnd\s*;\s*\}\s*catch'
                    ) -and
                    [regex]::IsMatch(
                        $_.source,
                        '(?s)if\s*\(\s*originalCompleted\s*\)\s*\{\s*SetLastError\(createError\)\s*;\s*return hWnd\s*;\s*\}\s*if\s*\(\s*originalAttempted\s*\)\s*\{\s*SetLastError\(ERROR_UNHANDLED_EXCEPTION\)\s*;\s*return nullptr\s*;'
                    ) -and
                    [regex]::IsMatch(
                        $_.source,
                        ('(?s)try\s*\{\s*originalAttempted\s*=\s*true\s*;\s*hWnd\s*=\s*' +
                         $escapedOriginal +
                         '\s*\(.*?createError\s*=\s*GetLastError\(\)\s*;\s*SetLastError\(createError\)\s*;\s*return hWnd\s*;.*?catch\s*\(\s*\.\.\.\s*\).*?SetLastError\(ERROR_UNHANDLED_EXCEPTION\)\s*;\s*return nullptr\s*;')
                    )
            }
            elseif ($_.returnKind -eq 'module') {
                $returnContract =
                    $_.source.Contains(
                        'if (lifecycleScope && IsJarvisActivationActive() && module &&'
                    ) -and
                    [regex]::IsMatch(
                        $_.source,
                        '(?s)originalCompleted\s*=\s*true\s*;\s*loadError\s*=\s*GetLastError\(\).*?\}\s*SetLastError\(loadError\)\s*;\s*return module\s*;\s*\}\s*catch'
                    ) -and
                    [regex]::IsMatch(
                        $_.source,
                        '(?s)if\s*\(\s*originalCompleted\s*\)\s*\{\s*SetLastError\(loadError\)\s*;\s*return module\s*;\s*\}\s*if\s*\(\s*originalAttempted\s*\)\s*\{\s*SetLastError\(ERROR_UNHANDLED_EXCEPTION\)\s*;\s*return nullptr\s*;'
                    ) -and
                    [regex]::IsMatch(
                        $_.source,
                        ('(?s)try\s*\{\s*originalAttempted\s*=\s*true\s*;\s*module\s*=\s*' +
                         $escapedOriginal +
                         '\s*\(.*?loadError\s*=\s*GetLastError\(\)\s*;\s*SetLastError\(loadError\)\s*;\s*return module\s*;.*?catch\s*\(\s*\.\.\.\s*\).*?SetLastError\(ERROR_UNHANDLED_EXCEPTION\)\s*;\s*return nullptr\s*;')
                    )
            }
            elseif ($_.returnKind -eq 'destroy') {
                $returnContract =
                    [regex]::IsMatch(
                        $_.source,
                        '(?s)if\s*\(\s*!lifecycleScope\s*\)\s*\{.*?originalAttempted\s*=\s*true\s*;.*?DestroyWindow_Original\(hWnd\).*?originalCompleted\s*=\s*true\s*;.*?destroyError\s*=\s*GetLastError\(\).*?SetLastError\(destroyError\).*?return destroyed'
                    ) -and
                    [regex]::IsMatch(
                        $_.source,
                        '(?s)originalAttempted\s*=\s*true\s*;\s*destroyed\s*=\s*DestroyWindow_Original\(hWnd\)\s*;\s*originalCompleted\s*=\s*true\s*;\s*destroyError\s*=\s*GetLastError\(\).*?CompleteUiThreadWindowDestroy\(.*?SetLastError\(destroyError\).*?return destroyed'
                    ) -and
                    [regex]::IsMatch(
                        $_.source,
                        '(?s)catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\(.*?if\s*\(\s*originalCompleted\s*\).*?SetLastError\(destroyError\).*?if\s*\(\s*originalAttempted\s*\).*?SetLastError\(ERROR_UNHANDLED_EXCEPTION\).*?try\s*\{.*?DestroyWindow_Original\(hWnd\).*?originalCompleted\s*=\s*true.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
                    ) -and
                    -not [regex]::IsMatch(
                        $_.source,
                        'return\s+DestroyWindow_Original\s*\('
                    )
            }
            else {
                $returnContract =
                    $_.source.Contains(
                        'if (!lifecycleScope || !IsJarvisActivationActive() ||'
                    ) -and
                    [regex]::IsMatch(
                        $_.source,
                        '(?s)if\s*\(\s*originalCompleted\s*\)\s*\{\s*return result\s*;\s*\}\s*if\s*\(\s*originalAttempted\s*\)\s*\{\s*return E_UNEXPECTED\s*;'
                    ) -and
                    [regex]::IsMatch(
                        $_.source,
                        ('(?s)try\s*\{\s*originalAttempted\s*=\s*true\s*;\s*result\s*=\s*' +
                         $escapedOriginal +
                         '\s*\(.*?return result\s*;.*?catch\s*\(\s*\.\.\.\s*\).*?return E_UNEXPECTED\s*;')
                    )
            }

            -not ($commonContract -and $returnContract)
        } |
        ForEach-Object { $_.name }
)
Add-Check `
    'styler.foreign-abi-exception-firewalls' `
    ($foreignAbiHookFirewallFailures.Count -eq 0) `
    ('Every CreateWindow/DestroyWindow/XAML diagnostics/LoadLibrary detour must contain constructor, original, post-processing, and fallback exceptions while invoking the original at most once. Missing: ' +
     ($foreignAbiHookFirewallFailures -join ', '))

$phase2AbiFailClosedEntrypoints =
    [regex]::IsMatch(
        $stylerOnWindowCreatedSection,
        '(?s)catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    [regex]::Matches(
        $stylerEnergySaverCallbackSection,
        'FailClosedForeignAbiException\('
    ).Count -ge 2 -and
    [regex]::IsMatch(
        $styler,
        '(?s)void\s+ReportUiWindowRoleFailure\(PCWSTR reason\)\s+noexcept\s*\{\s*FailClosedForeignAbiException\(reason\)\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerVisualTreeChangeSection,
        '(?s)catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)TapLifecycleScope\s+callbackScope\(\s*false,\s*TapLifecycleEntryKind::kCallback\s*\)\s*;\s*if\s*\(\s*!callbackScope\s*\)\s*\{.*?return\s+CallNextHookEx\(.*?\).*?\}\s*if\s*\(\s*nCode\s*==\s*HC_ACTION\s*\)'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)window-thread hook callback boundary threw'
    )
Add-Check `
    'styler.abi-entrypoints-unified-failclosed' `
    $phase2AbiFailClosedEntrypoints `
    'Every named Win32/COM/window-hook callback must fail closed through the single noexcept boundary helper, and an untracked WH_CALLWNDPROC invocation must pass through before dereferencing raw callback data.'

$phase2WorkerBoundaryRaiiContract =
    $stylerWatcherClassSection.Contains(
        'class WorkerReferenceGuard'
    ) -and
    $stylerWatcherClassSection.Contains(
        '~WorkerReferenceGuard() noexcept'
    ) -and
    $stylerWatcherClassSection.Contains(
        'class ComApartmentBalance'
    ) -and
    $stylerWatcherClassSection.Contains(
        '~ComApartmentBalance() noexcept'
    ) -and
    [regex]::Matches(
        $stylerWatcherConstructorSection +
            $stylerWatcherUnadviseSection,
        '\[\]\(LPVOID lpParam\)\s+noexcept\s*->\s*DWORD'
    ).Count -eq 2 -and
    [regex]::Matches(
        $stylerWatcherConstructorSection +
            $stylerWatcherUnadviseSection,
        'WorkerReferenceGuard\s+workerReference\(watcher\)'
    ).Count -eq 2 -and
    [regex]::Matches(
        $stylerWatcherConstructorSection +
            $stylerWatcherUnadviseSection,
        'ComApartmentBalance\s+comBalance\(hr\)'
    ).Count -eq 2 -and
    [regex]::Matches(
        $stylerWatcherConstructorSection +
            $stylerWatcherUnadviseSection,
        'if\s*\(\s*!lifecycleScope\s*\)'
    ).Count -ge 2 -and
    [regex]::Matches(
        $stylerWatcherConstructorSection +
            $stylerWatcherUnadviseSection,
        'RetainVisualTreeServiceGit\('
    ).Count -ge 4 -and
    -not ($stylerWatcherConstructorSection +
        $stylerWatcherUnadviseSection).Contains(
            'watcher->Release();'
        ) -and
    $phase2Harness.Contains(
        'git.worker-boundary-raii-containment'
    )
Add-Check `
    'styler.visual-tree-workers-raii-firewall' `
    $phase2WorkerBoundaryRaiiContract `
    'Both CreateThread workers must contain their full ABI boundary, own one pre-published reference through RAII, balance successful COM initialization, and retain on lifecycle-admission failure.'

$phase2UiCleanupCallbackBoundary =
    [regex]::IsMatch(
        $stylerUiLifecycleSection,
        '(?s)void\s+RetainUiThreadCleanupCallback\(.*?\)\s+noexcept.*?CompleteCleanup\(.*?UiCleanupOutcome::\s*Retained.*?FailClosedForeignAbiException\(failClosedReason\)'
    ) -and
    [regex]::IsMatch(
        $stylerUiLifecycleSection,
        '(?s)RunUiThreadCleanupDispatcherCallback\(.*?\)\s+noexcept\s+try.*?TapLifecycleScope\s+lifecycleScope\(\s*false.*?if\s*\(\s*!lifecycleScope\s*\).*?dispatcher-callback-lifecycle-admission-failed.*?RetainUiThreadCleanupCallback\('
    ) -and
    [regex]::IsMatch(
        $stylerUiLifecycleSection,
        '(?s)RunUiThreadShutdownCleanupCallback\(.*?\)\s+noexcept.*?TapLifecycleScope\s+lifecycleScope\(\s*false.*?BeginCleanup\(.*?if\s*\(\s*!lifecycleScope\s*\).*?shutdown-callback-lifecycle-admission-failed.*?RetainUiThreadCleanupCallback\('
    ) -and
    [regex]::IsMatch(
        $stylerInitializeForThreadSection,
        '(?s)ShutdownStarting\(.*?RunUiThreadShutdownCleanupCallback\(.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    $phase2Harness.Contains(
        'ui.cleanup-callback-admission-failure'
    )
Add-Check `
    'phase2.ui-thread.callback-admission-retained' `
    $phase2UiCleanupCallbackBoundary `
    'Dispatcher and ShutdownStarting cleanup callbacks must never mutate UI after lifecycle admission fails; they must publish a structured retained receipt, signal completion, pin, and quiesce.'

$beforeUninitUnloadingIndex = $stylerBeforeUninitSection.IndexOf(
    'g_jarvisActivationState.store(JarvisActivationState::kUnloading',
    [StringComparison]::Ordinal
)
$beforeUninitFirstFallibleIndex = $stylerBeforeUninitSection.IndexOf(
    'Wh_Log(L">")',
    [StringComparison]::Ordinal
)
$uninitUnloadingIndex = $stylerUninitSection.IndexOf(
    'g_jarvisActivationState.store(JarvisActivationState::kUnloading',
    [StringComparison]::Ordinal
)
$uninitFirstFallibleIndex = $stylerUninitSection.IndexOf(
    'Wh_Log(L">")',
    [StringComparison]::Ordinal
)
$phase2ModuleExportFirewall =
    [regex]::IsMatch(
        $stylerInjectTapSection,
        '(?s)class\s+InjectWindhawkTapFlagGuard.*?m_previous\s*\(\s*g_inInjectWindhawkTAP\.exchange\(.*?~InjectWindhawkTapFlagGuard\(\)\s+noexcept.*?g_inInjectWindhawkTAP\.store\(\s*m_previous'
    ) -and
    [regex]::IsMatch(
        $stylerInjectTapSection,
        '(?s)HRESULT\s+InjectWindhawkTAP\(\)\s+noexcept\s+try.*?InjectWindhawkTapFlagGuard\s+injectFlagGuard.*?catch\s*\(\s*\.\.\.\s*\).*?FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerAfterInitSection,
        '(?s)void\s+Wh_ModAfterInit\(\)\s+noexcept\s+try.*?JarvisStateGateGuard\s+stateGateGuard\(.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    $beforeUninitUnloadingIndex -ge 0 -and
    $beforeUninitFirstFallibleIndex -gt
        $beforeUninitUnloadingIndex -and
    $uninitUnloadingIndex -ge 0 -and
    $uninitFirstFallibleIndex -gt $uninitUnloadingIndex -and
    [regex]::IsMatch(
        $stylerBeforeUninitSection,
        '(?s)void\s+Wh_ModBeforeUninit\(\)\s+noexcept.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerUninitSection,
        '(?s)void\s+Wh_ModUninit\(\)\s+noexcept.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    [regex]::Matches(
        $stylerComExportSection,
        'FailClosedForeignAbiException\('
    ).Count -ge 2 -and
    $phase2Harness.Contains('module.export-abi-firewall')
Add-Check `
    'styler.module-export-and-inject-firewalls' `
    $phase2ModuleExportFirewall `
    'AfterInit, BeforeUninit, Uninit, COM exports, and TAP injection must contain their full ABI boundaries; state-gate and injection flags must restore by RAII, and Unloading must publish before fallible work.'

$initGenerationClaimIndex = $stylerInitSection.IndexOf(
    'g_moduleInitializationAttempted.compare_exchange_strong(',
    [StringComparison]::Ordinal
)
$initFirstLogIndex = $stylerInitSection.IndexOf(
    'Wh_Log(',
    [StringComparison]::Ordinal
)
$initStaticBlockedBaseline = [regex]::IsMatch(
    $styler,
    '(?s)std::atomic<JarvisActivationState>\s+g_jarvisActivationState\s*\{\s*JarvisActivationState::kBlocked\s*\}\s*;'
)
$initBlockedToAuthorizedCas = [regex]::IsMatch(
    $stylerInitSection,
    '(?s)JarvisActivationState\s+expectedActivationState\s*=\s*JarvisActivationState::kBlocked\s*;\s*if\s*\(\s*!g_jarvisActivationState\.compare_exchange_strong\(\s*expectedActivationState,\s*JarvisActivationState::kAuthorized,\s*std::memory_order_acq_rel,\s*std::memory_order_acquire\s*\)'
)
$initDoesNotRewriteBlockedBaseline = -not [regex]::IsMatch(
    $stylerInitSection,
    'g_jarvisActivationState\s*\.\s*(?:store|exchange)\s*\(\s*JarvisActivationState::kBlocked\b'
)
$settingsLatchIndex = $stylerSettingsChangedSection.IndexOf(
    'LatchJarvisActivationQuiesced(',
    [StringComparison]::Ordinal
)
$settingsFirstLogIndex = $stylerSettingsChangedSection.IndexOf(
    'Wh_Log(',
    [StringComparison]::Ordinal
)
$phase2InitSettingsFirewalls =
    [regex]::IsMatch(
        $stylerInitSection,
        '(?s)BOOL\s+Wh_ModInit\(\)\s+noexcept\s+try\s*\{.*?JarvisStateGateGuard\s+stateGateGuard\(\s*AcquireJarvisStateGate\(\).*?UiUniqueHandle\s+activationPermit\s*=\s*OpenValidatedActivationPermit\(\).*?ConsumeActivationPermit\(activationPermit\).*?\}\s*catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    $initStaticBlockedBaseline -and
    $initBlockedToAuthorizedCas -and
    $initDoesNotRewriteBlockedBaseline -and
    $initGenerationClaimIndex -ge 0 -and
    $initFirstLogIndex -gt $initGenerationClaimIndex -and
    -not [regex]::IsMatch(
        $stylerInitSection,
        'CloseHandle\s*\(\s*activationPermit|ReleaseJarvisStateGate\s*\(\s*stateGate'
    ) -and
    [regex]::IsMatch(
        $stylerSettingsChangedSection,
        '(?s)void\s+Wh_ModSettingsChanged\(\)\s+noexcept\s*\{.*?LatchJarvisActivationQuiesced\(.*?try\s*\{.*?Wh_Log\(.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    $settingsLatchIndex -ge 0 -and
    $settingsFirstLogIndex -gt $settingsLatchIndex -and
    $phase2Harness.Contains('module.export-abi-firewall')
Add-Check `
    'styler.module-init-settings-firewalls' `
    $phase2InitSettingsFirewalls `
    'Init must inherit the static Blocked baseline without rewriting it, claim the single mapped generation before fallible work, authorize only through a Blocked-to-Authorized CAS, and own its gate and permit by RAII; SettingsChanged must publish Quiesced before diagnostics, and neither export may leak an exception.'

$phase2TapComTotalFirewall =
    [regex]::IsMatch(
        $stylerSetSiteSection,
        '(?s)SetSite\(.*?\)\s+noexcept\s+try.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerGetSiteSection,
        '(?s)GetSite\(.*?\)\s+noexcept\s*\{.*?\*ppvSite\s*=\s*nullptr\s*;.*?try\s*\{.*?std::lock_guard<std::mutex>\s+lock\(siteMutex\).*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*\*ppvSite\s*=\s*nullptr\s*;.*?FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerFactorySection,
        '(?s)CreateInstance\(.*?\)\s+noexcept\s+override\s+try.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\(.*?try\s*\{.*?Wh_Log\(.*?catch\s*\(\s*\.\.\.\s*\)'
    ) -and
    [regex]::IsMatch(
        $stylerFactorySection,
        '(?s)LockServer\(BOOL lock\)\s+noexcept\s+override\s+try.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    $phase2Harness.Contains('module.tap-com-boundary-faults')
Add-Check `
    'styler.tap-com-total-firewall' `
    $phase2TapComTotalFirewall `
    'TAP SetSite/GetSite and both IClassFactory methods must contain lock, allocation, projected-object and nested diagnostic failures while keeping outputs null and latching fail-closed.'

$stylerAcquirePinSection = Get-SourceSlice `
    -Text $stylerUnloadSafetyPinSection `
    -StartMarker 'bool AcquireUnloadSafetyModulePin()' `
    -EndMarker 'bool ReleaseUnloadSafetyModulePin() noexcept'
$phase2ModuleReferenceRaii =
    [regex]::IsMatch(
        $stylerModuleReferenceOwnerSection,
        '(?s)class\s+NoThrowModuleReferenceOwner.*?~NoThrowModuleReferenceOwner\(\)\s+noexcept.*?std::exchange\(m_module,\s*nullptr\).*?FreeLibrary\(module\).*?LatchJarvisActivationQuiesced\(.*?RequirePermanentUnloadSafetyPin\('
    ) -and
    [regex]::IsMatch(
        $stylerInjectTapSection,
        '(?s)LoadLibraryEx\(.*?NoThrowModuleReferenceOwner\s+wuxOwner\(.*?AdoptConfirmed\(wux\).*?wuxOwner\.ReleaseNow\('
    ) -and
    [regex]::IsMatch(
        $stylerHostBinarySection,
        '(?s)NoThrowModuleReferenceOwner\s+acquiredReference\(.*?GetModuleHandleExW\(.*?AdoptConfirmed\(taskbarViewModule\).*?g_validatedTaskbarViewModule\s*=\s*acquiredReference\.Detach\(\)'
    ) -and
    -not $stylerHostBinarySection.Contains(
        'bool acquiredReference'
    ) -and
    [regex]::IsMatch(
        $stylerReleaseValidatedModuleSection,
        '(?s)NoThrowModuleReferenceOwner\s+owner\(.*?AdoptConfirmed\(module\).*?ReleaseNow\(.*?retainedModule.*?g_validatedTaskbarViewModule\s*=\s*retainedModule'
    ) -and
    [regex]::IsMatch(
        $stylerAcquirePinSection,
        '(?s)acquireTicket\s*=\s*ClaimUnloadSafetyPinTicketUnderDecisionGate\(\).*?g_unloadSafetyPinAcquirePurpose\s*=\s*UnloadSafetyPinAcquirePurpose::Initial;.*?\}\s*.*?GetModuleHandleExW\(.*?g_unloadSafetyPinAcquireTicket\s*==\s*acquireTicket\s*&&\s*g_unloadSafetyPinAcquirePurpose\s*==\s*UnloadSafetyPinAcquirePurpose::Initial.*?PublishOwnedUnloadSafetyPinUnderDecisionGate\(module\).*?g_unloadSafetyPinAcquireTicket\s*=\s*0'
    ) -and
    -not [regex]::IsMatch(
        $stylerAcquirePinSection,
        '(?s)UnloadSafetyPinDecisionGuard\s+decisionGuard[^}]*GetModuleHandleExW\('
    ) -and
    -not $stylerAcquirePinSection.Contains(
        'FreeLibrary('
    ) -and
    [regex]::IsMatch(
        $stylerXamlBlurDisconnectedSection,
        '(?s)TapLifecycleScope\s+callbackScope\(.*?if\s*\(\s*!callbackScope\s*\)\s*\{\s*return\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerStatsTimerSection,
        '(?s)CreateThreadpoolTimer\(\s*\[\]\(.*?\)\s+noexcept\s*\{\s*try\s*\{.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    $phase2Harness.Contains('module.loader-reference-raii')
Add-Check `
    'styler.module-reference-raii' `
    $phase2ModuleReferenceRaii `
    'Temporary XAML, Taskbar.View and unload-pin HMODULE references must be scope-owned, explicitly transferred, and released outside internal decision locks; callback admission and dormant timer ABI boundaries remain contained.'

$phase2NoexceptDiagnosticContainment =
    [regex]::IsMatch(
        $stylerXamlHostEnumerationSection,
        '(?s)XamlHostWindowSnapshot\s+GetXamlHostWnds\(\)\s+noexcept.*?try\s*\{\s*Wh_Log\(.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerCleanupStepsSection,
        '(?s)RunUiThreadCleanupSteps\(.*?\)\s+noexcept.*?catch\s*\(\s*\.\.\.\s*\).*?try\s*\{\s*Wh_Log\(.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerGetGitSection,
        '(?s)CapacityExceeded.*?RetainVisualTreeServiceGit\(.*?LatchJarvisActivationQuiesced\(.*?try\s*\{\s*Wh_Log\(.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerRetainGitSection,
        '(?s)LatchJarvisActivationQuiesced\(.*?RequirePermanentUnloadSafetyPin\(.*?try\s*\{\s*Wh_Log\(.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    $phase2Harness.Contains('module.noexcept-diagnostic-failures')
Add-Check `
    'styler.noexcept-diagnostics-contained' `
    $phase2NoexceptDiagnosticContainment `
    'Every audited noexcept diagnostic path must publish its receipt and fail-closed state before a nested logging firewall; logging failure may not alter the primary HRESULT or terminate Explorer.'

$phase2XamlBrushCallbackFirewalls =
    [regex]::IsMatch(
        $stylerXamlBlurClassSection,
        '(?s)~XamlBlurBrush\(\)\s+noexcept\s*;.*?void\s+OnConnected\(\)\s+noexcept\s*;.*?void\s+OnDisconnected\(\)\s+noexcept\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerXamlBlurDestructorSection,
        '(?s)~XamlBlurBrush\(\)\s+noexcept\s*\{.*?try\s*\{.*?catch\s*\(\s*\.\.\.\s*\)\s*\{.*?FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerXamlBlurConnectedSection,
        '(?s)OnConnected\(\)\s+noexcept\s*\{.*?try\s*\{.*?TapLifecycleScope.*?catch\s*\(\s*\.\.\.\s*\)\s*\{.*?FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerXamlBlurDisconnectedSection,
        '(?s)OnDisconnected\(\)\s+noexcept\s*\{\s*try\s*\{.*?TapLifecycleScope.*?catch\s*\(\s*\.\.\.\s*\)\s*\{.*?FailClosedForeignAbiException\('
    )
Add-Check `
    'styler.xaml-brush-callback-firewalls' `
    $phase2XamlBrushCallbackFirewalls `
    'XamlBlurBrush final Release, OnConnected, and OnDisconnected must be explicit noexcept boundaries that quiesce through the unified fail-closed helper.'

$phase2ProjectedDelegateFirewalls =
    [regex]::IsMatch(
        $stylerProjectedCallbackHelperSection,
        '(?s)RunProjectedCallbackNoThrow\(.*?\)\s+noexcept\s*\{.*?try\s*\{.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    [regex]::Matches(
        $styler,
        'RunProjectedCallbackNoThrow\('
    ).Count -ge 7 -and
    [regex]::IsMatch(
        $styler,
        '(?s)RegisterPropertyChangedCallback\(.*?\]\(.*?\)\s+noexcept.*?RunProjectedCallbackNoThrow\('
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)AdvancedEffectsEnabledChanged\(.*?\]\(.*?\)\s+noexcept.*?RunProjectedCallbackNoThrow\('
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)EnergySaverStatusChanged\(.*?\]\(.*?\)\s+noexcept.*?RunProjectedCallbackNoThrow\('
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)TryEnqueue\(.*?\]\(\)\s+noexcept.*?RunProjectedCallbackNoThrow\('
    )
Add-Check `
    'styler.projected-delegate-firewalls' `
    $phase2ProjectedDelegateFirewalls `
    'Projected property, power, settings, and dispatcher delegates must enter through a reusable noexcept fail-closed callback firewall.'

$stylerWithoutElementMutationGuard = $styler.Replace(
    $stylerElementMutationGuardSection,
    ''
)
$phase2ScopedElementPropertyMutation =
    [regex]::IsMatch(
        $stylerElementMutationGuardSection,
        '(?s)class\s+ScopedElementPropertyMutation.*?ScopedElementPropertyMutation\(\)\s+noexcept.*?previous_\(g_elementPropertyModifying\).*?g_elementPropertyModifying\s*=\s*true.*?~ScopedElementPropertyMutation\(\)\s+noexcept.*?g_elementPropertyModifying\s*=\s*previous_'
    ) -and
    [regex]::Matches(
        $stylerWithoutElementMutationGuard,
        '\bScopedElementPropertyMutation\s+[A-Za-z_][A-Za-z0-9_]*'
    ).Count -ge 4 -and
    -not [regex]::IsMatch(
        $stylerWithoutElementMutationGuard,
        'g_elementPropertyModifying\s*=\s*(?:true|false|wasModifying)\b'
    )
Add-Check `
    'styler.scoped-element-property-mutation' `
    $phase2ScopedElementPropertyMutation `
    'Every property-mutation suppression window must restore its prior TLS state through ScopedElementPropertyMutation, including exception and early-return paths.'

$phase2DormantStatsRaii =
    [regex]::IsMatch(
        $stylerStatsTimerSection,
        '(?s)class\s+StatsMutexOwner\s*\{.*?PublishKernelCapability\(.*?RetainedKernelCapabilityKind::StatsMutex.*?~StatsMutexOwner\(\)\s+noexcept.*?ReleaseMutex\(.*?RetainKernelCapability\(.*?RetainedKernelCapabilityKind::StatsMutex.*?RetainedKernelCapabilityDisposition::\s*MutexReleasePending.*?handle_\s*=\s*nullptr\s*;.*?owned_\s*=\s*false\s*;.*?CloseOrRetainKernelCapability\(.*?RetainedKernelCapabilityKind::StatsMutex.*?IsKernelCapabilityPhysicallyClosed\(closeOutcome\)'
    ) -and
    [regex]::IsMatch(
        $stylerStatsTimerSection,
        '(?s)class\s+StatsUrlContentOwner\s*\{.*?~StatsUrlContentOwner\(\)\s+noexcept.*?Wh_FreeUrlContent\('
    ) -and
    [regex]::IsMatch(
        $stylerStatsTimerSection,
        '(?s)CreateThreadpoolTimer\(\s*\[\]\(.*?\)\s+noexcept\s*\{.*?try\s*\{.*?catch\s*\(\s*\.\.\.\s*\)\s*\{\s*FailClosedForeignAbiException\('
    ) -and
    -not [regex]::IsMatch(
        $stylerStatsTimerSection,
        '(?m)^\s*HANDLE\s+mutex\s*='
    ) -and
    -not [regex]::IsMatch(
        $stylerStatsTimerSection,
        '(?m)^\s*const\s+WH_URL_CONTENT\*\s+content\s*='
    )
Add-Check `
    'styler.dormant-stats-raii' `
    $phase2DormantStatsRaii `
    'The dormant stats callback must give its mutex a nonzero owner ticket and retain an exact release-pending or close-failed receipt through noexcept RAII even though telemetry remains disabled.'

$phase2ConcreteKernelKinds = @(
    'VisualTreeAdviseThread',
    'VisualTreeUnadviseThread',
    'UiThread',
    'UiCleanupEvent',
    'StateGate',
    'ActivationPermit',
    'KillSwitchWatcherThread',
    'KillSwitchWatcherStopEvent',
    'KillSwitchWatcherChangeNotification',
    'StatsMutex',
    'XamlRegistryNotificationEvent',
    'XamlRegistryKey',
    'XamlRegistryWait'
).Where({
    -not $stylerKernelCapabilitySection.Contains(
        "RetainedKernelCapabilityKind::$_"
    )
}).Count -eq 0

$phase2KernelStateMachine =
    [regex]::IsMatch(
        $stylerKernelCapabilitySection,
        '(?s)enum\s+class\s+KernelCapabilitySlotState.*?Empty.*?Reserved.*?LiveLocal.*?Retained'
    ) -and
    [regex]::IsMatch(
        $stylerKernelCapabilitySection,
        '(?s)enum\s+class\s+KernelCapabilityCloseOutcome.*?NoOwner.*?Closed.*?Transferred.*?StillOwned'
    ) -and
    [regex]::IsMatch(
        $stylerKernelCapabilitySection,
        '(?s)ReserveKernelCapability\(.*?slot\.state\s*!=\s*KernelCapabilitySlotState::Empty.*?slot\.kind\s*=\s*kind\s*;.*?slot\.ownerIdentity\s*=\s*ownerIdentity\s*;.*?slot\.state\s*=\s*KernelCapabilitySlotState::Reserved'
    ) -and
    [regex]::IsMatch(
        $stylerKernelCapabilitySection,
        '(?s)PublishKernelCapability\(.*?slot\.state\s*==\s*KernelCapabilitySlotState::LiveLocal\s*&&\s*slot\.handle\s*==\s*handle.*?return\s+true\s*;.*?slot\.state\s*!=\s*KernelCapabilitySlotState::Reserved.*?slot\.handle\s*=\s*handle\s*;.*?slot\.state\s*=\s*KernelCapabilitySlotState::LiveLocal'
    ) -and
    [regex]::IsMatch(
        $stylerKernelCapabilitySection,
        '(?s)CancelKernelCapabilityReservation\(.*?slot\.state\s*!=\s*KernelCapabilitySlotState::Reserved\s*\|\|\s*slot\.handle.*?slot\s*=\s*\{\}\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerKernelCapabilitySection,
        '(?s)RetainKernelCapability\(.*?slot\.state\s*==\s*KernelCapabilitySlotState::Retained.*?slot\.handle\s*==\s*handle.*?\(\s*slot\.state\s*==\s*KernelCapabilitySlotState::Reserved\s*&&\s*!slot\.handle\s*\).*?\(\s*slot\.state\s*==\s*KernelCapabilitySlotState::LiveLocal\s*&&\s*slot\.handle\s*==\s*handle\s*\).*?slot\.state\s*=\s*KernelCapabilitySlotState::Retained'
    ) -and
    [regex]::IsMatch(
        $stylerKernelCapabilitySection,
        '(?s)CloseOrRetainKernelCapability\(.*?if\s*\(\s*CloseHandle\(handle\)\s*\).*?\*owner\s*=\s*nullptr\s*;.*?CompleteKernelCapabilityClose\(.*?return\s+KernelCapabilityCloseOutcome::Closed\s*;.*?if\s*\(\s*RetainKernelCapability\(.*?\)\s*\).*?\*owner\s*=\s*nullptr\s*;.*?return\s+KernelCapabilityCloseOutcome::Transferred\s*;.*?return\s+KernelCapabilityCloseOutcome::StillOwned\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerKernelCapabilitySection,
        '(?s)CloseOrRetainChangeNotification\(.*?if\s*\(\s*FindCloseChangeNotification\(handle\)\s*\).*?\*owner\s*=\s*INVALID_HANDLE_VALUE\s*;.*?return\s+KernelCapabilityCloseOutcome::Closed\s*;.*?if\s*\(\s*RetainKernelCapability\(.*?\)\s*\).*?\*owner\s*=\s*INVALID_HANDLE_VALUE\s*;.*?return\s+KernelCapabilityCloseOutcome::Transferred\s*;.*?return\s+KernelCapabilityCloseOutcome::StillOwned\s*;'
    )

$phase2TrackedKernelAcquisitionPlans = @(
    [pscustomobject]@{
        name = 'visual-tree-advise-thread'
        text = $stylerWatcherConstructorSection
        markers = @(
            'ReserveKernelCapability(',
            'm_adviseThread = CreateThread(',
            'CancelKernelCapabilityReservation(',
            'PublishKernelCapability('
        )
    }
    [pscustomobject]@{
        name = 'visual-tree-unadvise-thread'
        text = $stylerWatcherUnadviseSection
        markers = @(
            'ReserveKernelCapability(',
            'm_unadviseThread = CreateThread(',
            'CancelKernelCapabilityReservation(',
            'PublishKernelCapability('
        )
    }
    [pscustomobject]@{
        name = 'ui-thread-duplicate'
        text = $stylerCurrentUiThreadIdentitySection
        markers = @(
            'ReserveKernelCapability(',
            'DuplicateHandle(',
            'CancelKernelCapabilityReservation(',
            'UiUniqueHandle duplicatedOwner('
        )
    }
    [pscustomobject]@{
        name = 'ui-cleanup-event'
        text = $stylerInitializeForThreadSection
        markers = @(
            'const std::uint64_t cleanupEventOwnerIdentity =',
            'ReserveKernelCapability(',
            'CreateEventW(',
            'CancelKernelCapabilityReservation(',
            'ownedCleanupCompletedEvent = UiUniqueHandle('
        )
    }
    [pscustomobject]@{
        name = 'state-gate'
        text = $stylerStateGateSection
        markers = @(
            'ReserveKernelCapability(',
            'CreateSemaphoreW(',
            'CancelKernelCapabilityReservation(',
            'UiUniqueHandle stateGate('
        )
    }
    [pscustomobject]@{
        name = 'activation-permit'
        text = $stylerActivationPermitSection
        markers = @(
            'ReserveKernelCapability(',
            'CreateFileW(',
            'CancelKernelCapabilityReservation(',
            'UiUniqueHandle permit('
        )
    }
    [pscustomobject]@{
        name = 'kill-watcher-stop-event'
        text = $stylerWatcherStopEventAcquisitionSection
        markers = @(
            'ReserveKernelCapability(',
            'CreateEventW(',
            'CancelKernelCapabilityReservation(',
            'PublishKernelCapability('
        )
    }
    [pscustomobject]@{
        name = 'kill-watcher-change-notification'
        text = $stylerWatcherChangeAcquisitionSection
        markers = @(
            'ReserveKernelCapability(',
            'FindFirstChangeNotificationW(',
            'CancelKernelCapabilityReservation(',
            'PublishKernelCapability('
        )
    }
    [pscustomobject]@{
        name = 'kill-watcher-thread'
        text = $stylerWatcherThreadAcquisitionSection
        markers = @(
            'ReserveKernelCapability(',
            'CreateThread(',
            'CancelKernelCapabilityReservation(',
            'PublishKernelCapability('
        )
    }
    [pscustomobject]@{
        name = 'stats-mutex'
        text = $stylerStatsTimerSection
        markers = @(
            'ReserveKernelCapability(',
            'HANDLE rawMutex = CreateMutex(',
            'CancelKernelCapabilityReservation(',
            'StatsMutexOwner mutex('
        )
    }
    [pscustomobject]@{
        name = 'xaml-registry-key'
        text = $stylerXamlRegistryKeyAcquireSection
        markers = @(
            'ReserveKernelCapability(',
            'RegOpenKeyExW(',
            'CancelKernelCapabilityReservation(',
            'PublishKernelCapability('
        )
    }
    [pscustomobject]@{
        name = 'xaml-registry-notification-event'
        text = $stylerXamlRegistryEventAcquireSection
        markers = @(
            'ReserveKernelCapability(',
            'CreateEventW(',
            'CancelKernelCapabilityReservation(',
            'PublishKernelCapability('
        )
    }
    [pscustomobject]@{
        name = 'xaml-registry-wait'
        text = $stylerXamlRegistryWaitAcquireSection
        markers = @(
            'ReserveKernelCapability(',
            'BindXamlRegistryWaitBundle(',
            'RegisterWaitForSingleObject(',
            'CancelKernelCapabilityReservation(',
            'PublishKernelCapability('
        )
    }
)
$phase2KernelAcquisitionFailures = @(
    foreach ($plan in $phase2TrackedKernelAcquisitionPlans) {
        if (-not (Test-MarkersInOrder `
                    -Text $plan.text `
                    -Markers $plan.markers)) {
            $plan.name
        }
    }
)

$phase2KernelStillOwnedRetryable =
    ([regex]::Matches(
        $stylerUiHandleCapabilitySection,
        'closeOutcome\s*==\s*KernelCapabilityCloseOutcome::Transferred'
    ).Count -eq 2) -and
    [regex]::IsMatch(
        $stylerUiHandleCapabilitySection,
        '(?s)if\s*\(\s*cleanupCompletedEvent\s*\).*?CloseOrRetainKernelCapability\(.*?UiCleanupEvent.*?if\s*\(\s*IsKernelCapabilityPhysicallyClosed\(\s*closeOutcome\s*\)\s*\).*?releasedMask.*?else\s+if\s*\(\s*closeOutcome\s*==\s*KernelCapabilityCloseOutcome::Transferred\s*\)\s*\{.*?retainedMask.*?\}\s*else\s+if\s*\(\s*SUCCEEDED\(disposition\.error\)\s*\)\s*\{(?:(?!retainedMask).)*?HRESULT_FROM_WIN32\(closeError\)'
    ) -and
    [regex]::IsMatch(
        $stylerUiHandleCapabilitySection,
        '(?s)if\s*\(\s*threadHandle\s*&&.*?CloseOrRetainKernelCapability\(.*?UiThread.*?if\s*\(\s*IsKernelCapabilityPhysicallyClosed\(\s*closeOutcome\s*\)\s*\).*?releasedMask.*?else\s+if\s*\(\s*closeOutcome\s*==\s*KernelCapabilityCloseOutcome::Transferred\s*\)\s*\{.*?retainedMask.*?\}\s*else\s+if\s*\(\s*SUCCEEDED\(disposition\.error\)\s*\)\s*\{(?:(?!retainedMask).)*?HRESULT_FROM_WIN32\(closeError\)'
    )

$phase2StateGateSingleRelease =
    ([regex]::Matches(
        $stylerStateGateSection,
        '\bReleaseSemaphore\s*\('
    ).Count -eq 1) -and
    ([regex]::Matches(
        $stylerStateGateSection,
        'm_semaphoreReleased\s*=\s*true\s*;'
    ).Count -eq 1) -and
    ([regex]::Matches(
        $stylerStateGateSection,
        'm_semaphoreReleased\s*=\s*false\s*;'
    ).Count -eq 1) -and
    [regex]::IsMatch(
        $stylerStateGateSection,
        '(?s)if\s*\(\s*!m_semaphoreReleased\s*\)\s*\{\s*if\s*\(\s*!ReleaseSemaphore\(.*?\)\s*\)\s*\{.*?RetainWithoutClose\(\s*RetainedKernelCapabilityDisposition::ReleasePending.*?return\s+false\s*;.*?m_semaphoreReleased\s*=\s*true\s*;\s*\}.*?m_stateGate\.Reset\(\)'
    ) -and
    (Test-MarkersInOrder `
        -Text $stylerStateGateSection `
        -Markers @(
            'if (!m_semaphoreReleased)',
            'ReleaseSemaphore(',
            'm_semaphoreReleased = true;',
            'm_stateGate.Reset()'
        ))

$phase2KernelCapabilityOwnership =
    $phase2ConcreteKernelKinds -and
    $phase2KernelStateMachine -and
    $phase2KernelAcquisitionFailures.Count -eq 0 -and
    $phase2KernelStillOwnedRetryable -and
    $phase2StateGateSingleRelease -and
    $stylerKernelCapabilitySection.Contains(
        'g_kernelCapabilityOwnerTicket'
    ) -and
    [regex]::IsMatch(
        $stylerKernelCapabilitySection,
        '(?s)ClaimKernelCapabilityOwnerTicket\(\)\s+noexcept.*?fetch_add\(.*?\)\s*\+\s*1.*?if\s*\(\s*ticket\s*==\s*0\s*\)'
    ) -and
    $stylerKernelCapabilitySection.Contains(
        'RetainedKernelCapabilityDisposition::ReleasePending'
    ) -and
    $stylerKernelCapabilitySection.Contains(
        'RetainedKernelCapabilityDisposition::DeletePendingCloseFailed'
    ) -and
    $stylerKernelCapabilitySection.Contains(
        'CloseOrRetainChangeNotification('
    ) -and
    $stylerKernelCapabilitySection.Contains(
        'FindCloseChangeNotification(handle)'
    ) -and
    -not [regex]::IsMatch(
        $stylerUiUniqueHandleSection,
        'explicit\s+UiUniqueHandle\(\s*HANDLE\s+value\s*\)\s+noexcept'
    ) -and
    [regex]::IsMatch(
        $stylerStateGateSection,
        '(?s)UiUniqueHandle\s+AcquireJarvisStateGate\(\).*?ReserveKernelCapability\(.*?RetainedKernelCapabilityKind::StateGate.*?CancelKernelCapabilityReservation\(.*?class\s+JarvisStateGateGuard.*?UiUniqueHandle\s+m_stateGate\s*;.*?bool\s+m_semaphoreReleased\s*=\s*false\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerStateGateSection,
        '(?s)if\s*\(\s*!ReleaseSemaphore\(.*?RetainWithoutClose\(\s*RetainedKernelCapabilityDisposition::ReleasePending.*?return false\s*;.*?m_stateGate\.Reset\(\)'
    ) -and
    [regex]::IsMatch(
        $stylerActivationPermitSection,
        '(?s)UiUniqueHandle\s+OpenValidatedActivationPermit\(\).*?ReserveKernelCapability\(.*?RetainedKernelCapabilityKind::ActivationPermit.*?CancelKernelCapabilityReservation\(.*?SetFileInformationByHandle\(.*?permit\.Reset\(\s*nullptr,\s*RetainedKernelCapabilityDisposition::\s*DeletePendingCloseFailed'
    ) -and
    $styler.Contains(
        'RetainedKernelCapabilityKind::KillSwitchWatcherThread'
    ) -and
    $styler.Contains(
        'RetainedKernelCapabilityKind::KillSwitchWatcherStopEvent'
    ) -and
    $styler.Contains(
        'RetainedKernelCapabilityKind::KillSwitchWatcherChangeNotification'
    ) -and
    $stylerStartKillSwitchWatcherSection.Contains(
        'ReserveKernelCapability('
    ) -and
    $stylerStopKillSwitchWatcherSection.Contains(
        'return threadClosed && changeNotificationClosed &&'
    ) -and
    -not [regex]::IsMatch(
        $styler,
        'CloseHandle\s*\(\s*g_killSwitchWatcher|FindCloseChangeNotification\s*\(\s*g_killSwitchWatcher'
    ) -and
    $phase2Harness.Contains(
        'kernel-owner-tickets-nonzero-monotonic'
    ) -and
    $phase2Harness.Contains(
        'state-gate-release-failure-retains-without-close'
    ) -and
    $phase2Harness.Contains(
        'delete-pending-permit-close-failure-retained'
    ) -and
    $phase2Harness.Contains(
        'watcher-change-notification-uses-specific-close'
    ) -and
    $phase2Harness.Contains(
        'stats-mutex-release-failure-retains-owned-capability'
    )
Add-Check `
    'phase2.kernel-capability-terminal-ownership' `
    $phase2KernelCapabilityOwnership `
    ('Every tracked kernel capability must reserve a fixed slot before Create, DuplicateHandle, or FindFirstChangeNotification, publish or cancel that exact slot, preserve StillOwned as retryable rather than terminal-retained, release the state-gate semaphore at most once, and retain the previous concrete-kind, owner-ticket, checked-close, and 90-ID fault boundaries. Acquisition failures: ' +
     ($phase2KernelAcquisitionFailures -join ', '))

$phase2UiUniqueHandlePublicationContract =
    [regex]::IsMatch(
        $stylerUiUniqueHandleSection,
        '(?s)UiUniqueHandle\(.*?else\s+if\s*\(\s*value_\s*&&\s*value_\s*!=\s*INVALID_HANDLE_VALUE\s*\)\s*\{\s*published_\s*=\s*PublishKernelCapability\(.*?if\s*\(\s*!published_\s*\).*?FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerUiUniqueHandleSection,
        '(?s)explicit\s+operator\s+bool\(\)\s+const\s+noexcept\s*\{\s*return\s+value_\s*!=\s*nullptr\s*&&\s*value_\s*!=\s*INVALID_HANDLE_VALUE\s*&&\s*published_\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerUiUniqueHandleSection,
        '(?s)HANDLE\s+Release\(\)\s+noexcept\s*\{.*?value_\s*=\s*nullptr\s*;.*?published_\s*=\s*false\s*;.*?return\s+value\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerUiUniqueHandleSection,
        '(?s)if\s*\(\s*previous\s*\)\s*\{.*?value_\s*=\s*previous\s*;.*?return\s+false\s*;\s*\}.*?value_\s*=\s*value\s*;\s*published_\s*=\s*false\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerUiUniqueHandleSection,
        '(?s)RetainWithoutClose\(.*?RetainKernelCapability\(.*?value_\s*=\s*nullptr\s*;\s*published_\s*=\s*false\s*;'
    ) -and
    $stylerUiUniqueHandleSection.Contains(
        'published_ = std::exchange(other.published_, false);'
    ) -and
    $stylerUiUniqueHandleSection.Contains(
        'bool published_ = false;'
    )
Add-Check `
    'phase2.ui-unique-handle-published-state' `
    $phase2UiUniqueHandlePublicationContract `
    'UiUniqueHandle truthiness and every transfer, release, reset, retained transfer, and move must propagate the exact PublishKernelCapability result; a publication failure may never masquerade as a usable owner.'

$phase2XamlRegistryReserveBeforeExternalCreate =
    (Test-MarkersInOrder `
        -Text $stylerXamlRegistryKeyAcquireSection `
        -Markers @(
            'ReserveKernelCapability(',
            'RegOpenKeyExW(',
            'CancelKernelCapabilityReservation(',
            'PublishKernelCapability('
        )) -and
    (Test-MarkersInOrder `
        -Text $stylerXamlRegistryEventAcquireSection `
        -Markers @(
            'ReserveKernelCapability(',
            'CreateEventW(',
            'CancelKernelCapabilityReservation(',
            'PublishKernelCapability('
        )) -and
    (Test-MarkersInOrder `
        -Text $stylerXamlRegistryWaitAcquireSection `
        -Markers @(
            'ReserveKernelCapability(',
            'BindXamlRegistryWaitBundle(',
            'RegisterWaitForSingleObject(',
            'CancelKernelCapabilityReservation(',
            'PublishKernelCapability('
        ))

$phase2XamlRegistryBundleBinding =
    [regex]::IsMatch(
        $stylerXamlRegistryBundleSection,
        '(?s)BindXamlRegistryWaitBundle\(.*?waitSlot->opaqueDependentContext\s*=.*?waitSlot->dependencyOwnerIdentity1\s*=\s*eventOwnerIdentity\s*;.*?waitSlot->dependencyOwnerIdentity2\s*=\s*keyOwnerIdentity\s*;.*?eventSlot->dependencyOwnerIdentity1\s*=\s*waitOwnerIdentity\s*;.*?keySlot->dependencyOwnerIdentity1\s*=\s*waitOwnerIdentity\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerXamlRegistryBundleSection,
        '(?s)RetainXamlRegistryWaitBundle\(.*?const\s+bool\s+exactBundle\s*=.*?waitSlot->opaqueDependentContext\s*!=\s*0.*?dependencyOwnerIdentity1\s*==\s*eventOwnerIdentity.*?dependencyOwnerIdentity2\s*==\s*keyOwnerIdentity.*?eventSlot->dependencyOwnerIdentity1\s*==\s*waitOwnerIdentity.*?keySlot->dependencyOwnerIdentity1\s*==\s*waitOwnerIdentity.*?waitSlot->state\s*=\s*KernelCapabilitySlotState::Retained\s*;.*?eventSlot->state\s*=\s*KernelCapabilitySlotState::Retained\s*;.*?keySlot->state\s*=\s*KernelCapabilitySlotState::Retained\s*;'
    )

$phase2XamlPublishFailureRollback =
    ([regex]::Matches(
        $stylerXamlBlurConstructorSection,
        'if\s*\(\s*!PublishKernelCapability\('
    ).Count -eq 3) -and
    [regex]::IsMatch(
        $stylerXamlRegistryKeyAcquireSection,
        '(?s)if\s*\(\s*!PublishKernelCapability\(.*?XamlRegistryKey.*?\)\s*\)\s*\{.*?CloseOrRetainRegistryKey\(.*?if\s*\(\s*!m_powerKey\s*\).*?m_powerKeyOwnerIdentity\s*=\s*0\s*;.*?return\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerXamlRegistryEventAcquireSection,
        '(?s)if\s*\(\s*!PublishKernelCapability\(.*?XamlRegistryNotificationEvent.*?\)\s*\)\s*\{.*?CloseOrRetainKernelCapability\(.*?XamlRegistryNotificationEvent.*?if\s*\(\s*!m_regNotifyEvent\s*\).*?m_regNotifyEventOwnerIdentity\s*=\s*0\s*;.*?CloseOrRetainRegistryKey\(.*?return\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerXamlRegistryWaitAcquireSection,
        '(?s)if\s*\(\s*!PublishKernelCapability\(.*?XamlRegistryWait.*?\)\s*\)\s*\{.*?UnregisterWaitEx\(\s*m_regWaitHandle,\s*INVALID_HANDLE_VALUE\s*\).*?if\s*\(\s*unregisterConfirmed\s*\).*?CompleteKernelCapabilityClose\(.*?CloseOrRetainKernelCapability\(.*?CloseOrRetainRegistryKey\(.*?\}\s*else\s*\{.*?RetainXamlRegistryWaitBundle\(.*?m_regWaitContext\s*=\s*nullptr\s*;.*?\}\s*return\s*;'
    )

$phase2XamlUnconfirmedBundleOnly =
    $stylerXamlConstructorRetainedBundleSection.Contains(
        'RetainXamlRegistryWaitBundle('
    ) -and
    $stylerXamlDestructorRetainedBundleSection.Contains(
        'RetainXamlRegistryWaitBundle('
    ) -and
    [regex]::IsMatch(
        $stylerXamlConstructorRetainedBundleSection,
        '(?s)const\s+bool\s+bundleRetained\s*=\s*RetainXamlRegistryWaitBundle\(.*?\)\s*;\s*if\s*\(\s*!bundleRetained\s*\)\s*\{\s*LogJarvisDiagnosticNoThrow\('
    ) -and
    [regex]::IsMatch(
        $stylerXamlDestructorRetainedBundleSection,
        '(?s)const\s+bool\s+bundleRetained\s*=\s*RetainXamlRegistryWaitBundle\(.*?\)\s*;\s*if\s*\(\s*!bundleRetained\s*\)\s*\{\s*LogJarvisDiagnosticNoThrow\('
    ) -and
    ([regex]::Matches(
        $stylerXamlBlurConstructorSection,
        '\bRetainXamlRegistryWaitBundle\('
    ).Count -eq 1) -and
    ([regex]::Matches(
        $stylerXamlBlurDestructorSection,
        '\bRetainXamlRegistryWaitBundle\('
    ).Count -eq 1) -and
    [regex]::IsMatch(
        $stylerXamlConstructorRetainedBundleSection,
        '(?s)m_regWaitHandle\s*=\s*nullptr\s*;.*?m_regNotifyEvent\s*=\s*nullptr\s*;.*?m_powerKey\s*=\s*nullptr\s*;.*?m_regWaitContext\s*=\s*nullptr\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerXamlDestructorRetainedBundleSection,
        '(?s)m_regNotifyEvent\s*=\s*nullptr\s*;.*?m_powerKey\s*=\s*nullptr\s*;.*?m_regWaitContext\s*=\s*nullptr\s*;'
    ) -and
    -not [regex]::IsMatch(
        $stylerXamlConstructorRetainedBundleSection,
        '\b(?:CloseOrRetainKernelCapability|CloseOrRetainRegistryKey|RetainKernelCapability|CloseHandle|RegCloseKey)\s*\(|delete\s+std::exchange\(\s*m_regWaitContext'
    ) -and
    -not [regex]::IsMatch(
        $stylerXamlDestructorRetainedBundleSection,
        '\b(?:CloseOrRetainKernelCapability|CloseOrRetainRegistryKey|RetainKernelCapability|CloseHandle|RegCloseKey)\s*\(|delete\s+std::exchange\(\s*m_regWaitContext'
    )

$phase2ShouldUseFallbackTrackedKey =
    ([regex]::Matches(
        $stylerXamlShouldUseFallbackSection,
        'ReserveKernelCapability\('
    ).Count -eq 1) -and
    ([regex]::Matches(
        $stylerXamlShouldUseFallbackSection,
        'RegOpenKeyExW\('
    ).Count -eq 1) -and
    ([regex]::Matches(
        $stylerXamlShouldUseFallbackSection,
        'PublishKernelCapability\('
    ).Count -eq 1) -and
    ([regex]::Matches(
        $stylerXamlShouldUseFallbackSection,
        'CloseOrRetainRegistryKey\('
    ).Count -eq 2) -and
    [regex]::IsMatch(
        $stylerXamlShouldUseFallbackSection,
        '(?s)keyOwnerIdentity\s*=\s*ReserveKernelCapability\(.*?XamlRegistryKey.*?keyOwnerIdentity\s*!=\s*0\s*&&\s*RegOpenKeyExW\(.*?if\s*\(\s*!PublishKernelCapability\(.*?XamlRegistryKey.*?\)\s*\).*?CloseOrRetainRegistryKey\(.*?return\s+true\s*;.*?RegQueryValueExW\(.*?CloseOrRetainRegistryKey\(.*?else\s+if\s*\(\s*keyOwnerIdentity\s*!=\s*0\s*\).*?CancelKernelCapabilityReservation\('
    ) -and
    -not $stylerXamlShouldUseFallbackSection.Contains(
        'RegCloseKey('
    )

$phase2XamlRegistryWaitBundleOwnership =
    $phase2XamlRegistryReserveBeforeExternalCreate -and
    $phase2XamlRegistryBundleBinding -and
    $phase2XamlPublishFailureRollback -and
    $phase2XamlUnconfirmedBundleOnly -and
    $phase2ShouldUseFallbackTrackedKey
Add-Check `
    'phase2.xaml-registry-wait-bundle-ownership' `
    $phase2XamlRegistryWaitBundleOwnership `
    'The XAML registry key, notification event, and wait must reserve before external acquisition; bind all dependencies before wait publication; perform checked, returning rollback on all three publication failures; quarantine an unconfirmed unregister only as one exact bundle; and track the temporary ShouldUseFallback HKEY through typed close.'

$phase2NativeCallbackExplicitNoexcept =
    [regex]::IsMatch(
        $styler,
        '(?s)DWORD\s+WINAPI\s+KillSwitchWatcherThread\(\s*void\*\s*\)\s+noexcept\s+try\s*\{.*?\}\s*catch\s*\(\s*\.\.\.\s*\)\s*\{.*?FailClosedForeignAbiException\('
    ) -and
    [regex]::IsMatch(
        $stylerWatcherClassSection,
        '(?s)HRESULT\s+STDMETHODCALLTYPE\s+OnVisualTreeChange\(.*?VisualMutationType\s+mutationType\s*\)\s+noexcept\s+override\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerVisualTreeChangeSection,
        '(?s)HRESULT\s+VisualTreeWatcher::OnVisualTreeChange\(.*?VisualMutationType\s+mutationType\s*\)\s+noexcept\s+try\s*\{.*?\}\s*catch\s*\(\s*\.\.\.\s*\)\s*\{.*?FailClosedForeignAbiException\('
    )
Add-Check `
    'phase2.native-callback-explicit-noexcept' `
    $phase2NativeCallbackExplicitNoexcept `
    'The kill-switch watcher thread and VisualTreeWatcher visual-tree COM callback must declare explicit noexcept ABI boundaries, with the latter retaining its catch-all fail-closed firewall.'

$phase2ModuleInitSingleGeneration =
    $styler.Contains(
        'std::atomic<bool> g_moduleInitializationAttempted{false};'
    ) -and
    ([regex]::Matches(
        $styler,
        '\bg_moduleInitializationAttempted\b'
    ).Count -eq 2) -and
    [regex]::IsMatch(
        $stylerInitSection,
        '(?s)BOOL\s+Wh_ModInit\(\)\s+noexcept\s+try\s*\{\s*bool\s+firstInitializationAttempt\s*=\s*false\s*;\s*if\s*\(\s*!g_moduleInitializationAttempted\.compare_exchange_strong\(\s*firstInitializationAttempt,\s*true,.*?\)\s*\)\s*\{.*?g_jarvisActivationState\.exchange\(\s*JarvisActivationState::kQuiesced,.*?RequirePermanentUnloadSafetyPin\(.*?return\s+FALSE\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerInitSection,
        '(?s)JarvisActivationState\s+expectedActivationState\s*=\s*JarvisActivationState::kBlocked\s*;\s*if\s*\(\s*!g_jarvisActivationState\.compare_exchange_strong\(\s*expectedActivationState,\s*JarvisActivationState::kAuthorized,.*?\)\s*\)\s*\{.*?g_jarvisActivationState\.exchange\(\s*JarvisActivationState::kQuiesced,.*?return\s+FALSE\s*;'
    ) -and
    -not [regex]::IsMatch(
        $styler,
        'g_jarvisActivationState\s*\.\s*(?:store|exchange)\s*\(\s*JarvisActivationState::kAuthorized\b'
    ) -and
    -not [regex]::IsMatch(
        $styler,
        'g_moduleInitializationAttempted\s*\.\s*(?:store|exchange)\s*\(\s*false\b'
    ) -and
    $stylerInitOrderValid -and
    $stylerAuthorizedToActiveCas
Add-Check `
    'phase2.module-init-single-generation-cas' `
    $phase2ModuleInitSingleGeneration `
    'A mapped M1 module must admit one initialization generation only; duplicate entry irreversibly exchanges activation to Quiesced, Blocked-to-Authorized and Authorized-to-Active are CAS transitions, and no unconditional Authorized store may revive the generation.'

$phase2GraphicsNullGuardFailures =
    [System.Collections.Generic.List[string]]::new()
foreach ($effectName in @(
    'CompositeEffect',
    'FloodEffect',
    'BorderEffect',
    'GaussianBlurEffect',
    'ColorMatrixEffect'
)) {
    $mappingSection = Get-SourceSlice `
        -Text $styler `
        -StartMarker ('HRESULT {0}::GetNamedPropertyMapping(' -f $effectName) `
        -EndMarker ('HRESULT {0}::GetPropertyCount(' -f $effectName)
    $mappingGuarded = [regex]::IsMatch(
        $mappingSection,
        '(?s)if\s*\(\s*(?:name\s*==\s*nullptr|!name)\s*\|\|\s*(?:index\s*==\s*nullptr|!index)\s*\|\|\s*(?:mapping\s*==\s*nullptr|!mapping)\s*\).*?return\s+E_INVALIDARG\s*;.*?std::wstring_view\s+nameView\(name\)'
    )
    if (-not $mappingGuarded) {
        $phase2GraphicsNullGuardFailures.Add($effectName)
    }
}
$phase2GraphicsNullGuards =
    $phase2GraphicsNullGuardFailures.Count -eq 0 -and
    $phase2Harness.Contains('module.graphics-null-input-guards')
Add-Check `
    'styler.graphics-null-input-guards' `
    $phase2GraphicsNullGuards `
    ('All five graphics COM mapping methods must reject null name/index/mapping before constructing a string view. Missing: ' +
     ($phase2GraphicsNullGuardFailures -join ', '))

$phase2PermanentPinRequireSection = Get-SourceSlice `
    -Text $stylerUnloadSafetyPinSection `
    -StartMarker 'void RequirePermanentUnloadSafetyPin(PCWSTR reason) noexcept' `
    -EndMarker 'bool IsPermanentUnloadSafetyPinRequired()'
$phase2PermanentPinReleaseSection = Get-SourceSlice `
    -Text $stylerUnloadSafetyPinSection `
    -StartMarker 'bool ReleaseUnloadSafetyModulePin() noexcept' `
    -EndMarker 'void LogUnloadSafetyPinReceipt() noexcept'
$phase2PermanentPinProofSection = Get-SourceSlice `
    -Text $stylerUnloadSafetyPinSection `
    -StartMarker 'bool HasPublishedUnloadSafetyPinUnderDecisionGate() noexcept' `
    -EndMarker 'void PublishOwnedUnloadSafetyPinUnderDecisionGate('
$phase2PermanentPinLinearization =
    $stylerUnloadSafetyPinSection.Contains(
        'std::atomic_flag g_unloadSafetyPinDecisionGate'
    ) -and
    -not $stylerUnloadSafetyPinSection.Contains(
        'g_unloadSafetyPinMutex'
    ) -and
    $stylerUnloadSafetyPinSection.Contains(
        'g_unloadSafetyPinEpoch'
    ) -and
    $stylerUnloadSafetyPinSection.Contains(
        'g_unloadSafetyPinAcquireTicket'
    ) -and
    $stylerUnloadSafetyPinSection.Contains(
        'g_unloadSafetyPinReleaseTicket'
    ) -and
    $stylerUnloadSafetyPinSection.Contains(
        'g_unloadSafetyPinReleaseOwner'
    ) -and
    $stylerUnloadSafetyPinSection.Contains(
        'g_unloadSafetyUnconfirmedReleaseOwnerEpoch'
    ) -and
    [regex]::IsMatch(
        $stylerUnloadSafetyPinSection,
        '(?s)class\s+UnloadSafetyPinDecisionGuard.*?test_and_set\(.*?~UnloadSafetyPinDecisionGuard\(\)\s+noexcept.*?clear\('
    ) -and
    [regex]::IsMatch(
        $phase2PermanentPinRequireSection,
        '(?s)g_permanentUnloadSafetyPinRequired\.exchange\(\s*true.*?acquireTicket\s*=\s*ClaimUnloadSafetyPinTicketUnderDecisionGate\(\).*?\}\s*HMODULE\s+independentlyAcquired.*?GetModuleHandleExW\('
    ) -and
    [regex]::IsMatch(
        $phase2PermanentPinRequireSection,
        '(?s)g_unloadSafetyPinAcquireTicket\s*==\s*acquireTicket\s*&&\s*g_unloadSafetyPinAcquirePurpose\s*==\s*UnloadSafetyPinAcquirePurpose::Permanent'
    ) -and
    [regex]::IsMatch(
        $phase2PermanentPinReleaseSection,
        '(?s)g_unloadSafetyModulePin\.exchange\(.*?releaseTicket\s*=\s*ClaimUnloadSafetyPinTicketUnderDecisionGate\(\).*?g_unloadSafetyPinReleaseOwner\s*=\s*module;.*?State::Releasing.*?\}\s*.*?FreeLibrary\(module\)'
    ) -and
    [regex]::IsMatch(
        $phase2PermanentPinReleaseSection,
        '(?s)g_unloadSafetyPinReleaseTicket\s*==\s*releaseTicket\s*&&\s*g_unloadSafetyPinReleaseOwner\s*==\s*module'
    ) -and
    [regex]::IsMatch(
        $phase2PermanentPinReleaseSection,
        '(?s)!loaderReleaseSucceeded.*?RetainUnconfirmedReleaseOwnerUnderDecisionGate\(\s*module,\s*releaseTicket\s*\).*?HasPublishedUnloadSafetyPinUnderDecisionGate\(\)'
    ) -and
    -not $phase2PermanentPinProofSection.Contains(
        'UnconfirmedRelease'
    ) -and
    ([regex]::Matches(
        $stylerUnloadSafetyPinSection,
        '\bGetModuleHandleExW\('
    ).Count -eq 2) -and
    ([regex]::Matches(
        $stylerUnloadSafetyPinSection,
        '\bFreeLibrary\(module\)'
    ).Count -eq 1) -and
    -not [regex]::IsMatch(
        $phase2PermanentPinRequireSection,
        '(?s)GetModuleHandleExW\(.*?g_permanentUnloadSafetyPinRequired\.(?:exchange|store)\(\s*true'
    ) -and
    -not [regex]::IsMatch(
        $styler,
        'g_permanentUnloadSafetyPinRequired\s*\.\s*store\s*\(\s*false'
    ) -and
    $phase2Harness.Contains(
        'module.permanent-pin-publication-race'
    ) -and
    $phase2Harness.Contains(
        'fake-freelibrary-reenters-require-permanent'
    ) -and
    $phase2Harness.Contains(
        'fake-getmodule-reenters-require-permanent'
    ) -and
    $phase2Harness.Contains(
        'four-reentrant-outcomes-conserved'
    ) -and
    $phase2Harness.Contains(
        'CompleteAndConserved'
    )
Add-Check `
    'styler.permanent-pin-linearized-release' `
    $phase2PermanentPinLinearization `
    'Permanent pin intent must be irreversible; exact epoch/ticket commits must bracket loader calls outside the decision gate, failed FreeLibrary owners must remain unconfirmed, and the synchronous reentry matrix must conserve references without a second free.'

$phase2GitProtocolSection = Get-SourceSlice `
    -Text $phase2Protocol `
    -StartMarker 'class GitLifecycle {' `
    -EndMarker 'struct SubscriptionReceipt {'
$phase2UiProtocolSection = Get-SourceSlice `
    -Text $phase2Protocol `
    -StartMarker 'class UiThreadRegistry {' `
    -EndMarker 'enum class DispatchState {'
$phase2DispatchProtocolSection = Get-SourceSlice `
    -Text $phase2Protocol `
    -StartMarker 'class DispatchSlot {' `
    -EndMarker '}  // namespace jarvis::resource_protocol'
$stylerUiRuntimeSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'struct UiThreadRuntimeRecord {' `
    -EndMarker 'struct UiThreadCleanupExecution {'
$stylerUiDispatcherOwnerSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'enum class UiDispatcherReleaseOutcome {' `
    -EndMarker 'struct UiCapabilityDisposition {'
$stylerUiCleanupSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'bool CleanupAllRegisteredUiThreads(' `
    -EndMarker 'void InitializeSettingsAndTap()'

$phase2SharedProtocolContract =
    $styler.Contains('#include "jarvis-resource-protocol.hpp"') -and
    $buildScript.Contains(
        "includeFileName = 'jarvis-resource-protocol.hpp'"
    ) -and
    $buildScript.Contains(
        "Join-Path `$root 'mods\common\jarvis-resource-protocol.hpp'"
    ) -and
    $phase2Protocol.Contains(
        'namespace jarvis::resource_protocol'
    ) -and
    -not [regex]::IsMatch(
        $phase2Protocol,
        '(?i)#include\s*<(?:windows|windhawk|winrt)|\b(?:SetWindowsHookEx|RevokeInterfaceFromGlobal|CoCreateInstance)\s*\('
    )
Add-Check `
    'phase2.protocol.portable-and-build-bound' `
    $phase2SharedProtocolContract `
    'The shared lifecycle protocol must stay platform-free and must be hashed as an M1 supporting source.'

$phase2GitCoreContract =
    $phase2GitProtocolSection.Contains(
        'bool admission_open_ = false;'
    ) -and
    $phase2Protocol.Contains(
        'kGitLeaseCapacity = 64'
    ) -and
    $phase2Protocol.Contains('ReserveNonZeroSequence(') -and
    $phase2Protocol.Contains('ProtocolStatus::SequenceExhausted') -and
    -not $phase2Protocol.Contains('NormalizeNonZeroSequence') -and
    -not $phase2Protocol.Contains('AdvanceNonZeroSequence') -and
    $phase2GitProtocolSection.Contains(
        'std::array<std::uint64_t, kGitLeaseCapacity>'
    ) -and
    $phase2GitProtocolSection.Contains(
        'ProtocolStatus::CapacityExceeded'
    ) -and
    [regex]::IsMatch(
        $phase2Protocol,
        '(?s)struct\s+GitReceipt\s*\{.*?GitRetainedReason\s+retained_reason.*?ProtocolStatus\s+snapshot_status.*?GitCookieKnowledge\s+cookie_knowledge.*?protocol_failure_count.*?last_failure_operation.*?sequence_exhaustions'
    ) -and
    -not $phase2GitProtocolSection.Contains('std::unordered_set') -and
    -not $phase2GitProtocolSection.Contains(
        'std::string retained_reason'
    ) -and
    -not $phase2GitProtocolSection.Contains(
        'active_leases_.insert'
    ) -and
    [regex]::IsMatch(
        $phase2GitProtocolSection,
        '(?s)GitLeaseResult\s+AcquireLease\(\s*bool allow_close_control = false\)\s+noexcept.*?admission_open_.*?CapacityExceeded.*?SequenceExhausted'
    ) -and
    [regex]::IsMatch(
        $phase2GitProtocolSection,
        '(?s)ProtocolStatus\s+CloseAdmission\(\)\s+noexcept.*?admission_open_\s*=\s*false'
    ) -and
    $phase2GitProtocolSection.Contains(
        'std::condition_variable lease_cv_;'
    ) -and
    [regex]::IsMatch(
        $phase2GitProtocolSection,
        '(?s)WaitForNoLeases\(.*?\)\s+noexcept\s*\{.*?std::unique_lock lock\(mutex_\).*?lease_cv_\.wait_for'
    ) -and
    [regex]::IsMatch(
        $phase2GitProtocolSection,
        '(?s)GitRevokeResult\s+BeginRevoke\(\)\s+noexcept.*?if\s*\(\s*admission_open_\s*\).*?InvalidState.*?revoke_attempt_sequence_exhausted_.*?revoke_ticket_sequence_exhausted_.*?SequenceExhausted.*?AdvanceSequenceLocked'
    ) -and
    [regex]::IsMatch(
        $phase2GitProtocolSection,
        '(?s)if\s*\(\s*succeeded\s*\)\s*\{.*?cookie_\s*=\s*0\s*;.*?GitState::Revoked'
    ) -and
    [regex]::IsMatch(
        $phase2GitProtocolSection,
        '(?s)CompleteRevoke\(.*?GitRetainedReason retained_reason.*?\)\s+noexcept'
    ) -and
    [regex]::IsMatch(
        $phase2GitProtocolSection,
        '(?s)RetainRegisteredResource\(.*?GitRetainedReason retained_reason.*?\)\s+noexcept'
    ) -and
    [regex]::IsMatch(
        $phase2GitProtocolSection,
        '(?s)\[\[nodiscard\]\]\s+GitReceipt\s+Receipt\(\)\s+const\s+noexcept'
    ) -and
    $phase2GitProtocolSection.Contains(
        'state_ != GitState::Registered &&'
    ) -and
    $phase2GitProtocolSection.Contains(
        'state_ != GitState::Retained'
    ) -and
    $phase2Harness.Contains(
        'git.sequence-exhaustion-no-aba'
    )
Add-Check `
    'phase2.git.authoritative-retryable-core' `
    $phase2GitCoreContract `
    'Admission close, lease drain, revoke ownership and success-only cookie clearing must share the GitLifecycle state domain.'

$phase2GitLockFunctions = @(
    [pscustomobject]@{ id = 'register'; start = 'ProtocolStatus Register('; end = '// Retained resources reject ordinary callback access.'; lock = 'std::lock_guard lock(mutex_)' },
    [pscustomobject]@{ id = 'acquire-lease'; start = 'GitLeaseResult AcquireLease('; end = '[[nodiscard]] GitCookieResult CookieForLease('; lock = 'std::lock_guard lock(mutex_)' },
    [pscustomobject]@{ id = 'cookie-for-lease'; start = '[[nodiscard]] GitCookieResult CookieForLease('; end = 'ProtocolStatus ReleaseLease('; lock = 'std::lock_guard lock(mutex_)' },
    [pscustomobject]@{ id = 'release-lease'; start = 'ProtocolStatus ReleaseLease('; end = 'ProtocolStatus CloseAdmission()'; lock = 'std::unique_lock lock(mutex_)' },
    [pscustomobject]@{ id = 'close-admission'; start = 'ProtocolStatus CloseAdmission()'; end = '[[nodiscard]] GitWaitResult WaitForNoLeases('; lock = 'std::lock_guard lock(mutex_)' },
    [pscustomobject]@{ id = 'wait-for-no-leases'; start = '[[nodiscard]] GitWaitResult WaitForNoLeases('; end = 'GitRevokeResult BeginRevoke()'; lock = 'std::unique_lock lock(mutex_)' },
    [pscustomobject]@{ id = 'begin-revoke'; start = 'GitRevokeResult BeginRevoke()'; end = '[[nodiscard]] GitCookieResult CookieForRevoke('; lock = 'std::lock_guard lock(mutex_)' },
    [pscustomobject]@{ id = 'cookie-for-revoke'; start = '[[nodiscard]] GitCookieResult CookieForRevoke('; end = 'ProtocolStatus CompleteRevoke('; lock = 'std::lock_guard lock(mutex_)' },
    [pscustomobject]@{ id = 'complete-revoke'; start = 'ProtocolStatus CompleteRevoke('; end = '// Records a fail-safe retention discovered before a revoke call can'; lock = 'std::lock_guard lock(mutex_)' },
    [pscustomobject]@{ id = 'retain-resource'; start = 'ProtocolStatus RetainRegisteredResource('; end = '[[nodiscard]] GitReceipt Receipt() const noexcept'; lock = 'std::lock_guard lock(mutex_)' },
    [pscustomobject]@{ id = 'receipt'; start = '[[nodiscard]] GitReceipt Receipt() const noexcept'; end = 'private:'; lock = 'std::lock_guard lock(mutex_)' }
)
foreach ($function in $phase2GitLockFunctions) {
    $slice = Get-SourceSlice `
        -Text $phase2GitProtocolSection `
        -StartMarker $function.start `
        -EndMarker $function.end
    $contained =
        $slice.Contains('noexcept') -and
        $slice.Contains('ShouldFailBeforeLock(operation)') -and
        $slice.Contains('try {') -and
        $slice.Contains($function.lock) -and
        $slice.Contains('catch (...)') -and
        $slice.Contains('RecordProtocolFailure(operation)')
    Add-Check `
        "phase2.git.lock-contained.$($function.id)" `
        $contained `
        "GitLifecycle $($function.id) must contain both injected and real lock failures and return a structured ProtocolFailure."
}

$phase2SubscriptionProtocolSection = Get-SourceSlice `
    -Text $phase2Protocol `
    -StartMarker 'class SubscriptionLifecycle {' `
    -EndMarker 'enum class ApartmentInitKind {'
$phase2SubscriptionLockFunctions = @(
    [pscustomobject]@{ id = 'begin-advise'; start = 'ProtocolStatus BeginAdvise()'; end = 'ProtocolStatus CompleteAdvise(' },
    [pscustomobject]@{ id = 'complete-advise'; start = 'ProtocolStatus CompleteAdvise('; end = 'ProtocolStatus BeginUnadvise()' },
    [pscustomobject]@{ id = 'begin-unadvise'; start = 'ProtocolStatus BeginUnadvise()'; end = 'ProtocolStatus CompleteUnadvise(' },
    [pscustomobject]@{ id = 'complete-unadvise'; start = 'ProtocolStatus CompleteUnadvise('; end = '[[nodiscard]] SubscriptionReceipt Receipt() const noexcept' },
    [pscustomobject]@{ id = 'receipt'; start = '[[nodiscard]] SubscriptionReceipt Receipt() const noexcept'; end = 'private:' }
)
foreach ($function in $phase2SubscriptionLockFunctions) {
    $slice = Get-SourceSlice `
        -Text $phase2SubscriptionProtocolSection `
        -StartMarker $function.start `
        -EndMarker $function.end
    $contained =
        $slice.Contains('noexcept') -and
        $slice.Contains('ShouldFailBeforeLock(operation)') -and
        $slice.Contains('try {') -and
        $slice.Contains('std::lock_guard lock(mutex_)') -and
        $slice.Contains('catch (...)') -and
        $slice.Contains('RecordProtocolFailure(operation)')
    Add-Check `
        "phase2.git.subscription-lock-contained.$($function.id)" `
        $contained `
        "SubscriptionLifecycle $($function.id) must contain lock failures without crossing a noexcept ABI boundary."
}

$phase2SubscriptionUncertaintyContract =
    $phase2Protocol.Contains(
        'bool external_uncertainty_latched = false;'
    ) -and
    $phase2SubscriptionProtocolSection.Contains(
        'std::atomic<bool> external_uncertainty_latched_{false};'
    ) -and
    [regex]::IsMatch(
        $phase2SubscriptionProtocolSection,
        '(?s)ProtocolStatus\s+BeginAdvise\(\)\s+noexcept.*?state_\s*=\s*SubscriptionState::Advising.*?external_uncertainty_latched_\.store\(\s*true'
    ) -and
    [regex]::IsMatch(
        $phase2SubscriptionProtocolSection,
        '(?s)ProtocolStatus\s+CompleteUnadvise\(.*?\)\s+noexcept.*?external_uncertainty_latched_\.store\(\s*!succeeded'
    ) -and
    [regex]::IsMatch(
        $phase2SubscriptionProtocolSection,
        '(?s)RecordProtocolFailure\(.*?CompleteAdvise.*?BeginUnadvise.*?CompleteUnadvise.*?external_uncertainty_latched_\.store\(\s*true'
    ) -and
    [regex]::IsMatch(
        $phase2SubscriptionProtocolSection,
        '(?s)SubscriptionReceipt\s+Receipt\(\)\s+const\s+noexcept.*?external_uncertainty_latched_\.load.*?CouldRequireBestEffortUnadvise\(state_\)'
    ) -and
    $phase2Harness.Contains(
        'complete-advise-legal-prestate'
    ) -and
    $phase2Harness.Contains(
        'complete-unadvise-legal-prestate'
    ) -and
    $phase2Harness.Contains(
        'receipt.external_uncertainty_latched'
    )
Add-Check `
    'phase2.git.subscription-external-uncertainty-latched' `
    $phase2SubscriptionUncertaintyContract `
    'Subscription receipts must remain conservative when external Advise or Unadvise work cannot be committed after a lock failure.'

$stylerGetGitSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'HRESULT VisualTreeWatcher::GetVisualTreeServiceForCurrentApartment(' `
    -EndMarker 'bool VisualTreeWatcher::WaitForVisualTreeServiceLeases('
$stylerRevokeGitSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'HRESULT VisualTreeWatcher::RevokeVisualTreeServiceFromCurrentApartment()' `
    -EndMarker 'VisualTreeWatcher::VisualTreeWatcher('
$phase2GitExternalComFirewall =
    [regex]::IsMatch(
        $styler,
        '(?s)HRESULT\s+CreateGlobalInterfaceTableNoThrow\(.*?\)\s+noexcept.*?IGlobalInterfaceTable\*\s+rawGit\s*=\s*nullptr.*?try\s*\{.*?CoCreateInstance\(.*?\}\s*catch\s*\(\s*\.\.\.\s*\).*?RetainUnknownExternalComOutcome\(uncertaintyReason\).*?if\s*\(\s*FAILED\(hr\)\s*\).*?if\s*\(\s*rawGit\s*\).*?RetainUnknownExternalComOutcome\(uncertaintyReason\).*?owner\.AdoptConfirmed\(rawGit\)'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)HRESULT\s+GetInterfaceFromGlobalNoThrow\(.*?\)\s+noexcept.*?void\*\s+rawResult\s*=\s*nullptr.*?try\s*\{.*?git->GetInterfaceFromGlobal\(.*?&rawResult\).*?\}\s*catch\s*\(\s*\.\.\.\s*\).*?RetainUnknownExternalComOutcome\(uncertaintyReason\).*?if\s*\(\s*FAILED\(hr\)\s*\).*?if\s*\(\s*rawResult\s*\).*?RetainUnknownExternalComOutcome\(uncertaintyReason\)'
    ) -and
    [regex]::IsMatch(
        $stylerGetGitSection,
        '(?s)NoThrowGlobalInterfaceTableOwner\s+git\(.*?CreateGlobalInterfaceTableNoThrow\(.*?bool\s+serviceOutcomeUnknown\s*=\s*false.*?GetInterfaceFromGlobalNoThrow\(.*?service\.AdoptServiceConfirmed\(.*?if\s*\(\s*serviceOutcomeUnknown\s*\)\s*\{\s*service\.RetainUnconfirmedAcquisition\(.*?git\.Reset\(\).*?if\s*\(\s*!serviceOutcomeUnknown\s*&&.*?service\.Reset\(\)'
    ) -and
    [regex]::IsMatch(
        $stylerRevokeGitSection,
        '(?s)BeginRevoke\(\).*?NoThrowGlobalInterfaceTableOwner\s+git\(.*?CreateGlobalInterfaceTableNoThrow\(.*?CompleteRevoke\(\s*revoke\.ticket,\s*false.*?try\s*\{.*?RevokeInterfaceFromGlobal\(.*?\}\s*catch\s*\(\s*\.\.\.\s*\).*?revokeThrew\s*=\s*true.*?CompleteRevoke\(\s*revoke\.ticket,\s*SUCCEEDED\(hr\)\s*&&\s*!revokeThrew.*?git\.Reset\(\)'
    ) -and
    -not $stylerGetGitSection.Contains(
        'winrt::com_ptr<IGlobalInterfaceTable> git'
    ) -and
    -not $stylerGetGitSection.Contains(
        'service.Service().put()'
    ) -and
    -not $stylerRevokeGitSection.Contains(
        'winrt::com_ptr<IGlobalInterfaceTable> git'
    ) -and
    $phase2Harness.Contains(
        'git.get-external-com-exception-retains-lease'
    ) -and
    $phase2Harness.Contains(
        'git.cocreate-output-throw-retained'
    ) -and
    $phase2Harness.Contains(
        'git.revoke-external-com-exception-retained'
    )
Add-Check `
    'phase2.git.external-com-noexcept-firewall' `
    $phase2GitExternalComFirewall `
    'CoCreate, Get and revoke adapters must withhold every unconfirmed output, retain a proxy lease after unknown acquisition, and preserve the exact revoke ticket.'

$phase2GitAdapterContract =
    $stylerWatcherClassSection.Contains(
        'GitLifecycle m_visualTreeServiceGit;'
    ) -and
    -not $stylerWatcherClassSection.Contains(
        'std::atomic<DWORD> m_visualTreeServiceGitCookie'
    ) -and
    [regex]::IsMatch(
        $stylerWatcherClassSection,
        '(?s)void\s+Reset\(\)\s+noexcept\s*\{.*?m_service\.detach\(\).*?proxy->Release\(\).*?catch\s*\(\s*\.\.\.\s*\).*?EmergencyRetainAndFailClosed.*?m_lifecycle\s*=\s*nullptr.*?return\s*;.*?ReleaseLease\(m_ticket\)'
    ) -and
    $styler.Contains('ProvisionalGitCookieGuard') -and
    $styler.Contains('ReserveProvisionalGitSlot') -and
    $styler.Contains('ProvisionalGitSlotState::Reserved') -and
    $styler.Contains(
        'ProvisionalGitSlotState::UnknownMayBePresent'
    ) -and
    $styler.Contains('ProvisionalGitSlotState::Retained') -and
    $styler.Contains('std::array<ProvisionalGitQuarantineEntry, 64>') -and
    [regex]::IsMatch(
        $stylerWatcherConstructorSection,
        '(?s)ReserveNonZeroSequence\(.*?ReserveProvisionalGitSlot\(\).*?ProvisionalGitCookieGuard\s+provisionalGitCookie\(.*?RegisterInterfaceInGlobal\(.*?PublishCookie\(gitCookie\).*?m_visualTreeServiceGit\.Register\(.*?provisionalGitCookie\.Commit\(\)'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)RevokeVisualTreeServiceFromCurrentApartment\(\).*?CloseAdmission\(\).*?WaitForVisualTreeServiceLeases\(\s*5000\s*\).*?BeginRevoke\(\).*?RevokeInterfaceFromGlobal\(.*?CompleteRevoke\('
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)GetVisualTreeServiceForCurrentApartment\(.*?AcquireLease\(allowDuringClose\).*?ProtocolStatus::\s*CapacityExceeded.*?RetainVisualTreeServiceGit\(.*?GitRetainedReason::LeaseCapacityExceeded.*?LatchJarvisActivationQuiesced\('
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)RetainVisualTreeServiceGit\(HRESULT error,\s*GitRetainedReason reason\)\s+noexcept.*?RetainRegisteredResource\(.*?retainStatus\s*!='
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)EmergencyRetainAndFailClosed\(.*?\)\s+noexcept.*?AddRef\(\).*?m_emergencyProtocolFailureOwnerEstablished.*?RequirePermanentUnloadSafetyPin\(.*?FailClosedForeignAbiException\('
    ) -and
    $styler.Contains(
        'SubscriptionState::MaybeAdvised'
    ) -and
    $phase2GitExternalComFirewall
Add-Check `
    'phase2.git.production-adapter' `
    $phase2GitAdapterContract `
    'The production adapter must retain provisional cookies, release proxies before leases, and serialize best-effort Unadvise before retryable revoke.'

$phase2GitNoAllocationContract =
    $phase2Protocol.Contains(
        'enum class GitRetainedReason'
    ) -and
    $phase2Protocol.Contains(
        'constexpr std::string_view ToString(GitRetainedReason reason) noexcept'
    ) -and
    $phase2Harness.Contains(
        'std::is_trivially_copyable_v<protocol::GitReceipt>'
    ) -and
    [regex]::Matches(
        $phase2Harness,
        'static_assert\(noexcept\('
    ).Count -ge 11 -and
    $phase2Harness.Contains(
        'git.lease-capacity-retained'
    ) -and
    $phase2Harness.Contains(
        'git.fixed-reason-receipt-noalloc'
    ) -and
    $phase2Harness.Contains(
        'git.public-lock-failure-matrix'
    ) -and
    $phase2Harness.Contains(
        'git.unknown-cookie-fallback-receipt'
    ) -and
    $phase2Harness.Contains(
        'git.subscription-lock-failure-matrix'
    )
Add-Check `
    'phase2.git.fixed-capacity-noexcept-receipt' `
    $phase2GitNoAllocationContract `
    'GIT lease ownership and retained receipts must use fixed storage and enum reasons, and every public core operation must be noexcept.'

$phase2GitRetiredOwnerContract =
    $stylerWatcherGlobalsSection.Contains(
        'kRetiredVisualTreeWatcherCapacity = 64;'
    ) -and
    $stylerWatcherGlobalsSection.Contains(
        'std::array<winrt::com_ptr<VisualTreeWatcher>,'
    ) -and
    $stylerWatcherGlobalsSection.Contains(
        'RetiredVisualTreeWatcherOwnershipLedger'
    ) -and
    $stylerWatcherGlobalsSection.Contains(
        'RetiredVisualTreeWatcherOwnerGuard'
    ) -and
    [regex]::IsMatch(
        $stylerWatcherGlobalsSection,
        '(?s)PreserveRetiredVisualTreeWatcher\(\s*VisualTreeWatcher\*\s+watcher\)\s+noexcept.*?watcher->AddRef\(\).*?FailClosedForeignAbiException\(.*?std::lock_guard<std::mutex> lock\(g_visualTreeWatcherMutex\).*?slot->attach\(watcher\).*?processLifetimeRetained'
    ) -and
    [regex]::IsMatch(
        $stylerWatcherGlobalsSection,
        '(?s)~RetiredVisualTreeWatcherOwnerGuard\(\)\s+noexcept.*?PreserveRetiredVisualTreeWatcher\(m_watcher\).*?m_watcher\s*=\s*nullptr'
    ) -and
    -not $stylerWatcherGlobalsSection.Contains(
        'g_retiredVisualTreeWatchers.push_back'
    ) -and
    [regex]::Matches(
        $stylerSetSiteSection,
        'RetiredVisualTreeWatcherOwnerGuard'
    ).Count -ge 2 -and
    $stylerUninitSection.Contains(
        'LogRetiredVisualTreeWatcherOwnershipReceipts()'
    ) -and
    $phase2Harness.Contains(
        'git.retired-owner-transfer-failure'
    ) -and
    $phase2Harness.Contains(
        'FakeFixedRetiredOwnerAdapter'
    ) -and
    $phase2Harness.Contains(
        'FakeRetiredOwnerGuard'
    ) -and
    $phase2Harness.Contains(
        'owner-before-failclosed-before-publication'
    )
Add-Check `
    'phase2.git.retired-owner-noalloc-transfer' `
    $phase2GitRetiredOwnerContract `
    'Nonterminal watcher ownership must transfer through fixed no-allocation slots or an intrusive process-lifetime owner before the caller can release.'

$phase2GitProvisionalRetryContract =
    $styler.Contains('enum class ProvisionalGitSlotState') -and
    $styler.Contains(
        'std::atomic<ProvisionalGitSlotState> state'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)ReserveProvisionalGitSlot\(\)\s+noexcept.*?compare_exchange_strong\(.*?ProvisionalGitSlotState::Reserved.*?g_provisionalGitCapacityFailures'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)~ProvisionalGitCookieGuard\(\)\s+noexcept.*?RevokeInterfaceFromGlobal\(cookie\).*?catch\s*\(\s*\.\.\.\s*\).*?ProvisionalGitSlotState::Retained'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)~ProvisionalGitCookieGuard\(\)\s+noexcept.*?ProvisionalGitSlotState::UnknownMayBePresent.*?cookie-known=0.*?return\s*;.*?const DWORD cookie'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)bool\s+MarkRegistrationUnknown\(HRESULT error\)\s+noexcept.*?cookie\.store\(0.*?lastError\.store\(error.*?ProvisionalGitSlotState::UnknownMayBePresent'
    ) -and
    [regex]::IsMatch(
        $stylerWatcherConstructorSection,
        '(?s)RegisterInterfaceInGlobal\(.*?catch\s*\(\s*\.\.\.\s*\).*?if\s*\(\s*gitCookie\s*\).*?PublishCookie\(gitCookie\).*?else\s*\{.*?MarkRegistrationUnknown\(.*?winrt::to_hresult\(\).*?if\s*\(\s*FAILED\(registerHr\)\s*&&\s*!gitCookie\s*\).*?MarkRegistrationUnknown\(registerHr\).*?if\s*\(\s*!gitCookie\s*\).*?MarkRegistrationUnknown\(E_UNEXPECTED\)'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)bool\s+RetryProvisionalGitQuarantineFromInitializedApartment\(\)\s+noexcept.*?try\s*\{.*?CoInitializeEx\(nullptr,\s*COINIT_MULTITHREADED\).*?catch\s*\(\s*\.\.\.\s*\).*?RPC_E_CHANGED_MODE.*?try\s*\{\s*allRevoked\s*=\s*RetryProvisionalGitQuarantine\(\).*?catch\s*\(\s*\.\.\.\s*\).*?if\s*\(\s*balanceInitialization\s*\)\s*\{\s*try\s*\{\s*CoUninitialize\(\).*?catch\s*\(\s*\.\.\.\s*\).*?balanceConfirmed\s*=\s*false.*?return\s+allRevoked\s*&&\s*balanceConfirmed'
    ) -and
    [regex]::IsMatch(
        $stylerUninitSection,
        '(?s)UninitializeSettingsAndTap\(\).*?RetryProvisionalGitQuarantineFromInitializedApartment\(\).*?LogProvisionalGitQuarantineReceipts\(\)'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)bool\s+RetryProvisionalGitQuarantine\(\)\s+noexcept.*?compare_exchange_strong\(.*?ProvisionalGitSlotState::Retrying.*?NoThrowGlobalInterfaceTableOwner\s+git\(.*?CreateGlobalInterfaceTableNoThrow\(.*?createOutcomeUnknown.*?try\s*\{.*?RevokeInterfaceFromGlobal.*?catch\s*\(\s*\.\.\.\s*\).*?git\.Reset\(\).*?createOutcomeUnknown\s*\|\|\s*!gitReleaseConfirmed'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)void\s+VisualTreeWatcher::RetainVisualTreeServiceGit\(.*?\)\s+noexcept\s*\{\s*const auto retainStatus\s*=\s*m_visualTreeServiceGit\.RetainRegisteredResource\(.*?retainStatus\s*!='
    ) -and
    $phase2Harness.Contains(
        'git.provisional-initialized-unload-retry'
    ) -and
    $phase2Harness.Contains(
        'RetryProvisionalQuarantineFromInitializedApartment'
    ) -and
    $phase2Harness.Contains(
        'git.provisional-quarantine-overflow-receipt'
    ) -and
    $phase2Harness.Contains(
        'git.provisional-rollback-exception-retained'
    ) -and
    $phase2Harness.Contains(
        'git.provisional-register-throw-unknown-retained'
    ) -and
    $phase2Harness.Contains(
        'git.provisional-git-release-exception-contained'
    )
Add-Check `
    'phase2.git.initialized-provisional-retry-and-atomic-retain' `
    $phase2GitProvisionalRetryContract `
    'A fixed atomic slot must be reserved before external registration; rollback failures retain the exact cookie, and the one bounded retry performs COM without an internal mutex.'

$phase2GitRawOwnerFirewall =
    -not $styler.Contains(
        'winrt::com_ptr<IGlobalInterfaceTable>'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)class\s+NoThrowGlobalInterfaceTableOwner.*?~NoThrowGlobalInterfaceTableOwner\(\)\s+noexcept.*?bool\s+Reset\(\)\s+noexcept.*?std::exchange\(m_git,\s*nullptr\).*?try\s*\{.*?rawGit->Release\(\).*?\}\s*catch\s*\(\s*\.\.\.\s*\).*?g_externalGitReferenceReleaseFailures.*?LatchJarvisActivationQuiesced\(.*?RequirePermanentUnloadSafetyPin\('
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)class\s+ProvisionalGitCookieGuard.*?ProvisionalGitReservation reservation,\s*IGlobalInterfaceTable\* git\).*?IGlobalInterfaceTable\*\s+m_git\s*=\s*nullptr'
    ) -and
    [regex]::IsMatch(
        $stylerWatcherConstructorSection,
        '(?s)NoThrowGlobalInterfaceTableOwner\s+git\(.*?ProvisionalGitCookieGuard\s+provisionalGitCookie\(\s*provisionalReservation,\s*git\.get\(\)\).*?provisionalGitCookie\.Commit\(\).*?git\.Reset\(\).*?EmergencyRetainAndFailClosed\('
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)bool\s+RetryProvisionalGitQuarantine\(\)\s+noexcept.*?NoThrowGlobalInterfaceTableOwner\s+git\(.*?CreateGlobalInterfaceTableNoThrow\(.*?git\.Reset\(\).*?if\s*\(\s*createOutcomeUnknown\s*\|\|\s*!gitReleaseConfirmed\s*\)\s*\{\s*allRevoked\s*=\s*false'
    ) -and
    $phase2Harness.Contains(
        'git.provisional-git-release-exception-contained'
    )
Add-Check `
    'phase2.git.no-implicit-git-com-release' `
    $phase2GitRawOwnerFirewall `
    'Every temporary GIT interface owner must detach and explicitly contain final Release; implicit com_ptr AddRef or Release is forbidden.'

$phase2TapSiteExternalComFirewall =
    [regex]::IsMatch(
        $styler,
        '(?s)template\s*<typename Interface>\s*class\s+NoThrowExternalComReferenceOwner.*?~NoThrowExternalComReferenceOwner\(\)\s+noexcept.*?bool\s+Reset\(\)\s+noexcept.*?std::exchange\(m_value,\s*nullptr\).*?try\s*\{.*?rawValue->Release\(\).*?catch\s*\(\s*\.\.\.\s*\).*?RetainUnknownExternalComOutcome\('
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)HRESULT\s+QueryExternalComInterfaceNoThrow\(.*?\)\s+noexcept.*?void\*\s+queried\s*=\s*nullptr.*?try\s*\{.*?source->QueryInterface\(.*?&queried\).*?catch\s*\(\s*\.\.\.\s*\).*?RetainUnknownExternalComOutcome\(uncertaintyReason\).*?if\s*\(\s*FAILED\(hr\)\s*\).*?if\s*\(\s*queried\s*\).*?RetainUnknownExternalComOutcome\(uncertaintyReason\)'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)struct\s+SiteHolder.*?CopyFromExternal\(IUnknown\*\s+source\)\s+noexcept.*?try\s*\{\s*source->AddRef\(\).*?catch\s*\(\s*\.\.\.\s*\).*?RetainUnknownExternalComOutcome\(.*?value\.AdoptConfirmed\(source\).*?QueryInterfaceNoThrow\(.*?\)\s+const\s+noexcept.*?QueryExternalComInterfaceNoThrow\(.*?NoThrowExternalComReferenceOwner<IUnknown>\s+value'
    ) -and
    [regex]::IsMatch(
        $stylerSetSiteSection,
        '(?s)std::make_shared<SiteHolder>\(\).*?CopyFromExternal\(pUnkSite\).*?auto\s+watcherSite\s*=\s*newSite.*?oldSite\.reset\(\).*?winrt::make_self<VisualTreeWatcher>\(watcherSite->get\(\)\)'
    ) -and
    [regex]::IsMatch(
        $stylerGetSiteSection,
        '(?s)GetSite\(.*?\)\s+noexcept.*?std::shared_ptr<SiteHolder>\s+currentSite.*?currentSite->QueryInterfaceNoThrow\(riid,\s*ppvSite\)'
    ) -and
    [regex]::IsMatch(
        $stylerWatcherConstructorSection,
        '(?s)VisualTreeWatcher::VisualTreeWatcher\(IUnknown\*\s+site\).*?QueryExternalComInterfaceNoThrow\(\s*site,\s*__uuidof\(IXamlDiagnostics\).*?NoThrowExternalComReferenceOwner<IXamlDiagnostics>.*?QueryExternalComInterfaceNoThrow\(\s*xamlDiagnostics\.get\(\),\s*__uuidof\(IVisualTreeService3\).*?NoThrowExternalComReferenceOwner<IVisualTreeService3>'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)wf::IInspectable\s+VisualTreeWatcher::FromHandle\(.*?\).*?QueryExternalComInterfaceNoThrow\(.*?__uuidof\(IXamlDiagnostics\).*?GetIInspectableFromHandle\(.*?catch\s*\(\s*\.\.\.\s*\).*?RetainUnknownExternalComOutcome\(.*?NoThrowExternalComReferenceOwner<::IInspectable>.*?take_ownership_from_abi'
    ) -and
    -not $styler.Contains('winrt::com_ptr<IUnknown>') -and
    -not $styler.Contains('winrt::com_ptr<IXamlDiagnostics>') -and
    -not $styler.Contains('winrt::com_ptr<IGlobalInterfaceTable>') -and
    -not $styler.Contains('.as<IXamlDiagnostics>') -and
    $phase2Harness.Contains(
        'git.site-holder-external-com-firewall'
    ) -and
    $phase2Harness.Contains(
        'FakeSiteHolderExternalComFirewall'
    )
Add-Check `
    'phase2.git.tap-site-external-com-firewall' `
    $phase2TapSiteExternalComFirewall `
    'TAP site, diagnostics and handle-resolution references must use explicit external COM acquisition and no-throw release firewalls without blind compensation.'

$phase2ComPtrTypes = @(
    [regex]::Matches(
        $styler,
        'winrt::com_ptr<\s*([^>]+?)\s*>'
    ) | ForEach-Object {
        $_.Groups[1].Value.Trim()
    } | Sort-Object -Unique
)
$phase2InternalSelfReferenceNoexcept =
    [regex]::IsMatch(
        $stylerWatcherClassSection,
        '(?s)class\s+VisualTreeWatcher\s*:\s*public\s+winrt::implements<VisualTreeWatcher,\s*IVisualTreeServiceCallback2>.*?~VisualTreeWatcher\(\)\s+noexcept.*?static_assert\(\s*noexcept\(std::declval<VisualTreeWatcher&>\(\)\.AddRef\(\)\)\s*\).*?static_assert\(\s*noexcept\(std::declval<VisualTreeWatcher&>\(\)\.Release\(\)\)\s*\).*?static_assert\(\s*std::is_nothrow_destructible_v<VisualTreeWatcher>\s*\)'
    ) -and
    [regex]::IsMatch(
        $stylerWatcherClassSection,
        '(?s)class\s+WorkerReferenceGuard.*?~WorkerReferenceGuard\(\)\s+noexcept.*?m_watcher->Release\(\)'
    ) -and
    [regex]::IsMatch(
        $stylerWatcherConstructorSection,
        '(?s)AddRef\(\)\s*;\s*m_adviseThread\s*=\s*CreateThread\(.*?WorkerReferenceGuard\s+workerReference\(watcher\).*?if\s*\(\s*!m_adviseThread\s*\).*?Release\(\)'
    ) -and
    [regex]::IsMatch(
        $stylerWatcherUnadviseSection,
        '(?s)AddRef\(\)\s*;\s*m_unadviseThread\s*=\s*CreateThread\(.*?WorkerReferenceGuard\s+workerReference\(watcher\).*?if\s*\(\s*!m_unadviseThread\s*\).*?Release\(\)'
    ) -and
    ($phase2ComPtrTypes -join ',') -eq
        'IVisualTreeService3,VisualTreeWatcher' -and
    $phase2Harness.Contains(
        'git.internal-self-reference-noexcept'
    ) -and
    $phase2Harness.Contains(
        'noexcept(std::declval<FakeInternalSelfReferenceOps&>().AddRef())'
    ) -and
    $phase2Harness.Contains(
        'noexcept(std::declval<FakeInternalSelfReferenceOps&>().Release())'
    )
Add-Check `
    'phase2.git.internal-self-reference-noexcept-boundary' `
    $phase2InternalSelfReferenceNoexcept `
    'Only the exact internal non-composable VisualTreeWatcher may use projected self owners, and its AddRef, Release, destructor, worker publication and rollback paths must be compile-time noexcept.'

$stylerPreserveRetiredSection = Get-SourceSlice `
    -Text $stylerWatcherGlobalsSection `
    -StartMarker 'RetiredVisualTreeWatcherTransfer PreserveRetiredVisualTreeWatcher(' `
    -EndMarker 'class RetiredVisualTreeWatcherOwnerGuard {'
$stylerLogRetiredSection = Get-SourceSlice `
    -Text $stylerWatcherGlobalsSection `
    -StartMarker 'void LogRetiredVisualTreeWatcherOwnershipReceipts() noexcept' `
    -EndMarker 'class TapTransitionScope {'
$phase2GitNoLockedLogging =
    -not $styler.Contains('g_provisionalGitQuarantineMutex') -and
    [regex]::IsMatch(
        $stylerPreserveRetiredSection,
        '(?s)std::lock_guard<std::mutex> lock\(g_visualTreeWatcherMutex\).*?\}\s*catch\s*\(\s*\.\.\.\s*\).*?try\s*\{.*?RequirePermanentUnloadSafetyPin\(.*?\}\s*catch\s*\(\s*\.\.\.\s*\).*?try\s*\{.*?Wh_Log\(.*?\}\s*catch\s*\(\s*\.\.\.\s*\).*?RecordRetiredVisualTreeWatcherDiagnosticFailure'
    ) -and
    [regex]::IsMatch(
        $stylerLogRetiredSection,
        '(?s)std::lock_guard<std::mutex> lock\(g_visualTreeWatcherMutex\).*?\}\s*catch\s*\(\s*\.\.\.\s*\).*?try\s*\{.*?Wh_Log\(.*?\}\s*catch\s*\(\s*\.\.\.\s*\).*?RecordRetiredVisualTreeWatcherDiagnosticFailure'
    ) -and
    [regex]::IsMatch(
        $styler,
        '(?s)~ProvisionalGitCookieGuard\(\)\s+noexcept.*?ProvisionalGitSlotState::Retained.*?try\s*\{.*?RequirePermanentUnloadSafetyPin\(.*?\}\s*catch\s*\(\s*\.\.\.\s*\).*?try\s*\{.*?Wh_Log\(.*?\}\s*catch\s*\(\s*\.\.\.\s*\).*?RecordProvisionalGitDiagnosticFailure'
    )
Add-Check `
    'phase2.git.no-logging-or-com-under-bookkeeping-lock' `
    $phase2GitNoLockedLogging `
    'Provisional GIT must be lock-free, and every destructor-reachable diagnostic must be outside its bookkeeping lock and contained by its own exception firewall.'

$phase2FailClosedBeforePin =
    [regex]::IsMatch(
        $stylerNoThrowGitOwnerSection,
        '(?s)catch\s*\(\s*\.\.\.\s*\).*?LatchJarvisActivationQuiesced\(.*?RequirePermanentUnloadSafetyPin\('
    ) -and
    [regex]::IsMatch(
        $stylerRetainUnknownComSection,
        '(?s)LatchJarvisActivationQuiesced\(reason\).*?RequirePermanentUnloadSafetyPin\(reason\)'
    ) -and
    [regex]::Matches(
        $stylerProvisionalGuardSection,
        '(?s)LatchJarvisActivationQuiesced\(.*?RequirePermanentUnloadSafetyPin\('
    ).Count -ge 2 -and
    [regex]::IsMatch(
        $stylerRetryProvisionalSection,
        '(?s)COM initialization is unknown.*?RequirePermanentUnloadSafetyPin\(.*?COM initialization threw'
    ) -and
    [regex]::IsMatch(
        $stylerRetryProvisionalSection,
        '(?s)if\s*\(\s*!apartmentReady\s*\).*?LatchJarvisActivationQuiesced\(.*?COM initialization.*?failed.*?RequirePermanentUnloadSafetyPin\(.*?COM initialization.*?failed'
    ) -and
    [regex]::IsMatch(
        $stylerRetryProvisionalSection,
        '(?s)RetryProvisionalGitQuarantine\(\).*?catch\s*\(\s*\.\.\.\s*\).*?LatchJarvisActivationQuiesced\(.*?retry boundary threw.*?RequirePermanentUnloadSafetyPin\(.*?retry boundary threw'
    ) -and
    [regex]::IsMatch(
        $stylerRetryProvisionalSection,
        '(?s)CoUninitialize\(\).*?catch\s*\(\s*\.\.\.\s*\).*?LatchJarvisActivationQuiesced\(.*?COM balance is unknown.*?RequirePermanentUnloadSafetyPin\(.*?CoUninitialize threw'
    ) -and
    [regex]::IsMatch(
        $stylerRetryProvisionalSection,
        '(?s)if\s*\(\s*!allRevoked\s*\|\|\s*!balanceConfirmed\s*\).*?LatchJarvisActivationQuiesced\(.*?retained resources.*?RequirePermanentUnloadSafetyPin\(.*?retained resources'
    ) -and
    [regex]::IsMatch(
        $stylerEmergencyWatcherSection,
        '(?s)FailClosedForeignAbiException\(.*?RequirePermanentUnloadSafetyPin\('
    ) -and
    [regex]::Matches(
        $stylerPreserveRetiredSection,
        '(?s)FailClosedForeignAbiException\(.*?RequirePermanentUnloadSafetyPin\('
    ).Count -ge 2 -and
    [regex]::IsMatch(
        $stylerPreserveRetiredSection,
        '(?s)LatchJarvisActivationQuiesced\(.*?RequirePermanentUnloadSafetyPin\('
    ) -and
    [regex]::IsMatch(
        $stylerRetainGitSection,
        '(?s)LatchJarvisActivationQuiesced\(.*?RequirePermanentUnloadSafetyPin\('
    ) -and
    [regex]::IsMatch(
        $stylerAcquirePinSection,
        '(?s)catch\s*\(\s*\.\.\.\s*\).*?LatchJarvisActivationQuiesced\(.*?RequirePermanentUnloadSafetyPin\('
    ) -and
    $phase2Harness.Contains('module.failclosed-before-pin-decision') -and
    $phase2Harness.Contains('hold-pin-decision-gate') -and
    $phase2Harness.Contains('observe-quiesced-while-pin-blocked')
Add-Check `
    'phase2.failclosed-before-pin-decision' `
    $phase2FailClosedBeforePin `
    'Every audited hostile-COM, retained-owner and loader failure must publish Quiesced before entering the permanent-pin decision gate; the deterministic contention scenario must observe that order.'

$phase2UiRecordContract =
    $stylerUiRuntimeSection.Contains(
        'std::uint64_t recordId = 0;'
    ) -and
    $stylerUiRuntimeSection.Contains(
        'std::uint64_t activationGeneration'
    ) -and
    $stylerUiRuntimeSection.Contains('DWORD threadId = 0;') -and
    $stylerUiRuntimeSection.Contains(
        'std::uint64_t threadCreationTime = 0;'
    ) -and
    $stylerUiRuntimeSection.Contains('HANDLE threadHandle = nullptr;') -and
    $stylerUiRuntimeSection.Contains(
        'HANDLE cleanupCompletedEvent = nullptr;'
    ) -and
    $stylerUiRuntimeSection.Contains(
        'UiAgileDispatcherOwner dispatcherOwner;'
    ) -and
    $stylerUiDispatcherOwnerSection.Contains(
        'mutable std::atomic_flag decisionGate_ = ATOMIC_FLAG_INIT;'
    ) -and
    $stylerUiDispatcherOwnerSection.Contains(
        'UiDispatcherOwnerState::Releasing'
    ) -and
    $stylerUiDispatcherOwnerSection.Contains(
        'std::uint64_t activeReleaseTicket_ = 0;'
    ) -and
    $stylerUiDispatcherOwnerSection.Contains(
        'PublishUiDispatcherUnknownOwnerReceipt('
    ) -and
    [regex]::IsMatch(
        $stylerUiDispatcherOwnerSection,
        '(?s)state_\s*=\s*UiDispatcherOwnerState::Releasing;.*?\}\s*if\s*\(\s*ticketExhausted\s*\).*?try\s*\{.*?releaseOwner->Release\(\);'
    ) -and
    [regex]::IsMatch(
        $stylerUiDispatcherOwnerSection,
        '(?s)case\s+UiDispatcherOwnerState::Releasing:\s*return\s+UiDispatcherReleaseOutcome::DeferredBusy;'
    ) -and
    [regex]::IsMatch(
        $stylerUiDispatcherOwnerSection,
        '(?s)catch\s*\(\s*\.\.\.\s*\).*?PublishUiDispatcherUnknownOwnerReceipt\(.*?UiDispatcherUnknownReason::ReleaseThrew.*?UnknownRetained.*?FailClosedForeignAbiException\('
    ) -and
    $stylerUiDispatcherOwnerSection.Contains(
        'UiBorrowedDispatcherProjection'
    ) -and
    $styler.Contains(
        'g_uiProjectedDispatcherOwnerCount'
    ) -and
    $styler.Contains(
        'g_uiCleanupEventOwnerCount'
    ) -and
    $stylerUiRuntimeSection.Contains(
        'UiWindowRoleLifecycle windowRoles'
    ) -and
    $phase2Protocol.Contains('class UiWindowRoleLifecycle') -and
    $phase2Protocol.Contains('std::uint64_t active = 0;') -and
    $phase2Protocol.Contains('std::uint64_t created = 0;') -and
    $phase2Protocol.Contains('std::uint64_t destroyed = 0;') -and
    $phase2Protocol.Contains('std::uint64_t replacements = 0;') -and
    $phase2Protocol.Contains(
        'std::uint64_t failed_destroy_attempts = 0;'
    ) -and
    -not [regex]::IsMatch($stylerUiRuntimeSection, '\bHWND\b') -and
    [regex]::IsMatch(
        $styler,
        '(?s)DuplicateHandle\(.*?SYNCHRONIZE\s*\|\s*THREAD_QUERY_LIMITED_INFORMATION'
    ) -and
    $phase2Protocol.Contains(
        'The registry intentionally contains no HWND.'
    ) -and
    $phase2Protocol.Contains(
        'UiCapability::CleanupEvent'
    ) -and
    [regex]::IsMatch(
        $phase2Protocol,
        '(?s)CreatedMask\(\)\s+const\s+noexcept.*?has_thread_handle.*?UiCapability::ThreadHandle.*?has_agile_dispatcher.*?UiCapability::AgileDispatcher.*?has_cleanup_event.*?UiCapability::CleanupEvent'
    )
Add-Check `
    'phase2.ui-thread.capability-record-no-hwnd' `
    $phase2UiRecordContract `
    'Each HWND-free UI record must bind independent thread-handle, raw agile-dispatcher and cleanup-event owners; one short decision gate must linearize borrower admission and exact-ticket COM Release outside that gate, with thrown Release retained as an opaque fixed receipt.'

$phase2UiInitializationContract =
    $stylerInitializeForThreadSection.Contains(
        'g_uiThreadLifecycle.Reserve('
    ) -and
    $stylerInitializeForThreadSection.IndexOf(
        'g_uiThreadLifecycle.Reserve(',
        [StringComparison]::Ordinal
    ) -lt
        $stylerInitializeForThreadSection.IndexOf(
            'ProcessAllStylesFromSettings();',
            [StringComparison]::Ordinal
        ) -and
    $stylerInitializeForThreadSection.IndexOf(
        'ProcessAllStylesFromSettings();',
        [StringComparison]::Ordinal
    ) -lt
        $stylerInitializeForThreadSection.LastIndexOf(
            'g_uiThreadLifecycle.CompleteInitialization(',
            [StringComparison]::Ordinal
        ) -and
    $stylerInitializeForThreadSection.Contains(
        'RunUiThreadCleanupSteps(record)'
    ) -and
    [regex]::IsMatch(
        $phase2Protocol,
        '(?s)class\s+UiWindowRoleLifecycle.*?ObserveCreated\(UiWindowRole role\).*?\+\+counts->created.*?\+\+counts->active.*?counts->destroyed\s*>\s*counts->replacements.*?\+\+counts->replacements'
    ) -and
    [regex]::IsMatch(
        $phase2Protocol,
        '(?s)CompleteDestroy\(UiWindowRole role,\s*bool succeeded\).*?if\s*\(\s*!succeeded\s*\).*?\+\+counts->failed_destroy_attempts.*?return ProtocolStatus::Applied.*?\+\+counts->destroyed.*?--counts->active'
    ) -and
    [regex]::IsMatch(
        $stylerDestroyWindowSection,
        '(?s)bool\s+originalAttempted\s*=\s*false\s*;.*?bool\s+originalCompleted\s*=\s*false\s*;.*?if\s*\(\s*!lifecycleScope\s*\).*?DestroyWindow_Original\(hWnd\).*?auto role\s*=\s*ClassifyJarvisBootstrapWindow\(.*?DestroyWindow_Original\(hWnd\).*?destroyError\s*=\s*GetLastError\(\).*?CompleteUiThreadWindowDestroy\(\s*\*role,\s*destroyed\s*!=\s*FALSE\s*\).*?if\s*\(\s*originalCompleted\s*\).*?if\s*\(\s*originalAttempted\s*\).*?DestroyWindow original fallback threw'
    )
Add-Check `
    'phase2.ui-thread.transaction-and-window-observation' `
    $phase2UiInitializationContract `
    'Initialization must reserve before mutation, roll back on failure, and update role state for window destruction or replacement.'

$stylerUiCapabilityCommitSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'bool CommitUiCapabilityDisposition(' `
    -EndMarker 'UiCapabilityDisposition ReleaseUiDispatcherCapability('
$stylerUiFinalizeOwnerSection = Get-SourceSlice `
    -Text $styler `
    -StartMarker 'void TryFinalizeCleanedUiRuntimeOwner(' `
    -EndMarker 'UiThreadCleanupExecution RunUiThreadCleanupSteps('
$phase2UiCommitGateContract =
    $stylerUiHandleCapabilitySection.Contains(
        'std::atomic_flag capabilityCommitGate = ATOMIC_FLAG_INIT;'
    ) -and
    [regex]::IsMatch(
        $stylerUiCapabilityCommitSection,
        '(?s)terminalReleased\s*=\s*record->capabilityReleasedMask\.load\(.*?terminalRetained\s*=\s*record->capabilityRetainedMask\.load\(.*?\(\s*releasedToCommit\s*&\s*terminalRetained\s*\)\s*!=\s*0\s*\|\|.*?\(\s*retainedToCommit\s*&\s*terminalReleased\s*\)\s*!=\s*0.*?return\s+false\s*;.*?releasedToCommit\s*&=\s*~terminalReleased\s*;.*?retainedToCommit\s*&=\s*~terminalRetained\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerUiCapabilityCommitSection,
        '(?s)if\s*\(\s*releasedToCommit\s*!=\s*0\s*\|\|\s*retainedToCommit\s*!=\s*0\s*\).*?CompleteCapabilityDisposition\(.*?releasedToCommit.*?retainedToCommit.*?ProtocolStatus::Applied.*?return\s+false\s*;'
    ) -and
    (Test-MarkersInOrder `
        -Text $stylerUiCapabilityCommitSection `
        -Markers @(
            'g_uiThreadLifecycle.CompleteCapabilityDisposition(',
            'record->capabilityReleasedMask.fetch_or(',
            'record->capabilityRetainedMask.fetch_or(',
            'record->capabilityPendingReleasedMask.fetch_and(',
            'record->capabilityPendingRetainedMask.fetch_and('
        )) -and
    [regex]::IsMatch(
        $stylerUiCapabilityCommitSection,
        '(?s)UiDispatcherOwnerDecisionGuard\s+commitGuard\(\s*record->capabilityCommitGate\s*\)\s*;.*?committed\s*=\s*commitUnderAdapterGate\(\)'
    ) -and
    ([regex]::Matches(
        $stylerUiCapabilityCommitSection,
        'capabilityReleasedMask\.fetch_or\('
    ).Count -eq 1) -and
    ([regex]::Matches(
        $stylerUiCapabilityCommitSection,
        'capabilityRetainedMask\.fetch_or\('
    ).Count -eq 1) -and
    ([regex]::Matches(
        $stylerUiCapabilityCommitSection,
        'capabilityPendingReleasedMask\.fetch_and\('
    ).Count -eq 1) -and
    ([regex]::Matches(
        $stylerUiCapabilityCommitSection,
        'capabilityPendingRetainedMask\.fetch_and\('
    ).Count -eq 1)

$phase2UiFinalizerRetryContract =
    [regex]::IsMatch(
        $stylerUiFinalizeOwnerSection,
        '(?s)capabilitiesAlreadyFullyReleased\s*=.*?receiptMatchesCleanedOwner.*?receipt->capabilities_terminal.*?receipt->capability_retained_mask\s*==\s*0.*?receipt->capability_released_mask\s*==\s*receipt->capability_created_mask'
    ) -and
    [regex]::IsMatch(
        $stylerUiFinalizeOwnerSection,
        '(?s)finalizationClaimed\.compare_exchange_strong\(.*?if\s*\(\s*!capabilitiesAlreadyFullyReleased\s*\)\s*\{.*?CloseHandleCapabilities\(false\).*?if\s*\(\s*!CommitUiCapabilityDisposition\(.*?\)\s*\)\s*\{\s*record->finalizationClaimed\.store\(\s*false,\s*std::memory_order_release\s*\).*?return\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerUiFinalizeOwnerSection,
        '(?s)if\s*\(\s*!fullyReleased\s*\)\s*\{\s*if\s*\(\s*!terminalRetained\s*\)\s*\{\s*record->finalizationClaimed\.store\(\s*false,\s*std::memory_order_release\s*\).*?return\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerUiFinalizeOwnerSection,
        '(?s)if\s*\(\s*!RemoveUiThreadRuntimeRecord\(record\)\s*\)\s*\{\s*record->finalizationClaimed\.store\(\s*false,\s*std::memory_order_release\s*\)'
    ) -and
    ([regex]::Matches(
        $stylerUiFinalizeOwnerSection,
        'finalizationClaimed\.store\(\s*false,\s*std::memory_order_release\s*\)'
    ).Count -eq 3) -and
    [regex]::IsMatch(
        $stylerUiFinalizeOwnerSection,
        '(?s)if\s*\(\s*!capabilitiesAlreadyFullyReleased\s*\)\s*\{.*?CloseHandleCapabilities\(false\).*?\}\s*bool\s+fullyReleased\s*=\s*false.*?if\s*\(\s*!fullyReleased\s*\).*?return\s*;.*?RemoveUiThreadRuntimeRecord\(record\)'
    )

$phase2UiNoAllocationContract =
    $phase2Protocol.Contains(
        'constexpr std::size_t kUiThreadRegistryCapacity = 64;'
    ) -and
    $phase2Protocol.Contains(
        'std::array<char, kUiReceiptReasonCapacity> text{};'
    ) -and
    $phase2UiProtocolSection.Contains(
        'UiCleanupSnapshot SnapshotForCleanup()'
    ) -and
    $phase2UiProtocolSection.Contains(
        'std::array<Record, kUiThreadRegistryCapacity> records_{};'
    ) -and
    $phase2UiProtocolSection.Contains(
        'if (eligible > tickets.entries.size())'
    ) -and
    $phase2UiProtocolSection.Contains(
        '(void)tickets.TryAppend(BeginCleanupLocked(record));'
    ) -and
    [regex]::IsMatch(
        $phase2UiProtocolSection,
        'UiCleanupTicket\s+BeginCleanupLocked\(Record& record\)\s+noexcept'
    ) -and
    -not $phase2UiProtocolSection.Contains('std::vector') -and
    -not $phase2UiProtocolSection.Contains('std::string') -and
    [regex]::IsMatch(
        $stylerInitializeForThreadSection,
        '(?s)UiUniqueHandle\s+ownedThreadHandle;.*?GetCurrentUiThreadIdentity\(.*?&threadHandleOwnerIdentity.*?ownedThreadHandle\s*=\s*UiUniqueHandle\(\s*threadHandle,\s*RetainedKernelCapabilityKind::UiThread,\s*threadHandleOwnerIdentity,'
    ) -and
    [regex]::IsMatch(
        $stylerInitializeForThreadSection,
        '(?s)UiUniqueHandle\s+ownedCleanupCompletedEvent;.*?cleanupEventOwnerIdentity\s*=\s*ReserveKernelCapability\(.*?RetainedKernelCapabilityKind::UiCleanupEvent,\s*threadId.*?cleanupCompletedEvent\s*=\s*CreateEventW\(.*?CancelKernelCapabilityReservation\(.*?RetainedKernelCapabilityKind::UiCleanupEvent.*?ownedCleanupCompletedEvent\s*=\s*UiUniqueHandle\(\s*cleanupCompletedEvent,\s*RetainedKernelCapabilityKind::UiCleanupEvent,\s*cleanupEventOwnerIdentity,'
    ) -and
    $stylerInitializeForThreadSection.IndexOf(
        'g_uiThreadLifecycle.Reserve(',
        [StringComparison]::Ordinal
    ) -lt
        $stylerInitializeForThreadSection.IndexOf(
            'std::make_shared<UiThreadRuntimeRecord>()',
            [StringComparison]::Ordinal
        ) -and
    $stylerInitializeForThreadSection.Contains(
        'g_uiThreadLifecycle.FailInitialization('
    ) -and
    [regex]::IsMatch(
        $stylerInitializeForThreadSection,
        '(?s)ReleaseUiDispatcherCapability\(record\).*?CommitUiCapabilityDisposition\(.*?dispatcherDisposition.*?ResetCurrentUiThreadTlsOwner\(record\).*?record->CloseHandleCapabilities\(false\).*?CommitUiCapabilityDisposition\(.*?handleDisposition.*?g_uiThreadLifecycle\.FailInitialization\('
    ) -and
    [regex]::IsMatch(
        $stylerInitializeForThreadSection,
        '(?s)retainedDisposition\.retainedMask\s*=\s*jarvis::resource_protocol::kUiCapabilityMask\s*&\s*~terminalMask;.*?CommitUiCapabilityDisposition\('
    ) -and
    $phase2UiCommitGateContract -and
    $phase2UiFinalizerRetryContract -and
    [regex]::IsMatch(
        $stylerUiFinalizeOwnerSection,
        '(?s)dispatcherOwner\.HasOwner\(\).*?callbackUsers\.load\(.*?waiterUsers\.load\(.*?kUiCleanupEventSignaled.*?\(\s*receipt->capability_released_mask\s*&\s*kUiAgileDispatcherCapability\s*\)\s*!=\s*0.*?\(\s*receipt->capability_retained_mask\s*&\s*kUiAgileDispatcherCapability\s*\)\s*==\s*0.*?capabilitiesAlreadyFullyReleased.*?CloseHandleCapabilities\(false\).*?CommitUiCapabilityDisposition\(.*?capability_released_mask\s*==\s*receipt->capability_created_mask.*?RemoveUiThreadRuntimeRecord\(record\)'
    ) -and
    $phase2UiProtocolSection.Contains(
        'CompleteCapabilityDisposition('
    ) -and
    $phase2Protocol.Contains(
        'UiCapability::CleanupEvent'
    ) -and
    $phase2Harness.Contains(
        'ui.capability-release-receipts'
    ) -and
    $phase2Harness.Contains(
        'release-second-thread-handle-and-cleanup-event'
    ) -and
    $phase2Harness.Contains(
        'barrier-admit-borrow-before-release'
    ) -and
    $phase2Harness.Contains(
        'synchronous-release-reentry-sees-releasing'
    ) -and
    $phase2Harness.Contains(
        'release-throw-publishes-opaque-owner'
    ) -and
    $phase2Harness.Contains(
        'protocol-commit-failure-keeps-physical-release-pending'
    ) -and
    [regex]::IsMatch(
        $phase2Harness,
        '(?s)UiCapabilityReleaseReceipts\(\).*?borrowRaceExternalReleases\.load\(.*?\)\s*==\s*1.*?reentrantExternalReleases\.load\(.*?\)\s*==\s*1.*?throwingExternalReleases\.load\(.*?\)\s*==\s*1'
    ) -and
    $phase2Protocol.Contains(
        'capabilities_terminal'
    )
Add-Check `
    'phase2.ui-thread.fixed-snapshot-and-handle-rollback' `
    $phase2UiNoAllocationContract `
    'UI snapshots and reasons must remain fixed-capacity; physical disposition stays pending until a capabilityCommitGate-serialized, same-direction-idempotent terminal commit publishes terminal masks before clearing pending masks; commit, incomplete-receipt, and owner-removal failures must release finalizationClaimed for retry, while an already fully released receipt skips physical close and retries removal directly.'

$phase2UiEnumerationAndBootstrapContract =
    $stylerXamlHostEnumerationSection.Contains(
        'std::array<HWND, kMaxXamlHostWindowSnapshot> windows{};'
    ) -and
    $stylerXamlHostEnumerationSection.Contains(
        'BOOL CALLBACK CollectXamlHostWindow('
    ) -and
    [regex]::IsMatch(
        $stylerXamlHostEnumerationSection,
        '(?s)CollectXamlHostWindow\(.*?\)\s+noexcept\s+try\s*\{.*?\}\s*catch\s*\(\s*\.\.\.\s*\)'
    ) -and
    $stylerXamlHostEnumerationSection.Contains(
        'capacityExhausted = true;'
    ) -and
    $stylerXamlHostEnumerationSection.Contains(
        'XAML host enumeration receipt:'
    ) -and
    -not $stylerXamlHostEnumerationSection.Contains(
        'std::vector<HWND>'
    ) -and
    -not $stylerXamlHostEnumerationSection.Contains('push_back') -and
    $stylerAfterInitSection.Contains(
        'auto xamlHostWindows = GetXamlHostWnds();'
    ) -and
    $stylerAfterInitSection.Contains(
        'if (!xamlHostWindows.Complete())'
    ) -and
    [regex]::Matches(
        $stylerAfterInitSection,
        'InitializeForCurrentThread\(\s*\);'
    ).Count -ge 2 -and
    -not [regex]::IsMatch(
        $stylerAfterInitSection,
        'InitializeForCurrentThread\(\s*kUiThreadRole'
    ) -and
    $stylerOnWindowCreatedSection.Contains(
        'InitializeForCurrentThread(kUiThreadRoleTaskbarBridge);'
    ) -and
    $stylerOnWindowCreatedSection.Contains(
        'InitializeForCurrentThread(kUiThreadRoleXamlHost);'
    )
Add-Check `
    'phase2.ui-thread.enum-boundary-and-bootstrap-dedup' `
    $phase2UiEnumerationAndBootstrapContract `
    'USER32 enumeration must be fixed-capacity and noexcept, while bootstrap enumeration initializes threads without duplicating hook-owned role observations.'

$phase2UiCleanupContract =
    $stylerUiLifecycleSection.Contains(
        'g_uiThreadLifecycle.SnapshotForCleanup()'
    ) -and
    $stylerUiLifecycleSection.Contains(
        'ULONGLONG deadline ='
    ) -and
    $stylerUiLifecycleSection.IndexOf(
        'ULONGLONG deadline =',
        [StringComparison]::Ordinal
    ) -lt
        $stylerUiLifecycleSection.IndexOf(
            'SnapshotForCleanup()',
            [StringComparison]::Ordinal
        ) -and
    [regex]::IsMatch(
        $stylerUiLifecycleSection,
        '(?s)g_uiThreadRuntimeRecord->recordId\s*==\s*record->recordId.*?CompleteUiThreadCleanupOnCurrentThread\(\s*record,\s*ticket\)'
    ) -and
    $stylerUiLifecycleSection.Contains(
        'WaitForMultipleObjects('
    ) -and
    $stylerUiLifecycleSection.Contains(
        'MarkThreadExited('
    ) -and
    $stylerUiLifecycleSection.Contains(
        'SealAndLogUiThreadLifecycle('
    ) -and
    $stylerUiLifecycleSection.Contains(
        'LogUiThreadLifecycleReceipts('
    ) -and
    $stylerUninitSection.Contains(
        '"cleanup-skipped-callbacks-not-drained"'
    ) -and
    [regex]::IsMatch(
        $stylerUninitSection,
        '(?s)bool\s+cleanupWorkDrained\s*=\s*WaitForTapLifecycleIdle\(\s*5000\s*\).*?LogUiThreadLifecycleReceipts\(.*?"post-cleanup-callback-drain-final".*?"post-cleanup-callback-drain-timeout"'
    ) -and
    -not $stylerUninitSection.Contains('GetTaskbarUiWnd()') -and
    -not $stylerUninitSection.Contains('GetXamlHostWnds()') -and
    -not $stylerUiCleanupSection.Contains(
        'g_uiThreadRuntimeRegistryMutex'
    ) -and
    [regex]::IsMatch(
        $stylerUiLifecycleSection,
        '(?s)CompleteUiThreadCleanupOnCurrentThread\(.*?\)\s+noexcept\s+try\s*\{'
    ) -and
    [regex]::IsMatch(
        $stylerUiLifecycleSection,
        '(?s)RunUiThreadCleanupDispatcherCallback\(.*?\)\s+noexcept\s+try\s*\{\s*TapLifecycleScope'
    ) -and
    [regex]::IsMatch(
        $stylerUiLifecycleSection,
        '(?s)CleanupAllRegisteredUiThreads\(.*?\)\s+noexcept\s+try\s*\{'
    ) -and
    [regex]::IsMatch(
        $phase2UiProtocolSection,
        '(?s)SealGeneration\(.*?if\s*\(\s*record.active_ticket_id\s*!=\s*0\s*\).*?record.last_ticket_id\s*=\s*record.active_ticket_id.*?record.active_ticket_id\s*=\s*0.*?UiRecordState::Retained'
    )
Add-Check `
    'phase2.ui-thread.snapshot-deadline-and-terminal-receipts' `
    $phase2UiCleanupContract `
    'Cleanup must start from a registry snapshot, handle the current thread directly, use one total deadline, and seal receipts even when destructive cleanup is skipped.'

$stylerRetryWindowThreadHooksSection = Get-SourceSlice `
    -Text $stylerRunFromWindowThreadSection `
    -StartMarker 'bool RetryTrackedWindowThreadHooks()' `
    -EndMarker 'bool HasTrackedWindowThreadHooks()'
$stylerRemoveWindowThreadHookSection = Get-SourceSlice `
    -Text $stylerRunFromWindowThreadSection `
    -StartMarker 'bool RemoveTrackedWindowThreadHook(' `
    -EndMarker 'void LogWindowThreadDispatchReceipts()'
$stylerRunWindowThreadExternalSection = Get-SourceSlice `
    -Text $stylerRunFromWindowThreadSection `
    -StartMarker 'bool RunFromWindowThread(HWND hWnd,' `
    -EndMarker 'void OnWindowCreated(HWND hWnd,'
$stylerClaimWindowThreadContextSection = Get-SourceSlice `
    -Text $stylerRunFromWindowThreadSection `
    -StartMarker 'ClaimRunFromWindowThreadContext(' `
    -EndMarker 'WindowThreadDispatchCompactReceipt*'
$stylerClaimWindowThreadLockedSection = Get-SourceSlice `
    -Text $stylerClaimWindowThreadContextSection `
    -StartMarker 'std::lock_guard<std::mutex> lock(' `
    -EndMarker '        if (degradeAfterUnlock) {'
$stylerClaimWindowThreadAfterUnlockSection = Get-SourceSlice `
    -Text $stylerClaimWindowThreadContextSection `
    -StartMarker 'if (degradeAfterUnlock) {' `
    -EndMarker '    } catch (...) {'
$stylerLogWindowThreadReceiptsSection = Get-SourceSlice `
    -Text $stylerRunFromWindowThreadSection `
    -StartMarker 'void LogWindowThreadDispatchReceipts()' `
    -EndMarker 'bool RunFromWindowThread(HWND hWnd,'
$stylerLogWindowThreadResourceSnapshotSection = Get-SourceSlice `
    -Text $stylerLogWindowThreadReceiptsSection `
    -StartMarker 'bool pending = false;' `
    -EndMarker '    Wh_Log('

$phase2DispatchClaimAndLogUnlockedSideEffects =
    $stylerClaimWindowThreadContextSection.Contains(
        'std::optional<WindowThreadDispatchObserverReference>'
    ) -and
    ([regex]::Matches(
        $stylerClaimWindowThreadLockedSection,
        'observerReference\.emplace\(\s*context\s*\)'
    ).Count -eq 1) -and
    (Test-MarkersInOrder `
        -Text $stylerClaimWindowThreadLockedSection `
        -Markers @(
            'observerReference.emplace(context);',
            'slotValidated = true;',
            'context->protocol.ClaimCallback()'
        )) -and
    $stylerClaimWindowThreadLockedSection.Contains(
        'context->protocol.ClaimCallback()'
    ) -and
    $stylerClaimWindowThreadLockedSection.Contains(
        'callbackReference.emplace(context);'
    ) -and
    -not [regex]::IsMatch(
        $stylerClaimWindowThreadLockedSection,
        '\b(?:MarkWindowThreadDispatchReceiptDegraded|PublishWindowThreadDispatchReceipt|RequirePermanentUnloadSafetyPin|Wh_Log|LogJarvisDiagnosticNoThrow|ReleaseRunFromWindowThreadContext)\s*\(|observerReference->Get\('
    ) -and
    [regex]::IsMatch(
        $stylerClaimWindowThreadAfterUnlockSection,
        '(?s)if\s*\(\s*degradeAfterUnlock\s*\)\s*\{\s*MarkWindowThreadDispatchReceiptDegraded\(\s*observerReference->Get\(\),\s*E_UNEXPECTED\s*\).*?if\s*\(\s*publishAfterUnlock\s*\)\s*\{\s*PublishWindowThreadDispatchReceipt\(.*?observerReference->Get\(\)'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)class\s+WindowThreadDispatchObserverReference\s*\{.*?AddRefRunFromWindowThreadContext\(context_\).*?~WindowThreadDispatchObserverReference\(\)\s+noexcept\s*\{.*?ReleaseRunFromWindowThreadContext\(context_\)'
    ) -and
    ([regex]::Matches(
        $stylerLogWindowThreadReceiptsSection,
        'g_windowThreadResourcesMutex'
    ).Count -eq 1) -and
    [regex]::IsMatch(
        $stylerLogWindowThreadResourceSnapshotSection,
        '(?s)bool\s+pending\s*=\s*false\s*;\s*unsigned\s+hookCount\s*=\s*0\s*;\s*\{\s*std::lock_guard<std::mutex>\s+lock\(\s*g_windowThreadResourcesMutex\s*\)\s*;.*?pending\s*=\s*g_pendingWindowThreadContext\s*!=\s*nullptr\s*;.*?hookCount\s*=.*?std::count_if\(.*?\)\s*;\s*\}'
    ) -and
    -not [regex]::IsMatch(
        $stylerLogWindowThreadResourceSnapshotSection,
        '\b(?:Wh_Log|LogJarvisDiagnosticNoThrow|MarkWindowThreadDispatchReceiptDegraded|PublishWindowThreadDispatchReceipt|RequirePermanentUnloadSafetyPin)\s*\('
    ) -and
    [regex]::IsMatch(
        $stylerLogWindowThreadReceiptsSection,
        '(?s)\}\s*Wh_Log\(\s*L"Dispatch summary: receipts='
    )
Add-Check `
    'phase2.dispatch.claim-and-log-unlocked-side-effects' `
    $phase2DispatchClaimAndLogUnlockedSideEffects `
    'ClaimRunFromWindowThreadContext may only claim or mutate exact owners under g_windowThreadResourcesMutex; deferred receipt, pin, and diagnostic work must use an observer AddRef after unlock with RAII Release, while dispatch logging may snapshot only pending and hookCount under that mutex and must log outside it.'

$phase2DispatchTicketType =
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)struct\s+WindowThread[A-Za-z0-9_]*Ticket\s*\{(?=[^}]*dispatchId)(?=[^}]*(?:generation|operationEpoch))(?=[^}]*HHOOK\s+hook)(?=[^}]*RunFromWindowThreadContext\*\s+context)[^}]*\}'
    )
$phase2DispatchTryAdmission =
    [regex]::IsMatch(
        $stylerRunWindowThreadExternalSection,
        '(?s)(?:WindowThreadOperationClaim\s+[A-Za-z_][A-Za-z0-9_]*\s*\(|(?:std::try_to_lock|try_lock\(\))).*?if\s*\(\s*![A-Za-z_][A-Za-z0-9_]*\s*\).*?return\s+false\s*;'
    )
$phase2DispatchExactTicketOperations =
    [regex]::IsMatch(
        $stylerRunWindowThreadExternalSection,
        '(?s)(?:Reserve|Claim)[A-Za-z0-9_]*WindowThread[A-Za-z0-9_]*(?:Hook|Dispatch)[A-Za-z0-9_]*\(.*?SetWindowsHookEx\(.*?(?:Commit|Complete)[A-Za-z0-9_]*WindowThread[A-Za-z0-9_]*(?:Hook|Dispatch)[A-Za-z0-9_]*\('
    ) -and
    [regex]::IsMatch(
        $stylerRemoveWindowThreadHookSection,
        '(?s)(?:Reserve|Claim)[A-Za-z0-9_]*\(.*?UnhookWindowsHookEx\(.*?(?:Commit|Complete)[A-Za-z0-9_]*\('
    ) -and
    [regex]::IsMatch(
        $stylerRetryWindowThreadHooksSection,
        '(?s)(?:Reserve|Claim)[A-Za-z0-9_]*\(.*?UnhookWindowsHookEx\(.*?(?:Commit|Complete)[A-Za-z0-9_]*\('
    )
$phase2DispatchExternalCallsOutsideLocks =
    $phase2DispatchTryAdmission -and
    -not $stylerRunWindowThreadExternalSection.Contains(
        'g_windowThreadHookOperationMutex'
    ) -and
    -not $stylerRemoveWindowThreadHookSection.Contains(
        'g_windowThreadHookOperationMutex'
    ) -and
    -not $stylerRetryWindowThreadHooksSection.Contains(
        'g_windowThreadHookOperationMutex'
    ) -and
    -not [regex]::IsMatch(
        $stylerRunWindowThreadExternalSection,
        '(?s)(?:lock_guard|unique_lock)<std::mutex>[^;]*g_windowThreadResourcesMutex[^;]*;[^}]*(?:SetWindowsHookEx|SendMessageTimeoutW|UnhookWindowsHookEx)\('
    ) -and
    -not [regex]::IsMatch(
        $stylerRemoveWindowThreadHookSection,
        '(?s)(?:lock_guard|unique_lock)<std::mutex>[^;]*g_windowThreadResourcesMutex[^;]*;[^}]*UnhookWindowsHookEx\('
    ) -and
    -not [regex]::IsMatch(
        $stylerRetryWindowThreadHooksSection,
        '(?s)(?:lock_guard|unique_lock)<std::mutex>[^;]*g_windowThreadResourcesMutex[^;]*;[^}]*UnhookWindowsHookEx\('
    )
$phase2DispatchExternalApiContract =
    $phase2DispatchTicketType -and
    $phase2DispatchExactTicketOperations -and
    $phase2DispatchExternalCallsOutsideLocks -and
    $stylerRunWindowThreadExternalSection.Contains(
        'SetWindowsHookEx('
    ) -and
    $stylerRunWindowThreadExternalSection.Contains(
        'SendMessageTimeoutW('
    ) -and
    $stylerRemoveWindowThreadHookSection.Contains(
        'UnhookWindowsHookEx('
    )
Add-Check `
    'phase2.dispatch.external-api-outside-lock-ticket' `
    $phase2DispatchExternalApiContract `
    'SetWindowsHookEx, SendMessageTimeoutW, and UnhookWindowsHookEx must run outside bookkeeping locks after a fail-fast exact ticket claim, then commit by dispatch ID, generation, hook, and context.'

$phase2DispatchAdapterContract =
    $stylerRunFromWindowThreadSection.Contains(
        'std::array<WindowThreadDispatchCompactReceipt, 64>'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'g_trackedWindowThreadHookContext'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'std::array<RetainedUntrackedWindowThreadHook, 64>'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'g_retainedUntrackedWindowThreadHooks'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'AddRefRunFromWindowThreadContext(context)'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'RecordUnclaimedWindowThreadCallback('
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'static_cast<WPARAM>(dispatchId)'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)g_trackedWindowThreadHookDispatchId\s*!=\s*dispatchId.*?context->dispatchId\s*!=\s*dispatchId'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'WindowThreadDispatchReason::SendTimeoutOrFailure'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'WindowThreadDispatchReason::HookRemovalFailed'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'g_windowThreadDispatchCapacityReceipt'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'mutable std::mutex mutex;'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'enum class WindowThreadDispatchObservation'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'std::atomic<std::uint64_t> protocolLateCallbacks'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'std::atomic<std::uint64_t> adapterLateCallbacks'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'MergeWindowThreadDispatchCallbackCountsLocked('
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'std::atomic<bool> receiptDegraded'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'std::atomic<std::uint32_t> actualReleasedMask'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'std::atomic<std::uint64_t> protocolDoubleRelease'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'std::atomic<std::uint64_t> actualDoubleRelease'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'std::atomic<std::uint64_t> resourcesInflight'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'MergeWindowThreadDispatchDoubleReleaseCountsLocked('
    ) -and
    ([regex]::Matches(
        $stylerRunFromWindowThreadSection,
        'MarkWindowThreadDispatchCompactActualResourceReleased\('
    ).Count -ge 3) -and
    $stylerRunFromWindowThreadSection.Contains(
        'class WindowThreadDispatchCallbackReference'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)\[\]\(\s*int\s+nCode,\s*WPARAM\s+wParam,\s*LPARAM\s+lParam\)\s+noexcept\s*->\s*LRESULT\s*\{.*?auto\s+callbackReference\s*=\s*ClaimRunFromWindowThreadContext'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)bool\s+exactSlotCleared\s*=\s*false;.*?if\s*\(\s*exactSlotCleared\s*\).*?MarkWindowThreadDispatchCompactActualResourceReleased\(.*?kDispatchHookResource.*?HookState::Removed.*?else\s*\{.*?HookState::Retained'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        'WindowThreadDispatchObservation::\s*ReceiptCapacityExhausted'
    ) -and
    -not [regex]::IsMatch(
        (Get-SourceSlice -Text $stylerRunFromWindowThreadSection `
            -StartMarker 'struct WindowThreadDispatchCompactReceipt {' `
            -EndMarker '};'),
        '\b(?:HWND|PVOID|void\*)\b'
    ) -and
    $phase2Protocol.Contains(
        'enum class CallbackPhase'
    ) -and
    $phase2Protocol.Contains(
        'enum class DispatchRetainedReason'
    ) -and
    $phase2Protocol.Contains(
        'enum class DispatchReason'
    ) -and
    $phase2Protocol.Contains(
        'enum class DispatchResourceKind'
    ) -and
    $phase2Protocol.Contains(
        'enum class DispatchResourceDisposition'
    ) -and
    [regex]::IsMatch(
        $phase2Protocol,
        '(?s)struct\s+DispatchResourceReceipt\s*\{.*?DispatchResourceKind\s+kind.*?DispatchResourceDisposition\s+disposition.*?DispatchRetainedReason\s+retained_reason'
    ) -and
    [regex]::IsMatch(
        $phase2Protocol,
        '(?s)std::array<DispatchResourceReceipt,\s*3>\s+resources.*?SenderReference.*?CallbackReference.*?HookHandle'
    ) -and
    -not $phase2DispatchProtocolSection.Contains(
        'resources_created_'
    ) -and
    -not $phase2DispatchProtocolSection.Contains(
        'resources_released_'
    ) -and
    [regex]::IsMatch(
        $phase2DispatchProtocolSection,
        '(?s)for\s*\(\s*const\s+auto&\s+resource\s*:\s*receipt\.resources\s*\).*?resources_created.*?resources_released.*?resources_retained.*?resources_inflight'
    ) -and
    [regex]::IsMatch(
        $phase2Protocol,
        '(?s)struct\s+DispatchReceipt\s*\{.*?CallbackPhase\s+callback_phase\s*=\s*CallbackPhase::None;.*?resources_retained.*?resources_inflight.*?DispatchReason\s+reason\s*=\s*DispatchReason::None;.*?DispatchRetainedReason\s+retained_reason.*?bool\s+protocol_failure\s*=\s*false;'
    ) -and
    -not [regex]::IsMatch(
        $phase2DispatchProtocolSection,
        '\bstd::(?:string|vector|unordered_(?:map|set))\b'
    ) -and
    [regex]::IsMatch(
        $phase2DispatchProtocolSection,
        '(?s)DispatchClaimStatus\s+ClaimCallback\(\)\s+noexcept.*?reason_\s*=\s*DispatchReason::CallbackClaimed;\s*callback_phase_\s*=\s*CallbackPhase::Claimed;\s*state_\s*=\s*DispatchState::Claimed;.*?catch\s*\(\.\.\.\)\s*\{\s*return\s+DispatchClaimStatus::ProtocolFailure;'
    ) -and
    [regex]::IsMatch(
        $phase2DispatchProtocolSection,
        '(?s)CompleteHookRemoval\(.*?if\s*\(\s*!succeeded\s*\)\s*\{.*?hook_state_\s*=\s*HookState::Retained;.*?retained_reason_.*?UpdateTerminalLocked\(\);.*?return\s+ProtocolStatus::Applied;'
    ) -and
    [regex]::IsMatch(
        $phase2DispatchProtocolSection,
        '(?s)CompleteCallback\(.*?callback_phase_\s*==\s*CallbackPhase::Completed.*?SaturatingIncrementDispatchCount\(\s*duplicate_callbacks_\s*\).*?callback_phase_\s*!=\s*CallbackPhase::Claimed\s*\|\|.*?!callback_ref_held_.*?callback_phase_\s*=\s*CallbackPhase::Completed;'
    ) -and
    [regex]::IsMatch(
        $phase2DispatchProtocolSection,
        '(?s)static\s+void\s+SaturatingIncrementDispatchCount\(.*?numeric_limits<std::uint64_t>::max\(\)'
    ) -and
    [regex]::IsMatch(
        $phase2DispatchProtocolSection,
        '(?s)ClaimCallback\(.*?SaturatingIncrementDispatchCount\(\s*duplicate_callbacks_\s*\).*?SaturatingIncrementDispatchCount\(\s*late_callbacks_\s*\)'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)std::optional<WindowThreadDispatchCallbackReference>\s+callbackReference\s*;.*?callbackReference\.emplace\(\s*context\s*\).*?g_pendingWindowThreadContext\s*=\s*nullptr\s*;.*?return\s+std::move\(\s*\*callbackReference\s*\)\s*;'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)DispatchClaimStatus::\s*ProtocolFailure.*?pending slot still owns the callback reference.*?return\s+\{\};'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)validMask\s*=.*?resourceMask\s*==\s*kDispatchSenderResource.*?resourceMask\s*==\s*kDispatchCallbackResource.*?resourceMask\s*==\s*kDispatchHookResource.*?actualDoubleRelease'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)protocolDoubleRelease\.store\(.*?std::max\(.*?value\.double_release.*?MergeWindowThreadDispatchDoubleReleaseCountsLocked'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)UpdateDegradedWindowThreadDispatchReceiptLocked\(.*?hasRetainedResource\s*=.*?retainedMask\s*!=\s*0.*?contextReferencesRetained\.load\(.*?retainedReason\.store\(.*?hasRetainedResource.*?DispatchRetainedReason::ProtocolFailure.*?DispatchRetainedReason::None.*?state\.store\(.*?hasRetainedResource.*?DispatchState::\s*Retained.*?DispatchState::\s*Completed'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)catch\s*\(\.\.\.\)\s*\{.*?const\s+std::uint32_t\s+resourceUniverse\s*=\s*WindowThreadDispatchResourceUniverse\(receipt\);.*?const\s+std::uint32_t\s+releasedMask\s*=.*?&\s*resourceUniverse;.*?hasRetainedResource\s*=.*?contextReferencesRetained\.load\(.*?retainedReason\.store\(.*?DispatchRetainedReason::ProtocolFailure'
    ) -and
    ([regex]::Matches(
        (Get-SourceSlice -Text $stylerRunFromWindowThreadSection `
            -StartMarker 'void MarkWindowThreadDispatchCompactReceiptDegraded(' `
            -EndMarker 'void MarkWindowThreadDispatchReceiptDegraded('),
        'WindowThreadDispatchResourceUniverse\(receipt\)'
    ).Count -eq 1) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)PublishWindowThreadDispatchReceipt\(.*?compact->receiptDegraded\.load\(.*?context->receiptDegraded\.store\(.*?return\s+false;.*?lock_guard<std::mutex>\s+receiptLock\(.*?compact->receiptDegraded\.load\(.*?context->receiptDegraded\.store\(.*?return\s+false;'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)bool\s+MarkWindowThreadDispatchActualResourceReleased\(.*?const\s+bool\s+exactOwner\s*=.*?MarkWindowThreadDispatchCompactActualResourceReleased\(.*?const\s+bool\s+receiptHealthy\s*=.*?compactReceipt->receiptDegraded\.load\(.*?if\s*\(\s*!exactOwner\s*\|\|\s*!receiptHealthy\s*\).*?context->receiptDegraded\.store\(.*?return\s+exactOwner;'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)bool\s+ReleaseWindowThreadDispatchContextResource\(.*?const\s+bool\s+exactOwner\s*=\s*MarkWindowThreadDispatchActualResourceReleased\(.*?if\s*\(\s*!exactOwner\s*\).*?SaturatingIncrementWindowThreadDispatchCount\(.*?contextReferencesRetained.*?MarkWindowThreadDispatchReceiptDegraded\(.*?return\s+false;.*?const\s+bool\s+receiptHealthy\s*=.*?context->receiptDegraded\.load\(.*?ReleaseRunFromWindowThreadContext\(context\);.*?return\s+receiptHealthy;'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'std::atomic<std::uint64_t> contextReferencesRetained{0};'
    ) -and
    $stylerRunFromWindowThreadSection.Contains(
        'retained-context-refs=%llu'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)~WindowThreadDispatchCallbackReference\(\)\s+noexcept.*?ReleaseWindowThreadDispatchContextResource\(\s*context_,\s*kDispatchCallbackResource\s*\)'
    ) -and
    ([regex]::Matches(
        $stylerRunFromWindowThreadSection,
        'ReleaseRunFromWindowThreadContext\(context\);'
    ).Count -eq 1) -and
    -not [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)adapter(?:Late|Duplicate)Callbacks\.fetch_add\('
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)SaturatingIncrementWindowThreadDispatchCount\(\s*&receipt->adapterDuplicateCallbacks\s*\).*?SaturatingIncrementWindowThreadDispatchCount\(\s*&receipt->adapterLateCallbacks\s*\)'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)if\s*\(\s*!hook\s*\).*?protocol\.Register\(\s*1,\s*dispatchId,\s*false,\s*false\s*\).*?protocol\.Cancel\(.*?DispatchReason::HookInstallFailed.*?PublishWindowThreadDispatchReceipt\(context\).*?ReleaseWindowThreadDispatchContextResource\(\s*context,\s*kDispatchCallbackResource\s*\).*?ReleaseWindowThreadDispatchContextResource\(\s*context,\s*kDispatchSenderResource\s*\)'
    ) -and
    [regex]::IsMatch(
        $stylerRunFromWindowThreadSection,
        '(?s)bool\s+RunFromWindowThread\(.*?\)\s+noexcept\s+try\s*\{.*?FailClosedForeignAbiException\(\s*L"RunFromWindowThread production boundary threw"\s*\)'
    ) -and
    ([regex]::Matches(
        $stylerUninitSection,
        'LogWindowThreadDispatchReceipts\(\)'
    ).Count -ge 2) -and
    [regex]::IsMatch(
        $stylerUninitSection,
        '(?s)cleanupWorkDrained\s*=\s*WaitForTapLifecycleIdle\(5000\).*?RetryTrackedWindowThreadHooks\(\).*?LogWindowThreadDispatchReceipts\(\)'
    )
Add-Check `
    'phase2.dispatch.fixed-slot-accounting-and-receipts' `
    $phase2DispatchAdapterContract `
    'Dispatch must use fixed enumerable resources, ID-qualified claim, context ownership for retained hooks, and consistent reasoned receipts.'

$phase2FaultLabStaticContract =
    $phase2FaultRunner.Contains(
        'jarvis2-offline-lifecycle-fault-lab'
    ) -and
    $phase2FaultRunner.Contains(
        'activationPermitted = $false'
    ) -and
    $phase2FaultRunner.Contains(
        "liveExplorer = 'not-run'"
    ) -and
    $phase2FaultRunner.Contains('-std=c++20') -and
    $phase2FaultRunner.Contains('-Werror') -and
    -not [regex]::IsMatch(
        $phase2FaultRunner,
        '(?i)\b(?:Start-Process|Invoke-WebRequest|curl|windhawk\.exe|explorer\.exe|active-module\.txt)\b'
    ) -and
    -not [regex]::IsMatch(
        $phase2Harness,
        '(?i)#include\s*<windows\.h>|\b(?:RevokeInterfaceFromGlobal|SetWindowsHookEx|SendMessageTimeoutW)\s*\('
    ) -and
    $phase2Harness.Contains('std::barrier') -and
    $phase2Harness.Contains('enum class RetainReasonCode') -and
    $phase2Harness.Contains('reasonCode') -and
    $phase2FaultRunner.Contains(
        'Assert-HarnessValidatorNegativeCases'
    ) -and
    $phase2FaultRunner.Contains(
        'retained resource has no reason'
    ) -and
    -not $phase2Harness.Contains(
        'RecordObservedResourceTotals'
    )
Add-Check `
    'phase2.fault-lab.portable-static-contract' `
    $phase2FaultLabStaticContract `
    'The mandatory fault lab must be deterministic, portable, strict-warning, and incapable of activating or touching Explorer.'

$themeMatch = [regex]::Match($styler, '(?s)// JARVIS2_THEME_BEGIN(?<theme>.*?)// JARVIS2_THEME_END')
Add-Check 'styler.theme-markers' $themeMatch.Success 'The first-party theme needs an auditable source boundary.'
if ($themeMatch.Success) {
    $theme = $themeMatch.Groups['theme'].Value
    $targetCount = [regex]::Matches($theme, 'ThemeTargetStyles\{').Count
    Add-Check 'styler.theme-target-count' ($targetCount -ge 6 -and $targetCount -le 16) "Expected a focused target set; found $targetCount."
    Add-Check 'styler.theme-solid-brushes' (-not [regex]::IsMatch($theme, 'AcrylicBrush|WindhawkBlur|ImageBrush|https?://')) 'The default theme must use local solid brushes only.'
    Add-Check 'styler.no-global-grid-selector' (-not [regex]::IsMatch($theme, 'ThemeTargetStyles\{L"Grid"')) 'The theme must not match every Grid in Explorer.'
}

Test-Pattern 'icon.default-disabled' $iconSize '(?m)^- Enabled:\s+false\s*$' 'The private-symbol experiment must be disabled by default.'
Test-Pattern 'icon.default-stock-size' $iconSize '(?m)^- IconSize:\s+24\s*$' 'Enabling default settings must retain the stock icon size.'
Add-Check 'icon.bounded-size' ($iconSize.Contains('iconSize < 20 || iconSize > 32') -and $iconSize.Contains('iconSize = 24;')) 'Icon size must be constrained to 20-32 with a stock-safe fallback.'
Add-Check 'icon.single-symbol-hook' ([regex]::Matches($iconSize, 'LR"\(').Count -eq 1 -and [regex]::Matches($iconSize, 'WindhawkUtils::SYMBOL_HOOK\s+hooks\[\]').Count -eq 1) 'M2 may resolve exactly one private Taskbar.View symbol.'
Test-Pattern 'icon.modern-symbol-only' $iconSize 'TaskbarConfiguration::GetIconHeightInViewPixels\(void\)' 'M2 must use only the modern icon-height calculation.'
Test-NoPattern 'icon.no-broad-upstream-hooks' $iconSize 'VirtualProtect|SHAppBarMessage|SendMessageTimeoutW|LoadLibraryExW_Hook|OffsetFromAssembly|SystemTrayController|SearchButtonBase|TaskListButton_Update|TaskbarFrame_' 'M2 must omit the broad upstream geometry, tray, search, scanner and memory-write paths.'
Test-NoPattern 'icon.no-refresh-side-effect' $iconSize 'WM_SETTINGCHANGE|SPI_SETLOGICALDPIOVERRIDE|SendMessageW?\(' 'M2 must not force Explorer layout refreshes.'
Test-NoPattern 'icon.no-window-code' $iconSize 'CreateWindowExW|SetWindowLong|SetWindowPos|RegisterClass' 'M2 must not create or manipulate windows.'
Test-NoPattern 'icon.no-network-or-telemetry' $iconSize 'WinINet|WinHTTP|NetworkInformation|HttpClient|StartStatsTimer|socket\(' 'M2 must not contain network or telemetry paths.'
Test-Pattern 'icon.explorer-sha-gate' $iconSize ([regex]::Escape($baseline.explorer.sha256)) 'M2 in-process Explorer hash must match the compatibility manifest.'
Test-Pattern 'icon.taskbar-sha-gate' $iconSize ([regex]::Escape($baseline.taskbarView.sha256)) 'M2 in-process Taskbar.View hash must match the compatibility manifest.'
Add-Check 'icon.loaded-path-gate' ($iconSize.Contains('MicrosoftWindows.Client.Core_cw5n1h2txyewy') -and $iconSize.Contains('Taskbar.View.dll') -and $iconSize.Contains('actualTaskbarViewPath')) 'M2 must verify the exact loaded Taskbar.View path.'
Test-Pattern 'icon.legacy-module-rejected' $iconSize 'GetModuleHandleW\(L"ExplorerExtensions\.dll"\)' 'The current profile must reject the legacy module instead of using it as fallback.'
Add-Check 'icon.runtime-quiesce' ($iconSize.Contains('KillSwitchWatcherThread') -and $iconSize.Contains('LatchRuntimeBlocked') -and $iconSize.Contains('g_runtimeBlocked.load(std::memory_order_acquire)')) 'A watcher must latch the active hook into file-I/O-free pass-through mode.'
Add-Check 'icon.quiesce-latched' ($iconSize.Contains('A full module reload and a new one-shot permit are') -and $iconSize.Contains('settings changed without an active hook')) 'Settings changes must not reactivate a quiesced module.'

$initStart = $iconSize.IndexOf('BOOL Wh_ModInit()', [StringComparison]::Ordinal)
$initOrderValid = $false
if ($initStart -ge 0) {
    $initBody = $iconSize.Substring($initStart)
    $killIndex = $initBody.IndexOf('if (IsEmergencyKillSwitchArmed())', [StringComparison]::Ordinal)
    $enabledIndex = $initBody.IndexOf('if (!settings.enabled)', [StringComparison]::Ordinal)
    $verifyIndex = $initBody.IndexOf('if (!VerifyHost(&taskbarViewModule))', [StringComparison]::Ordinal)
    $gateIndex = $initBody.IndexOf('if (!stateGate.tryAcquire())', [StringComparison]::Ordinal)
    $permitIndex = $initBody.IndexOf('ValidateAndConsumeActivationPermit()', [StringComparison]::Ordinal)
    $permitConsumeIndex = $permitIndex
    if ($permitIndex -lt 0) {
        $permitIndex = $initBody.IndexOf('OpenValidatedActivationPermit()', [StringComparison]::Ordinal)
        $permitConsumeIndex = $initBody.IndexOf('ConsumeActivationPermit(', [StringComparison]::Ordinal)
    }
    $hookIndex = $initBody.IndexOf('if (!HookTaskbarIconSize(taskbarViewModule))', [StringComparison]::Ordinal)
    $initOrderValid =
        $killIndex -ge 0 -and
        $enabledIndex -gt $killIndex -and
        $verifyIndex -gt $enabledIndex -and
        $gateIndex -gt $verifyIndex -and
        $permitIndex -gt $gateIndex -and
        $permitConsumeIndex -ge $permitIndex -and
        $hookIndex -gt $permitConsumeIndex
}
Add-Check 'icon.init-order' $initOrderValid 'Kill switch, disabled default, host verification, state gate and one-shot permit must precede symbol hooking.'

Add-Check 'compatibility.schema-current' ($compatibility.schemaVersion -ge 3) 'Compatibility manifest must use the current safety-gate schema.'
$manifestSafetyContract =
    $compatibility.safety.activationPermit -eq 'active-module.txt' -and
    $compatibility.safety.activationPermitEncoding -eq 'strict-ascii-module-id-no-bom-no-newline' -and
    $compatibility.safety.activationPermitLifetime -eq 'one-shot-consumed-before-hook-registration' -and
    $compatibility.safety.activationPermitMaxAgeSeconds -eq 300 -and
    $compatibility.safety.stateGate -eq 'Local\JARVIS2.StateGate.v1' -and
    $compatibility.safety.unknownStatePolicy -eq 'fail-closed'
Add-Check 'compatibility.activation-permit-contract' $manifestSafetyContract 'Manifest must define the single-file, exact ASCII, five-minute, one-shot permit contract.'
Add-Check 'compatibility.exact-build' ($compatibility.host.minimumWindowsBuild -eq $compatibility.host.maximumWindowsBuild) 'Release compatibility must be an exact Windows build.'
$manifestIds = @($compatibility.modules | ForEach-Object id | Sort-Object)
Add-Check 'compatibility.module-allowlist' (($manifestIds -join ',') -eq 'jarvis-native-taskbar,jarvis-taskbar-icon-size') 'Compatibility manifest must list exactly the two reviewed modules.'
Add-Check 'compatibility.legacy-absent' ($baseline.runtimeModuleExpectations.legacyExplorerExtensions -eq 'absent') 'Legacy ExplorerExtensions.dll must be explicitly absent for this profile.'
Add-Check 'compatibility.sxs-not-accepted' (@($baseline.observedAlternates | Where-Object acceptedAsLoadedModule).Count -eq 0) 'Same-version SxS Taskbar.View binaries must not be accepted by version alone.'

$supervisorBaselineMatches =
    $supervisorSource.Contains("public const string CurrentBuild = `"$($baseline.windowsBuild)`"") -and
    $supervisorSource.Contains("public const int Ubr = $($baseline.ubr)") -and
    $supervisorSource.Contains($baseline.explorer.productVersion) -and
    $supervisorSource.Contains($baseline.explorer.sha256) -and
    $supervisorSource.Contains($baseline.explorer.size.ToString()) -and
    $supervisorSource.Contains($baseline.taskbarView.productVersion) -and
    $supervisorSource.Contains($baseline.taskbarView.sha256) -and
    $supervisorSource.Contains($baseline.taskbarView.size.ToString()) -and
    $supervisorSource.Contains($baseline.systemTray.productVersion) -and
    $supervisorSource.Contains($baseline.systemTray.sha256) -and
    $supervisorSource.Contains($baseline.searchUx.productVersion) -and
    $supervisorSource.Contains($baseline.searchUx.sha256)
Add-Check 'compatibility.supervisor-sync' $supervisorBaselineMatches 'Supervisor constants must match every required host fingerprint.'

$stylerLock = @($upstreamLock.dependencies | Where-Object name -eq 'Windows 11 Taskbar Styler')
Add-Check 'upstream.taskbar-styler-lock' ($stylerLock.Count -eq 1 -and $stylerLock[0].sourceSha256 -eq 'E84FD55F81D6A0214EAE3BE6B7C89D1C1A2C95BCD7428B10F6C083F2B3E1FD21') 'M1 provenance must pin the audited upstream source.'
$iconLock = @($upstreamLock.dependencies | Where-Object name -eq 'Taskbar height and icon size')
Add-Check 'upstream.icon-size-lock' ($iconLock.Count -eq 1 -and $iconLock[0].releaseCommit -eq '5d70208acc5a1f46d1c28439cb21c13f1079ec1d' -and $iconLock[0].sourceSha256Lf -eq 'F8FC11864877B1AD8DD975D4514E28608AA60E5A4924EFBAB363ACD54FEBBB57') 'M2 provenance must pin the GPL upstream commit and canonical source hash.'

Add-Check 'toolchain.lock-schema' ($toolchainLock.schemaVersion -eq 2) 'The toolchain lock must use the aggregate-input schema.'
$lockedScopeNames = @($toolchainLock.compileInputTree.scopes | ForEach-Object { "$($_.kind):$($_.relativePath.Replace('\', '/'))" } | Sort-Object)
$expectedScopeNames = @('file:Engine/1.7.3/64/windhawk.lib', 'file:windhawk.exe', 'file:windhawk.ini', 'tree:Compiler')
$compileTreeLocked =
    $toolchainLock.compileInputTree.algorithm -eq 'sha256-path-size-content-v1' -and
    [int64]$toolchainLock.compileInputTree.fileCount -ge 8000 -and
    [int64]$toolchainLock.compileInputTree.bytes -ge 500000000 -and
    ([string]$toolchainLock.compileInputTree.sha256) -match '^[0-9A-F]{64}$' -and
    ($lockedScopeNames -join ',') -eq ($expectedScopeNames -join ',')
Add-Check 'toolchain.lock-compile-input-tree' $compileTreeLocked 'The lock must cover the complete Compiler tree and all portable files read by compile_mod.py.'
$pythonLocked =
    $toolchainLock.python.selector -eq '-3.14' -and
    $toolchainLock.python.launcher.sha256 -match '^[0-9A-F]{64}$' -and
    $toolchainLock.python.interpreter.version -eq '3.14.3' -and
    $toolchainLock.python.interpreter.sha256 -match '^[0-9A-F]{64}$' -and
    @($toolchainLock.python.runtimeFiles).Count -ge 2
Add-Check 'toolchain.lock-python' $pythonLocked 'The Python launcher, resolved interpreter and runtime DLLs must be locked.'
$buildSyntaxDetail = if ($buildParseErrors.Count -eq 0) {
    'Build-NativeMod.ps1 parsed without PowerShell syntax errors.'
}
else {
    ($buildParseErrors | ForEach-Object Message) -join '; '
}
Add-Check 'build.syntax' ($buildParseErrors.Count -eq 0) $buildSyntaxDetail
$startProcessCommands = @($buildAst.FindAll({
    param($node)
    if ($node -isnot [System.Management.Automation.Language.CommandAst]) {
        return $false
    }
    $commandName = $node.GetCommandName()
    return -not [string]::IsNullOrWhiteSpace($commandName) -and
        $commandName -match '(?i)(?:^|\\)Start-Process$|^saps$|^start$'
}, $true))
Add-Check 'build.no-start-process' ($startProcessCommands.Count -eq 0) 'Build-NativeMod.ps1 must not use Start-Process under any circumstances.'

$installerPathReferences = @($buildAst.FindAll({
    param($node)
    return $node -is [System.Management.Automation.Language.VariableExpressionAst] -and
        $node.VariablePath.UserPath.Equals('installerPath', [StringComparison]::OrdinalIgnoreCase)
}, $true))
$installerExecutableLiterals = @($buildAst.FindAll({
    param($node)
    return $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
        $node.Value -match '(?i)(?:^|[\\/])windhawk_setup_offline\.exe$'
}, $true))
$processLaunchCommands = @($buildAst.FindAll({
    param($node)
    if ($node -isnot [System.Management.Automation.Language.CommandAst]) {
        return $false
    }
    $commandName = $node.GetCommandName()
    return -not [string]::IsNullOrWhiteSpace($commandName) -and
        ($commandName.Equals('Invoke-CapturedProcess', [StringComparison]::OrdinalIgnoreCase) -or
         $commandName -match '(?i)(?:^|\\)Start-Process$|^saps$|^start$')
}, $true))
$installerProcessLaunches = @($processLaunchCommands | Where-Object {
    $_.Extent.Text -match '(?i)\$installerPath\b|windhawk_setup_offline\.exe'
})
$installerDownloadReferences = @($buildAst.FindAll({
    param($node)
    return $node -is [System.Management.Automation.Language.MemberExpressionAst] -and
        $node.Extent.Text -match '(?i)\bsourceInstaller\.url\b'
}, $true))
$installerExecutionSurfaceAbsent =
    $installerPathReferences.Count -eq 0 -and
    $installerExecutableLiterals.Count -eq 0 -and
    $installerProcessLaunches.Count -eq 0 -and
    $installerDownloadReferences.Count -eq 0
Add-Check 'build.no-installer-process-launch' $installerExecutionSurfaceAbsent 'The installer path/executable must not exist in the build AST, reach a process-launch API, or be downloaded by the build.'
Test-NoPattern 'build.no-arbitrary-python-runner' $buildScript '\$PythonLauncher' 'The build must not accept an arbitrary runner that can bypass the pinned compiler script.'
Test-Pattern 'build.preprovisioned-portable-only' $buildScript "portableProvisioning = 'preprovisioned-validated'" 'The build must declare that only an existing, locked-hash-validated portable toolchain is accepted.'
Test-Pattern 'build.installer-execution-forbidden' $buildScript "installerExecution = 'forbidden'" 'Build evidence must state that installer execution is forbidden.'
Test-Pattern 'build.run-level-atomic-publish' $buildScript '\[System\.IO\.Directory\]::Move\(\$runStageDirectory, \$finalRunDirectory\)' 'A complete run must be published with one directory rename.'
Test-Pattern 'build.reparse-defense' $buildScript 'Assert-NoReparsePointsInPath' 'Build output and cleanup paths must reject reparse points.'

$phase2FaultInvocationPassed = $true
$phase2FaultInvocationDetail = 'Offline lifecycle fault lab completed.'
try {
    $null = & $phase2FaultRunnerPath
}
catch {
    $phase2FaultInvocationPassed = $false
    $phase2FaultInvocationDetail =
        "Offline lifecycle fault lab failed: $($_.Exception.Message)"
}
Add-Check `
    'phase2.fault-lab.mandatory-run' `
    $phase2FaultInvocationPassed `
    $phase2FaultInvocationDetail

$phase2FaultReceipt = $null
$phase2FaultReceiptLoaded = $false
try {
    if (-not (Test-Path -LiteralPath $phase2FaultReceiptPath -PathType Leaf)) {
        throw "Fault-lab receipt is missing: $phase2FaultReceiptPath"
    }
    $phase2FaultReceiptText =
        Get-Content -LiteralPath $phase2FaultReceiptPath -Raw
    $phase2FaultReceipt =
        $phase2FaultReceiptText | ConvertFrom-Json -Depth 100
    $phase2FaultReceiptLoaded = $true
}
catch {
    $phase2FaultReceiptDetail = $_.Exception.Message
}
if ($phase2FaultReceiptLoaded) {
    $phase2FaultReceiptDetail =
        "Loaded $phase2FaultReceiptPath"
}
Add-Check `
    'phase2.fault-lab.receipt-loaded' `
    $phase2FaultReceiptLoaded `
    $phase2FaultReceiptDetail

if ($phase2FaultReceiptLoaded) {
    $phase2SchemaValid = $false
    try {
        $phase2SchemaValid = $phase2FaultReceiptText |
            Test-Json -SchemaFile $phase2ReceiptSchemaPath
    }
    catch {
        $phase2SchemaValid = $false
    }
    Add-Check `
        'phase2.fault-lab.schema-valid' `
        $phase2SchemaValid `
        'The latest mandatory lifecycle receipt must validate against the committed schema.'

    $phase2ReceiptOutcome =
        $phase2FaultReceipt.result -eq 'passed' -and
        $phase2FaultReceipt.offlineEvidenceReady -and
        -not $phase2FaultReceipt.releaseReady -and
        -not $phase2FaultReceipt.activationPermitted -and
        $phase2FaultReceipt.liveExplorer -eq 'not-run' -and
        [int]$phase2FaultReceipt.summary.scenarioCount -eq
            @($phase2FaultReceipt.scenarios).Count -and
        [int]$phase2FaultReceipt.summary.passed -eq
            @($phase2FaultReceipt.scenarios).Count -and
        [int]$phase2FaultReceipt.summary.failed -eq 0 -and
        [int64]$phase2FaultReceipt.summary.retainedUnexplained -eq 0 -and
        [int64]$phase2FaultReceipt.summary.doubleRelease -eq 0 -and
        @($phase2FaultReceipt.scenarios |
            Where-Object { -not $_.passed }).Count -eq 0
    Add-Check `
        'phase2.fault-lab.outcome-boundary' `
        $phase2ReceiptOutcome `
        'Every lifecycle scenario must pass without unexplained or double-released resources, while release and live activation remain forbidden.'

    $phase2RequiredScenarioIds = @(
        'git.normal-close',
        'git.get-blocks-revoke',
        'git.revoke-fail-retry',
        'git.concurrent-close-single-owner',
        'git.cocreate-fail-retained',
        'git.com-changed-mode',
        'git.s-false-balanced',
        'git.advise-fail-maybe-advised',
        'git.unadvise-fail-before-revoke',
        'git.unadvise-ok-revoke-fail',
        'git.stale-generation',
        'git.repeat-close-noop',
        'git.worker-boundary-raii-containment',
        'git.provisional-commit-fail-quarantine',
        'git.provisional-initialized-unload-retry',
        'git.retired-owner-transfer-failure',
        'git.lease-capacity-retained',
        'git.fixed-reason-receipt-noalloc',
        'git.public-lock-failure-matrix',
        'git.unknown-cookie-fallback-receipt',
        'git.revoke-commit-protocol-failure',
        'git.subscription-lock-failure-matrix',
        'git.sequence-exhaustion-no-aba',
        'git.provisional-rollback-exception-retained',
        'git.provisional-register-throw-unknown-retained',
        'git.provisional-git-release-exception-contained',
        'git.provisional-quarantine-overflow-receipt',
        'git.proxy-final-release-before-lease',
        'git.proxy-release-exception-retains-lease',
        'git.get-external-com-exception-retains-lease',
        'git.cocreate-output-throw-retained',
        'git.site-holder-external-com-firewall',
        'git.internal-self-reference-noexcept',
        'git.revoke-external-com-exception-retained',
        'ui.fixed-capacity-snapshot-transaction',
        'ui.raw-handle-rollback',
        'ui.enum-callback-fixed-capacity',
        'ui.bootstrap-duplicate-observation',
        'ui.normal-clean',
        'ui.duplicate-init',
        'ui.window-gone-thread-alive',
        'ui.window-replacement',
        'ui.multiple-same-role-windows',
        'ui.destroy-window-failure',
        'ui.destroy-hook-abi-firewall',
        'ui.seal-late-clean',
        'ui.thread-id-reuse',
        'ui.dispatch-rejected',
        'ui.thread-exited',
        'ui.partial-clean-retry',
        'ui.timeout-late-clean',
        'ui.initialization-rollback',
        'ui.self-thread-cleanup',
        'ui.shutdown-cleanup',
        'ui.cleanup-callback-admission-failure',
        'ui.cleanup-retry-failure-terminal',
        'ui.capability-release-receipts',
        'dispatch.sync-success',
        'dispatch.timeout-cancel',
        'dispatch.callback-claims-before-cancel',
        'dispatch.claimed-before-guard-cancel-race',
        'dispatch.unhook-fail-retry',
        'dispatch.duplicate-callback',
        'dispatch.slot-conflict',
        'dispatch.callback-throws',
        'dispatch.target-exit',
        'dispatch.callback-before-unhook-retry',
        'dispatch.unhook-retry-before-callback',
        'dispatch.success-unhook-callback-inflight',
        'dispatch.hook-install-two-resource-receipt',
        'dispatch.adapter-late-slot',
        'dispatch.adapter-republish-monotonic',
        'dispatch.adapter-double-release-republish-saturation',
        'dispatch.callback-protocol-publication-failure',
        'dispatch.emergency-hook-exact-slot',
        'dispatch.foreign-abi-exception-firewall',
        'dispatch.reentrant-fail-fast',
        'dispatch.unhook-ticket-outside-lock',
        'module.permanent-pin-publication-race',
        'module.export-abi-firewall',
        'module.failclosed-before-pin-decision',
        'module.tap-com-boundary-faults',
        'module.noexcept-diagnostic-failures',
        'module.graphics-null-input-guards',
        'module.loader-reference-raii',
        'module.lockserver-balance',
        'module.xaml-brush-callback-firewalls',
        'module.projected-delegate-firewalls',
        'module.dormant-stats-raii',
        'module.pin-release-first-race'
    )
    $phase2ActualScenarioIds = @(
        $phase2FaultReceipt.scenarios |
            ForEach-Object { [string]$_.id }
    )
    $phase2UniqueActualScenarioIds = @(
        $phase2ActualScenarioIds |
            Sort-Object -Unique
    )
    $phase2MissingScenarioIds = @(
        $phase2RequiredScenarioIds |
            Where-Object { $_ -notin $phase2UniqueActualScenarioIds }
    )
    $phase2ExtraScenarioIds = @(
        $phase2UniqueActualScenarioIds |
            Where-Object { $_ -notin $phase2RequiredScenarioIds }
    )
    $phase2DuplicateScenarioIds = @(
        $phase2ActualScenarioIds |
            Group-Object |
            Where-Object { $_.Count -ne 1 } |
            ForEach-Object { $_.Name }
    )
    Add-Check `
        'phase2.fault-lab.exact-scenario-set' `
        ($phase2MissingScenarioIds.Count -eq 0 -and
         $phase2ExtraScenarioIds.Count -eq 0 -and
         $phase2DuplicateScenarioIds.Count -eq 0 -and
         $phase2ActualScenarioIds.Count -eq
            $phase2RequiredScenarioIds.Count) `
        ('The receipt scenario IDs must be the exact required set. Missing: ' +
         ($phase2MissingScenarioIds -join ', ') + '; extra: ' +
         ($phase2ExtraScenarioIds -join ', ') + '; duplicate: ' +
         ($phase2DuplicateScenarioIds -join ', '))

    $phase2ResourceEventErrors =
        [System.Collections.Generic.List[string]]::new()
    $phase2KnownRetainReasons = @(
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
    foreach ($scenario in @($phase2FaultReceipt.scenarios)) {
        $scenarioId = [string]$scenario.id
        $resourceEventsProperty =
            $scenario.PSObject.Properties['resourceEvents']
        if ($null -eq $resourceEventsProperty) {
            $phase2ResourceEventErrors.Add(
                "${scenarioId}: resourceEvents missing"
            )
            continue
        }
        $resourceEvents = @($resourceEventsProperty.Value)
        $createCount = @(
            $resourceEvents |
                Where-Object { $_.action -ceq 'create' }
        ).Count
        $releaseCount = @(
            $resourceEvents |
                Where-Object { $_.action -ceq 'release' }
        ).Count
        $retainCount = @(
            $resourceEvents |
                Where-Object { $_.action -ceq 'retain' }
        ).Count
        foreach ($resourceEvent in $resourceEvents) {
            $eventAction = [string]$resourceEvent.action
            $eventReason = [string]$resourceEvent.reasonCode
            if ($eventAction -ceq 'retain') {
                if ($eventReason -notin $phase2KnownRetainReasons) {
                    $phase2ResourceEventErrors.Add(
                        "${scenarioId}: retained resource has an " +
                        "unknown or empty reason $eventReason"
                    )
                }
            }
            elseif ($eventReason -cne 'none') {
                $phase2ResourceEventErrors.Add(
                    "${scenarioId}: non-retain event has retain reason " +
                    $eventReason
                )
            }
        }
        if ($createCount -ne
                [int64]$scenario.resourceAccounting.created -or
            $releaseCount -ne
                [int64]$scenario.resourceAccounting.released -or
            $retainCount -ne
                [int64]$scenario.resourceAccounting.retained) {
            $phase2ResourceEventErrors.Add(
                "${scenarioId}: event counts do not match accounting"
            )
        }
        foreach ($resourceGroup in @(
            $resourceEvents |
                Group-Object -Property resourceId
        )) {
            $groupEvents = @($resourceGroup.Group)
            $resourceKinds = @(
                $groupEvents |
                    ForEach-Object { [string]$_.resourceKind } |
                    Sort-Object -Unique
            )
            $groupCreates = @(
                $groupEvents |
                    Where-Object { $_.action -ceq 'create' }
            ).Count
            $groupTerminals = @(
                $groupEvents |
                    Where-Object {
                        $_.action -ceq 'release' -or
                        $_.action -ceq 'retain'
                    }
            ).Count
            if ([string]::IsNullOrWhiteSpace(
                    [string]$resourceGroup.Name) -or
                $resourceKinds.Count -ne 1 -or
                $groupCreates -ne 1 -or
                $groupTerminals -ne 1 -or
                [string]$groupEvents[0].action -cne 'create' -or
                [string]$groupEvents[-1].action -notin
                    @('release', 'retain')) {
                $phase2ResourceEventErrors.Add(
                    "${scenarioId}: resource $($resourceGroup.Name) " +
                    'must have one kind, one create, and one terminal event'
                )
            }
        }
    }
    Add-Check `
        'phase2.fault-lab.resource-event-accounting' `
        ($phase2ResourceEventErrors.Count -eq 0) `
        ('Each resource must have one explicit create and one release or ' +
         'reasoned retain event in order, with accounting kept as an exact ' +
         'summary. Errors: ' +
         ($phase2ResourceEventErrors -join '; '))

    $phase2ScenarioProductionGateMap = [ordered]@{
        'git.normal-close' = @('phase2.git.authoritative-retryable-core')
        'git.get-blocks-revoke' = @('phase2.git.authoritative-retryable-core')
        'git.revoke-fail-retry' = @('phase2.git.authoritative-retryable-core')
        'git.concurrent-close-single-owner' = @('phase2.git.authoritative-retryable-core')
        'git.cocreate-fail-retained' = @('phase2.git.initialized-provisional-retry-and-atomic-retain')
        'git.com-changed-mode' = @('phase2.git.production-adapter')
        'git.s-false-balanced' = @('phase2.git.production-adapter')
        'git.advise-fail-maybe-advised' = @('phase2.git.subscription-external-uncertainty-latched')
        'git.unadvise-fail-before-revoke' = @('phase2.git.subscription-external-uncertainty-latched')
        'git.unadvise-ok-revoke-fail' = @('phase2.git.subscription-external-uncertainty-latched')
        'git.stale-generation' = @('phase2.git.authoritative-retryable-core')
        'git.repeat-close-noop' = @('phase2.git.authoritative-retryable-core')
        'git.worker-boundary-raii-containment' = @('styler.visual-tree-workers-raii-firewall')
        'git.provisional-commit-fail-quarantine' = @('phase2.git.initialized-provisional-retry-and-atomic-retain')
        'git.provisional-initialized-unload-retry' = @('phase2.git.initialized-provisional-retry-and-atomic-retain')
        'git.retired-owner-transfer-failure' = @('phase2.git.retired-owner-noalloc-transfer')
        'git.lease-capacity-retained' = @('phase2.git.fixed-capacity-noexcept-receipt')
        'git.fixed-reason-receipt-noalloc' = @('phase2.git.fixed-capacity-noexcept-receipt')
        'git.public-lock-failure-matrix' = @('phase2.git.authoritative-retryable-core')
        'git.unknown-cookie-fallback-receipt' = @('phase2.git.fixed-capacity-noexcept-receipt')
        'git.revoke-commit-protocol-failure' = @('phase2.git.authoritative-retryable-core')
        'git.subscription-lock-failure-matrix' = @('phase2.git.subscription-external-uncertainty-latched')
        'git.sequence-exhaustion-no-aba' = @('phase2.git.fixed-capacity-noexcept-receipt')
        'git.provisional-rollback-exception-retained' = @('phase2.git.initialized-provisional-retry-and-atomic-retain')
        'git.provisional-register-throw-unknown-retained' = @('phase2.git.initialized-provisional-retry-and-atomic-retain')
        'git.provisional-git-release-exception-contained' = @('phase2.git.no-implicit-git-com-release')
        'git.provisional-quarantine-overflow-receipt' = @('phase2.git.fixed-capacity-noexcept-receipt')
        'git.proxy-final-release-before-lease' = @('phase2.git.external-com-noexcept-firewall')
        'git.proxy-release-exception-retains-lease' = @('phase2.git.external-com-noexcept-firewall')
        'git.get-external-com-exception-retains-lease' = @('phase2.git.external-com-noexcept-firewall')
        'git.cocreate-output-throw-retained' = @('phase2.git.external-com-noexcept-firewall')
        'git.site-holder-external-com-firewall' = @('phase2.git.tap-site-external-com-firewall')
        'git.internal-self-reference-noexcept' = @('phase2.git.internal-self-reference-noexcept-boundary')
        'git.revoke-external-com-exception-retained' = @('phase2.git.external-com-noexcept-firewall')
        'ui.fixed-capacity-snapshot-transaction' = @('phase2.ui-thread.fixed-snapshot-and-handle-rollback')
        'ui.raw-handle-rollback' = @('phase2.ui-thread.fixed-snapshot-and-handle-rollback')
        'ui.enum-callback-fixed-capacity' = @('phase2.ui-thread.enum-boundary-and-bootstrap-dedup')
        'ui.bootstrap-duplicate-observation' = @('phase2.ui-thread.enum-boundary-and-bootstrap-dedup')
        'ui.normal-clean' = @('phase2.ui-thread.snapshot-deadline-and-terminal-receipts')
        'ui.duplicate-init' = @('phase2.ui-thread.transaction-and-window-observation')
        'ui.window-gone-thread-alive' = @('phase2.ui-thread.capability-record-no-hwnd')
        'ui.window-replacement' = @('phase2.ui-thread.transaction-and-window-observation')
        'ui.multiple-same-role-windows' = @('phase2.ui-thread.transaction-and-window-observation')
        'ui.destroy-window-failure' = @('phase2.ui-thread.transaction-and-window-observation')
        'ui.destroy-hook-abi-firewall' = @('styler.foreign-abi-exception-firewalls')
        'ui.seal-late-clean' = @('phase2.ui-thread.snapshot-deadline-and-terminal-receipts')
        'ui.thread-id-reuse' = @('phase2.ui-thread.capability-record-no-hwnd')
        'ui.dispatch-rejected' = @('phase2.ui-thread.snapshot-deadline-and-terminal-receipts')
        'ui.thread-exited' = @('phase2.ui-thread.snapshot-deadline-and-terminal-receipts')
        'ui.partial-clean-retry' = @('phase2.ui-thread.snapshot-deadline-and-terminal-receipts')
        'ui.timeout-late-clean' = @('phase2.ui-thread.snapshot-deadline-and-terminal-receipts')
        'ui.initialization-rollback' = @('phase2.ui-thread.fixed-snapshot-and-handle-rollback')
        'ui.self-thread-cleanup' = @('phase2.ui-thread.snapshot-deadline-and-terminal-receipts')
        'ui.shutdown-cleanup' = @('phase2.ui-thread.snapshot-deadline-and-terminal-receipts')
        'ui.cleanup-callback-admission-failure' = @('phase2.ui-thread.callback-admission-retained')
        'ui.cleanup-retry-failure-terminal' = @('phase2.ui-thread.snapshot-deadline-and-terminal-receipts')
        'ui.capability-release-receipts' = @('phase2.ui-thread.capability-record-no-hwnd')
        'dispatch.sync-success' = @('phase2.dispatch.fixed-slot-accounting-and-receipts')
        'dispatch.timeout-cancel' = @('phase2.dispatch.fixed-slot-accounting-and-receipts')
        'dispatch.callback-claims-before-cancel' = @('phase2.dispatch.fixed-slot-accounting-and-receipts')
        'dispatch.claimed-before-guard-cancel-race' = @('phase2.dispatch.fixed-slot-accounting-and-receipts')
        'dispatch.unhook-fail-retry' = @('phase2.dispatch.external-api-outside-lock-ticket')
        'dispatch.duplicate-callback' = @('phase2.dispatch.fixed-slot-accounting-and-receipts')
        'dispatch.slot-conflict' = @('phase2.dispatch.fixed-slot-accounting-and-receipts')
        'dispatch.callback-throws' = @('phase2.dispatch.fixed-slot-accounting-and-receipts')
        'dispatch.target-exit' = @('phase2.dispatch.fixed-slot-accounting-and-receipts')
        'dispatch.callback-before-unhook-retry' = @('phase2.dispatch.external-api-outside-lock-ticket')
        'dispatch.unhook-retry-before-callback' = @('phase2.dispatch.external-api-outside-lock-ticket')
        'dispatch.success-unhook-callback-inflight' = @('phase2.dispatch.external-api-outside-lock-ticket')
        'dispatch.hook-install-two-resource-receipt' = @('phase2.dispatch.fixed-slot-accounting-and-receipts')
        'dispatch.adapter-late-slot' = @('phase2.dispatch.fixed-slot-accounting-and-receipts')
        'dispatch.adapter-republish-monotonic' = @('phase2.dispatch.fixed-slot-accounting-and-receipts')
        'dispatch.adapter-double-release-republish-saturation' = @('phase2.dispatch.fixed-slot-accounting-and-receipts')
        'dispatch.callback-protocol-publication-failure' = @(
            'phase2.dispatch.claim-and-log-unlocked-side-effects',
            'phase2.dispatch.fixed-slot-accounting-and-receipts'
        )
        'dispatch.emergency-hook-exact-slot' = @('phase2.dispatch.external-api-outside-lock-ticket')
        'dispatch.foreign-abi-exception-firewall' = @('styler.foreign-abi-exception-firewalls')
        'dispatch.reentrant-fail-fast' = @('phase2.dispatch.external-api-outside-lock-ticket')
        'dispatch.unhook-ticket-outside-lock' = @('phase2.dispatch.external-api-outside-lock-ticket')
        'module.permanent-pin-publication-race' = @('styler.permanent-pin-linearized-release')
        'module.export-abi-firewall' = @('styler.module-export-and-inject-firewalls')
        'module.failclosed-before-pin-decision' = @('phase2.failclosed-before-pin-decision')
        'module.tap-com-boundary-faults' = @('styler.tap-com-total-firewall')
        'module.noexcept-diagnostic-failures' = @('styler.noexcept-diagnostics-contained')
        'module.graphics-null-input-guards' = @('styler.graphics-null-input-guards')
        'module.loader-reference-raii' = @('styler.module-reference-raii')
        'module.lockserver-balance' = @('styler.factory-lockserver-balanced')
        'module.xaml-brush-callback-firewalls' = @('styler.xaml-brush-callback-firewalls')
        'module.projected-delegate-firewalls' = @('styler.projected-delegate-firewalls')
        'module.dormant-stats-raii' = @('styler.dormant-stats-raii')
        'module.pin-release-first-race' = @('styler.permanent-pin-linearized-release')
    }

    $phase2ScenarioGateErrors =
        [System.Collections.Generic.List[string]]::new()
    $phase2MappedScenarioIds = @(
        $phase2ScenarioProductionGateMap.Keys
    )
    foreach ($scenarioId in $phase2RequiredScenarioIds) {
        if ($scenarioId -notin $phase2MappedScenarioIds) {
            $phase2ScenarioGateErrors.Add(
                "${scenarioId}: production gate mapping missing"
            )
            continue
        }
        $mappedGates = @(
            $phase2ScenarioProductionGateMap[$scenarioId]
        )
        if ($mappedGates.Count -eq 0) {
            $phase2ScenarioGateErrors.Add(
                "${scenarioId}: production gate mapping empty"
            )
            continue
        }
        if (@($mappedGates | Sort-Object -Unique).Count -ne
            $mappedGates.Count) {
            $phase2ScenarioGateErrors.Add(
                "${scenarioId}: production gate mapping duplicated"
            )
        }
        foreach ($gateName in $mappedGates) {
            if ($gateName -notmatch
                '^(?:styler\.|phase2\.(?:git|ui-thread|dispatch|protocol|failclosed))') {
                $phase2ScenarioGateErrors.Add(
                    "${scenarioId}: non-production gate $gateName"
                )
                continue
            }
            $gateRelevant =
                if ($scenarioId.StartsWith(
                        'git.', [StringComparison]::Ordinal)) {
                    $gateName -match '^phase2\.git\.' -or
                    $gateName -ceq
                        'styler.visual-tree-workers-raii-firewall'
                }
                elseif ($scenarioId.StartsWith(
                        'ui.', [StringComparison]::Ordinal)) {
                    $gateName -match '^phase2\.ui-thread\.' -or
                    $gateName -ceq
                        'styler.foreign-abi-exception-firewalls'
                }
                elseif ($scenarioId.StartsWith(
                        'dispatch.', [StringComparison]::Ordinal)) {
                    $gateName -match '^phase2\.dispatch\.' -or
                    $gateName -ceq
                        'styler.foreign-abi-exception-firewalls'
                }
                else {
                    $gateName -match '^styler\.' -or
                    $gateName -ceq
                        'phase2.failclosed-before-pin-decision'
                }
            if (-not $gateRelevant) {
                $phase2ScenarioGateErrors.Add(
                    "${scenarioId}: unrelated production gate $gateName"
                )
                continue
            }
            $gateChecks = @(
                $checks |
                    Where-Object { $_.name -ceq $gateName }
            )
            if ($gateChecks.Count -ne 1) {
                $phase2ScenarioGateErrors.Add(
                    "${scenarioId}: gate $gateName missing or duplicated"
                )
            }
            elseif (-not $gateChecks[0].passed) {
                $phase2ScenarioGateErrors.Add(
                    "${scenarioId}: gate $gateName failed"
                )
            }
        }
    }
    foreach ($mappedId in $phase2MappedScenarioIds) {
        if ($mappedId -notin $phase2RequiredScenarioIds) {
            $phase2ScenarioGateErrors.Add(
                "${mappedId}: mapping has no required scenario"
            )
        }
    }
    Add-Check `
        'phase2.fault-lab.production-gate-map' `
        ($phase2ScenarioGateErrors.Count -eq 0) `
        ('Every exact scenario ID must map to at least one existing, unique, ' +
         'passing production static gate. Errors: ' +
         ($phase2ScenarioGateErrors -join '; '))

    $phase2SourceIdentityErrors =
        [System.Collections.Generic.List[string]]::new()
    $phase2ReceiptSourceProperties =
        $phase2FaultReceipt.sourceIdentity.PSObject.Properties
    $phase2ReceiptSourceKeys = @(
        $phase2ReceiptSourceProperties |
            ForEach-Object { $_.Name }
    )
    $phase2ExpectedSourceKeys = @(
        $phase2ExpectedSourceIdentity.Keys
    )
    foreach ($missingKey in @(
        $phase2ExpectedSourceKeys |
            Where-Object { $_ -notin $phase2ReceiptSourceKeys }
    )) {
        $phase2SourceIdentityErrors.Add(
            "${missingKey}: descriptor missing"
        )
    }
    foreach ($extraKey in @(
        $phase2ReceiptSourceKeys |
            Where-Object { $_ -notin $phase2ExpectedSourceKeys }
    )) {
        $phase2SourceIdentityErrors.Add(
            "${extraKey}: unexpected descriptor"
        )
    }
    $phase2SeenSourcePaths =
        [System.Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal
        )
    $phase2SeenSourceDescriptors =
        [System.Collections.Generic.HashSet[string]]::new(
            [StringComparer]::OrdinalIgnoreCase
        )
    foreach ($entry in $phase2ExpectedSourceIdentity.GetEnumerator()) {
        $receiptProperty =
            $phase2ReceiptSourceProperties[$entry.Key]
        if ($null -eq $receiptProperty) {
            continue
        }
        $descriptor = $receiptProperty.Value
        $relativePath = [string]$descriptor.relativePath
        if ($relativePath -cne [string]$entry.Value.relativePath) {
            $phase2SourceIdentityErrors.Add(
                "$($entry.Key): expected $($entry.Value.relativePath), " +
                "found $relativePath"
            )
        }
        if (-not $phase2SeenSourcePaths.Add($relativePath)) {
            $phase2SourceIdentityErrors.Add(
                "$($entry.Key): duplicate relativePath $relativePath"
            )
        }
        $descriptorSignature = '{0}|{1}|{2}' -f
            $relativePath,
            [int64]$descriptor.size,
            ([string]$descriptor.sha256).ToUpperInvariant()
        if (-not $phase2SeenSourceDescriptors.Add(
                $descriptorSignature)) {
            $phase2SourceIdentityErrors.Add(
                "$($entry.Key): duplicate descriptor"
            )
        }
        $verified = Test-EvidenceFile `
            -Descriptor $descriptor `
            -AllowedRoot $root
        if (-not $verified.passed) {
            $phase2SourceIdentityErrors.Add(
                "$($entry.Key): $($verified.detail)"
            )
        }
        elseif (-not ([string]$verified.path).Equals(
                [string]$entry.Value.fullPath,
                [StringComparison]::OrdinalIgnoreCase)) {
            $phase2SourceIdentityErrors.Add(
                "$($entry.Key): descriptor resolved to the wrong file"
            )
        }
    }
    Add-Check `
        'phase2.fault-lab.current-source-identity' `
        ($phase2SourceIdentityErrors.Count -eq 0 -and
         $phase2ReceiptSourceKeys.Count -eq
            $phase2ExpectedSourceKeys.Count -and
         $phase2SeenSourcePaths.Count -eq
            $phase2ExpectedSourceKeys.Count -and
         $phase2SeenSourceDescriptors.Count -eq
            $phase2ExpectedSourceKeys.Count) `
        ('The keyed fault receipt must bind six distinct exact current files ' +
         '(M1, protocol, harness, runner, schema, and TestProject) by path, ' +
         'size, and SHA-256. Errors: ' +
         ($phase2SourceIdentityErrors -join '; '))
}
else {
    Add-Check `
        'phase2.fault-lab.evidence-closure' `
        $false `
        'The mandatory fault-lab receipt could not be loaded or validated.'
}

$sourceHashes = @{
    'jarvis-native-taskbar' = (Get-FileHash -LiteralPath $stylerPath -Algorithm SHA256).Hash
    'jarvis-taskbar-icon-size' = (Get-FileHash -LiteralPath $iconSizePath -Algorithm SHA256).Hash
}
$currentBuildScriptSha256 = (Get-FileHash -LiteralPath $buildScriptPath -Algorithm SHA256).Hash
$currentTestScriptSha256 = (Get-FileHash -LiteralPath $testScriptPath -Algorithm SHA256).Hash
$currentToolchainLockSha256 = (Get-FileHash -LiteralPath $toolchainLockPath -Algorithm SHA256).Hash
$receiptSchemaCurrent = $nativeBuildReceipt.schemaVersion -eq 3
Add-Check 'receipt.schema-v3' $receiptSchemaCurrent 'Committed native build evidence must be generated with schema v3.'

if ($receiptSchemaCurrent) {
    try {
        $receiptNoLiveClaim =
            -not $nativeBuildReceipt.activationPermitted -and
            $nativeBuildReceipt.liveExplorer -eq 'not-run' -and
            $nativeBuildReceipt.offlineEvidenceReady -and
            -not $nativeBuildReceipt.releaseReady
        Add-Check 'receipt.no-live-claim' $receiptNoLiveClaim 'Offline compilation must never imply activation or live validation.'

        $identityMatches =
            $nativeBuildReceipt.generatedBy -eq 'scripts/Build-NativeMod.ps1' -and
            $nativeBuildReceipt.evidence.buildScript.relativePath -eq 'scripts/Build-NativeMod.ps1' -and
            $nativeBuildReceipt.evidence.buildScript.sha256 -eq $currentBuildScriptSha256 -and
            $nativeBuildReceipt.evidence.testScript.relativePath -eq 'scripts/Test-Project.ps1' -and
            $nativeBuildReceipt.evidence.testScript.sha256 -eq $currentTestScriptSha256 -and
            $nativeBuildReceipt.evidence.toolchainLock.relativePath -eq 'config/toolchain-lock.json' -and
            $nativeBuildReceipt.evidence.toolchainLock.sha256 -eq $currentToolchainLockSha256
        Add-Check 'receipt.script-and-lock-identity' $identityMatches 'Receipt must bind the exact current build script, test script and toolchain lock.'

        $receiptTree = $nativeBuildReceipt.toolchain.compileInputTree
        $receiptToolchainPropertyNames = @($nativeBuildReceipt.toolchain.PSObject.Properties.Name)
        $receiptToolchainMatches =
            $nativeBuildReceipt.toolchain.windhawkVersion -eq $toolchainLock.windhawkVersion -and
            $nativeBuildReceipt.toolchain.windhawkCommit -eq $toolchainLock.windhawkCommit -and
            $receiptToolchainPropertyNames -contains 'sourceInstallerLockSha256' -and
            $receiptToolchainPropertyNames -contains 'sourceInstallerLockSigner' -and
            $receiptToolchainPropertyNames -contains 'installerExecution' -and
            $receiptToolchainPropertyNames -contains 'portableProvisioning' -and
            $nativeBuildReceipt.toolchain.sourceInstallerLockSha256 -eq $toolchainLock.sourceInstaller.sha256 -and
            $nativeBuildReceipt.toolchain.sourceInstallerLockSigner -eq $toolchainLock.sourceInstaller.signerSubject -and
            $nativeBuildReceipt.toolchain.installerExecution -eq 'forbidden' -and
            $nativeBuildReceipt.toolchain.portableProvisioning -eq 'preprovisioned-validated' -and
            $receiptToolchainPropertyNames -notcontains 'installerSha256' -and
            $receiptToolchainPropertyNames -notcontains 'installerSigner' -and
            $nativeBuildReceipt.toolchain.compilerScriptSha256 -eq $toolchainLock.compilerScript.sha256 -and
            $nativeBuildReceipt.toolchain.pythonExecutableSha256 -eq $toolchainLock.python.interpreter.sha256 -and
            $nativeBuildReceipt.toolchain.pythonLauncherSha256 -eq $toolchainLock.python.launcher.sha256 -and
            $receiptTree.algorithm -eq $toolchainLock.compileInputTree.algorithm -and
            [int64]$receiptTree.fileCount -eq [int64]$toolchainLock.compileInputTree.fileCount -and
            [int64]$receiptTree.bytes -eq [int64]$toolchainLock.compileInputTree.bytes -and
            $receiptTree.sha256 -eq $toolchainLock.compileInputTree.sha256
        Add-Check 'receipt.toolchain-identity' $receiptToolchainMatches 'Receipt must bind the full compiler-input aggregate and pinned Python identity.'

        $expectedRunPrefix = "artifacts/native/runs/$($nativeBuildReceipt.runId)/"
        Add-Check 'receipt.run-summary-path' ($nativeBuildReceipt.runSummary.relativePath -eq "${expectedRunPrefix}run-summary.json") 'Committed evidence must point to the canonical immutable run directory.'
        $runSummaryEvidence = Test-EvidenceFile -Descriptor $nativeBuildReceipt.runSummary -AllowedRoot $artifactsRoot
        Add-Check 'receipt.run-summary-file' $runSummaryEvidence.passed $runSummaryEvidence.detail
        $runSummary = $null
        if ($runSummaryEvidence.passed) {
            $runSummary = Get-Content -LiteralPath $runSummaryEvidence.path -Raw | ConvertFrom-Json
        }
        $runSummaryMatches =
            $null -ne $runSummary -and
            $runSummary.schemaVersion -eq 3 -and
            $runSummary.runId -eq $nativeBuildReceipt.runId -and
            $runSummary.status -eq 'complete' -and
            $runSummary.canonicalFullRun -and
            -not $runSummary.activationPermitted -and
            $runSummary.liveExplorer -eq 'not-run' -and
            $runSummary.evidence.buildScript.sha256 -eq $currentBuildScriptSha256 -and
            $runSummary.evidence.testScript.sha256 -eq $currentTestScriptSha256 -and
            $runSummary.evidence.toolchainLock.sha256 -eq $currentToolchainLockSha256 -and
            (Get-NormalizedJson $runSummary.toolchain) -eq (Get-NormalizedJson $nativeBuildReceipt.toolchain)
        Add-Check 'receipt.run-summary-content' $runSummaryMatches 'The hashed run summary must describe one complete canonical offline run.'

        $committedModuleIds = @($nativeBuildReceipt.modules | ForEach-Object id | Sort-Object)
        $summaryModuleIds = if ($null -ne $runSummary) { @($runSummary.modules | ForEach-Object id | Sort-Object) } else { @() }
        $moduleSetsMatch =
            ($committedModuleIds -join ',') -eq 'jarvis-native-taskbar,jarvis-taskbar-icon-size' -and
            ($summaryModuleIds -join ',') -eq ($committedModuleIds -join ',')
        Add-Check 'receipt.module-set' $moduleSetsMatch 'Committed receipt and run summary must contain exactly the reviewed module allowlist.'

        foreach ($moduleId in $sourceHashes.Keys) {
            $committedModules = @($nativeBuildReceipt.modules | Where-Object id -eq $moduleId)
            $summaryModules = if ($null -ne $runSummary) { @($runSummary.modules | Where-Object id -eq $moduleId) } else { @() }
            $moduleDescriptorsMatch =
                $committedModules.Count -eq 1 -and
                $summaryModules.Count -eq 1 -and
                (Get-NormalizedJson $committedModules[0]) -eq (Get-NormalizedJson $summaryModules[0])
            Add-Check "receipt.$moduleId.descriptor" $moduleDescriptorsMatch 'Committed and run-summary module evidence must be identical.'
            if (-not $moduleDescriptorsMatch) {
                continue
            }

            $moduleEvidence = $committedModules[0]
            $expectedSupportingSources = @(
                if ($moduleId -eq 'jarvis-native-taskbar') {
                    [pscustomobject]@{
                        path = 'mods/common/jarvis-resource-protocol.hpp'
                        includeFileName = 'jarvis-resource-protocol.hpp'
                        sha256 = (Get-FileHash `
                            -LiteralPath $phase2ProtocolPath `
                            -Algorithm SHA256).Hash
                    }
                }
            )
            $moduleEvidencePropertyNames =
                @($moduleEvidence.PSObject.Properties.Name)
            $actualSupportingSources = @(
                if (
                    $moduleEvidencePropertyNames -contains
                        'supportingSources'
                ) {
                    $moduleEvidence.supportingSources
                }
            )
            $supportingSourcesMatch =
                $actualSupportingSources.Count -eq
                    $expectedSupportingSources.Count
            if ($supportingSourcesMatch) {
                for (
                    $supportingIndex = 0;
                    $supportingIndex -lt
                        $expectedSupportingSources.Count;
                    ++$supportingIndex
                ) {
                    $expectedSupporting =
                        $expectedSupportingSources[$supportingIndex]
                    $actualSupporting =
                        $actualSupportingSources[$supportingIndex]
                    if (
                        $actualSupporting.path -ne
                            $expectedSupporting.path -or
                        $actualSupporting.includeFileName -ne
                            $expectedSupporting.includeFileName -or
                        -not ([string]$actualSupporting.sha256).Equals(
                            [string]$expectedSupporting.sha256,
                            [StringComparison]::OrdinalIgnoreCase)
                    ) {
                        $supportingSourcesMatch = $false
                    }
                }
            }
            Add-Check `
                "receipt.$moduleId.supporting-sources" `
                $supportingSourcesMatch `
                'Supporting-source descriptors must exactly bind the shared lifecycle protocol for M1 and remain empty for M2.'

            $modulePathsCanonical = @(
                @(
                    $moduleEvidence.sourceSnapshot.relativePath,
                    $moduleEvidence.artifact.relativePath,
                    $moduleEvidence.compileLog.relativePath,
                    $moduleEvidence.moduleReceipt.relativePath
                    @(
                        $actualSupportingSources |
                            ForEach-Object {
                                $_.snapshot.relativePath
                            }
                    )
                ) | Where-Object { -not ([string]$_).StartsWith($expectedRunPrefix, [StringComparison]::Ordinal) }
            )
            Add-Check "receipt.$moduleId.canonical-paths" ($modulePathsCanonical.Count -eq 0) 'Every evidence file must live under the same immutable run directory.'
            $sourceMatches =
                $moduleEvidence.architecture -eq 'amd64' -and
                $moduleEvidence.sourceSha256 -eq $sourceHashes[$moduleId] -and
                $moduleEvidence.result.exitCode -eq 0 -and
                $moduleEvidence.result.warningCount -eq 0 -and
                $moduleEvidence.result.errorCount -eq 0
            Add-Check "receipt.$moduleId.source-result" $sourceMatches 'Module evidence must match the current source and a warning-free build.'

            $evidenceFiles = [ordered]@{
                sourceSnapshot = $moduleEvidence.sourceSnapshot
                artifact = $moduleEvidence.artifact
                compileLog = $moduleEvidence.compileLog
                moduleReceipt = $moduleEvidence.moduleReceipt
            }
            $verifiedFiles = @{}
            foreach ($entry in $evidenceFiles.GetEnumerator()) {
                $verified = Test-EvidenceFile -Descriptor $entry.Value -AllowedRoot $artifactsRoot
                $verifiedFiles[$entry.Key] = $verified
                Add-Check "receipt.$moduleId.file.$($entry.Key)" $verified.passed $verified.detail
            }
            for (
                $supportingIndex = 0;
                $supportingIndex -lt
                    $actualSupportingSources.Count;
                ++$supportingIndex
            ) {
                $supportingEvidence = Test-EvidenceFile `
                    -Descriptor (
                        $actualSupportingSources[$supportingIndex].snapshot
                    ) `
                    -AllowedRoot $artifactsRoot
                Add-Check `
                    "receipt.$moduleId.file.supporting-$supportingIndex" `
                    $supportingEvidence.passed `
                    $supportingEvidence.detail
            }

            if ($verifiedFiles.moduleReceipt.passed) {
                $moduleReceipt = Get-Content -LiteralPath $verifiedFiles.moduleReceipt.path -Raw | ConvertFrom-Json
                $moduleReceiptModulePropertyNames =
                    @($moduleReceipt.module.PSObject.Properties.Name)
                $moduleReceiptSupportingSources = @(
                    if (
                        $moduleReceiptModulePropertyNames -contains
                            'supportingSources'
                    ) {
                        $moduleReceipt.module.supportingSources
                    }
                )
                $moduleReceiptMatches =
                    $moduleReceipt.schemaVersion -eq 3 -and
                    $moduleReceipt.runId -eq $nativeBuildReceipt.runId -and
                    $moduleReceipt.module.id -eq $moduleId -and
                    $moduleReceipt.module.architecture -eq 'amd64' -and
                    $moduleReceipt.module.sourceSha256 -eq $sourceHashes[$moduleId] -and
                    $moduleReceipt.evidence.buildScript.sha256 -eq $currentBuildScriptSha256 -and
                    $moduleReceipt.evidence.testScript.sha256 -eq $currentTestScriptSha256 -and
                    $moduleReceipt.evidence.toolchainLock.sha256 -eq $currentToolchainLockSha256 -and
                    (Get-NormalizedJson $moduleReceipt.module.sourceSnapshot) -eq (Get-NormalizedJson $moduleEvidence.sourceSnapshot) -and
                    (Get-NormalizedJson $moduleReceiptSupportingSources) -eq (Get-NormalizedJson $actualSupportingSources) -and
                    (Get-NormalizedJson $moduleReceipt.toolchain) -eq (Get-NormalizedJson $nativeBuildReceipt.toolchain) -and
                    (Get-NormalizedJson $moduleReceipt.output) -eq (Get-NormalizedJson $moduleEvidence.artifact) -and
                    (Get-NormalizedJson $moduleReceipt.result.compileLog) -eq (Get-NormalizedJson $moduleEvidence.compileLog) -and
                    $moduleReceipt.result.exitCode -eq 0 -and
                    $moduleReceipt.result.warningCount -eq 0 -and
                    $moduleReceipt.result.errorCount -eq 0 -and
                    -not $moduleReceipt.activationPermitted -and
                    $moduleReceipt.liveExplorer -eq 'not-run'
                Add-Check "receipt.$moduleId.module-receipt" $moduleReceiptMatches 'Hashed per-module receipt must bind the same source, artifact, log, scripts and toolchain lock.'
            }

            $artifactPeMatches =
                $moduleEvidence.artifact.pe.machine -eq '0x8664' -and
                $moduleEvidence.artifact.pe.optionalHeaderMagic -eq '0x020B' -and
                $moduleEvidence.artifact.pe.isDll -and
                $moduleEvidence.artifact.pe.isExecutableImage -and
                $moduleEvidence.artifact.pe.sizeOfOptionalHeader -eq 240 -and
                @($moduleEvidence.artifact.windhawkExports | Where-Object { $_ -eq 'InternalWhModPtr' }).Count -eq 1 -and
                @($moduleEvidence.artifact.windhawkExports | Where-Object { $_ -eq '_Z10Wh_ModInitv' }).Count -eq 1
            Add-Check "receipt.$moduleId.strict-pe" $artifactPeMatches 'Artifact must be a strictly parsed AMD64 executable DLL with exact concrete Windhawk exports.'
        }
    }
    catch {
        Add-Check 'receipt.validation-completed' $false "Receipt evidence validation threw: $($_.Exception.Message)"
    }
}
else {
    Add-Check 'receipt.evidence-closure' $false 'Run a canonical all-module build with the current scripts to replace the stale receipt.'
}

Add-Check 'safety.explicit-live-authorization' ($safetyContract.Contains('user explicitly approves') -and $safetyContract.Contains('Do not inject a module')) 'Repository instructions must forbid live injection without explicit current-task authorization.'
Add-Check 'recovery.flag-not-unload' ($recovery.Contains('不是结束进程的按钮') -and $recovery.Contains('不允许') -and $recovery.Contains('物理卸载')) 'Recovery docs must distinguish the emergency flag from DLL unload or process termination.'

$piAgentHostRoot =
    Join-Path $root 'src\common\Jarvis.PiAgentHost'
$piAgentPackagePath =
    Join-Path $piAgentHostRoot 'package.json'
$piAgentPackage =
    Get-Content -LiteralPath $piAgentPackagePath -Raw |
        ConvertFrom-Json -Depth 20
$piAgentRuntimeSource = @(
    Get-ChildItem `
        -LiteralPath (Join-Path $piAgentHostRoot 'src') `
        -File `
        -Filter '*.mjs' |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$piAgentDependencies =
    @($piAgentPackage.dependencies.PSObject.Properties)
$piAgentSidecarOnly =
    $piAgentPackage.name -eq '@jarvisv2/pi-agent-host' -and
    $piAgentPackage.private -eq $true -and
    $piAgentPackage.type -eq 'module' -and
    $piAgentDependencies.Count -eq 2 -and
    $piAgentPackage.dependencies.'@earendil-works/pi-ai' -eq '0.82.1' -and
    $piAgentPackage.dependencies.'@earendil-works/pi-coding-agent' -eq
        '0.82.1' -and
    [regex]::Matches(
        $piAgentRuntimeSource,
        'from\s+"node:net"').Count -eq 1 -and
    $piAgentRuntimeSource.Contains('createConnection(pipePath)') -and
    $piAgentRuntimeSource.Contains('validateDesktopBrokerPipe(pipePath)') -and
    $piAgentRuntimeSource.Contains(
        'jarvis2-pi-model-[0-9a-f]{32}') -and
    -not [regex]::IsMatch(
        $piAgentRuntimeSource,
        '(?i)\b(?:electron|webview2?|browserwindow|' +
        'node:(?:http|https|dgram)|createServer|listen)\b')
Add-Check `
    'architecture.pi-agent-isolated-nonweb-sidecar' `
    $piAgentSidecarOnly `
    (
        'The reviewed private Pi package may host only the bounded Node JSONL ' +
        'sidecar and its exact local named-pipe client; browser, server, TCP/UDP ' +
        'and WebView runtimes remain forbidden.'
    )

$forbiddenFiles = @(
    Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction Stop |
        Where-Object {
            $_.FullName -notmatch
                '[\\/](bin|obj|artifacts|node_modules|\.git)[\\/]' -and
            (($_.Name -eq 'package.json' -and
              $_.FullName -ne $piAgentPackagePath) -or
             $_.Extension -in '.html', '.jsx', '.tsx')
        }
)
Add-Check `
    'architecture.no-web-shell' `
    ($forbiddenFiles.Count -eq 0) `
    'The project must not contain an Electron, WebView or HTML replacement shell.'

$managedBuild = [pscustomobject]@{
    status = 'skipped'
    exitCode = $null
    detail = 'Managed build was explicitly skipped.'
}
if (-not $SkipManagedBuild) {
    $supervisorBuildOutput =
        & dotnet build $supervisorProject --configuration Release --nologo 2>&1
    $supervisorBuildExitCode = $LASTEXITCODE
    Add-Check `
        'supervisor.release-build' `
        ($supervisorBuildExitCode -eq 0) `
        (($supervisorBuildOutput | Select-Object -Last 8) -join [Environment]::NewLine)
    $hostModelBuildOutput =
        & dotnet build $explorerHostModelProject --configuration Release --nologo 2>&1
    $hostModelBuildExitCode = $LASTEXITCODE
    Add-Check `
        'explorer-host-model.release-build' `
        ($hostModelBuildExitCode -eq 0) `
        (($hostModelBuildOutput | Select-Object -Last 8) -join [Environment]::NewLine)
    $controlCenterBuildOutput =
        & dotnet build $controlCenterProject --configuration Release --nologo 2>&1
    $controlCenterBuildExitCode = $LASTEXITCODE
    Add-Check `
        'control-center.release-build' `
        ($controlCenterBuildExitCode -eq 0) `
        (($controlCenterBuildOutput | Select-Object -Last 8) -join [Environment]::NewLine)
    $nativeStyleBuildOutput =
        & dotnet build $nativeStyleLabProject --configuration Release --nologo 2>&1
    $nativeStyleBuildExitCode = $LASTEXITCODE
    Add-Check `
        'native-style-lab.release-build' `
        ($nativeStyleBuildExitCode -eq 0) `
        (($nativeStyleBuildOutput | Select-Object -Last 8) -join [Environment]::NewLine)
    $desktopProbeBuildOutput =
        & dotnet build $desktopStyleProbeProject --configuration Release --nologo 2>&1
    $desktopProbeBuildExitCode = $LASTEXITCODE
    Add-Check `
        'desktop-style-probe.release-build' `
        ($desktopProbeBuildExitCode -eq 0) `
        (($desktopProbeBuildOutput | Select-Object -Last 8) -join [Environment]::NewLine)
    $desktopSessionBuildOutput =
        & dotnet build $desktopStyleSessionProject --configuration Release --nologo 2>&1
    $desktopSessionBuildExitCode = $LASTEXITCODE
    Add-Check `
        'desktop-style-session.release-build' `
        ($desktopSessionBuildExitCode -eq 0) `
        (($desktopSessionBuildOutput | Select-Object -Last 8) -join [Environment]::NewLine)
    $nativeWindowSessionBuildOutput =
        & dotnet build $nativeWindowStyleSessionProject --configuration Release --nologo 2>&1
    $nativeWindowSessionBuildExitCode = $LASTEXITCODE
    Add-Check `
        'native-window-style-session.release-build' `
        ($nativeWindowSessionBuildExitCode -eq 0) `
        (($nativeWindowSessionBuildOutput | Select-Object -Last 8) -join [Environment]::NewLine)
    $explorerFrameModelBuildOutput =
        & dotnet build $explorerFrameModelProject --configuration Release --nologo 2>&1
    $explorerFrameModelBuildExitCode = $LASTEXITCODE
    Add-Check `
        'explorer-frame-model.release-build' `
        ($explorerFrameModelBuildExitCode -eq 0) `
        (($explorerFrameModelBuildOutput | Select-Object -Last 8) -join [Environment]::NewLine)
    $explorerPreviewModelBuildOutput =
        & dotnet build $explorerPreviewModelProject --configuration Release --nologo 2>&1
    $explorerPreviewModelBuildExitCode = $LASTEXITCODE
    Add-Check `
        'explorer-preview-model.release-build' `
        ($explorerPreviewModelBuildExitCode -eq 0) `
        (($explorerPreviewModelBuildOutput | Select-Object -Last 8) -join [Environment]::NewLine)
    $explorerSurfaceProbeBuildOutput =
        & dotnet build $explorerSurfaceProbeProject --configuration Release --nologo 2>&1
    $explorerSurfaceProbeBuildExitCode = $LASTEXITCODE
    Add-Check `
        'explorer-surface-probe.release-build' `
        ($explorerSurfaceProbeBuildExitCode -eq 0) `
        (($explorerSurfaceProbeBuildOutput | Select-Object -Last 8) -join [Environment]::NewLine)
    $buildExitCode = if (
        $supervisorBuildExitCode -eq 0 -and
        $hostModelBuildExitCode -eq 0 -and
        $controlCenterBuildExitCode -eq 0 -and
        $nativeStyleBuildExitCode -eq 0 -and
        $desktopProbeBuildExitCode -eq 0 -and
        $desktopSessionBuildExitCode -eq 0 -and
        $nativeWindowSessionBuildExitCode -eq 0 -and
        $explorerFrameModelBuildExitCode -eq 0 -and
        $explorerPreviewModelBuildExitCode -eq 0 -and
        $explorerSurfaceProbeBuildExitCode -eq 0
    ) {
        0
    }
    else {
        1
    }
    $buildOutput = @(
        $supervisorBuildOutput
        $hostModelBuildOutput
        $controlCenterBuildOutput
        $nativeStyleBuildOutput
        $desktopProbeBuildOutput
        $desktopSessionBuildOutput
        $nativeWindowSessionBuildOutput
        $explorerFrameModelBuildOutput
        $explorerPreviewModelBuildOutput
        $explorerSurfaceProbeBuildOutput
    )
    $managedBuild = [pscustomobject]@{
        status = if ($buildExitCode -eq 0) { 'passed' } else { 'failed' }
        exitCode = $buildExitCode
        detail = (($buildOutput | Select-Object -Last 8) -join [Environment]::NewLine)
    }
}

$allChecksPassed = $failures.Count -eq 0
$receiptChecksPassed = @($checks | Where-Object { $_.name -like 'receipt.*' -and -not $_.passed }).Count -eq 0
$offlineEvidenceReady = $allChecksPassed -and $receiptChecksPassed -and $managedBuild.status -eq 'passed'
$result = [pscustomobject]@{
    project = 'JARVIS2'
    passed = $allChecksPassed
    offlineEvidenceReady = $offlineEvidenceReady
    releaseGatePassed = $offlineEvidenceReady
    releaseReady = $false
    activationPermitted = $false
    managedBuild = $managedBuild
    checkCount = $checks.Count
    moduleSourceSha256 = $sourceHashes
    checks = $checks
    failures = $failures
}

$result | ConvertTo-Json -Depth 7
if (-not $allChecksPassed) {
    exit 1
}
