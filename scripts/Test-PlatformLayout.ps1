[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$matrixPath = Join-Path $root 'config\platform-matrix.json'
$matrix =
    Get-Content -LiteralPath $matrixPath -Raw |
        ConvertFrom-Json
$visualBackendSchemaPath = Join-Path $root (
    [string]$matrix.visualBackendContract.schemaPath)
$visualBackendManifestRoot = Join-Path $root (
    [string]$matrix.visualBackendContract.manifestRoot)
$checks = [System.Collections.Generic.List[object]]::new()
$errors = [System.Collections.Generic.List[string]]::new()
$pathComparison = if ([OperatingSystem]::IsWindows()) {
    [StringComparison]::OrdinalIgnoreCase
}
else {
    [StringComparison]::Ordinal
}

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
        $errors.Add($Name)
    }
}

Add-Check `
    'matrix.identity' `
    ($matrix.schemaVersion -eq 1 -and
     $matrix.repositoryName -eq 'JarvisV2' -and
     $matrix.runtimeNamespace -eq 'JARVIS2' -and
     $matrix.layoutContract -eq
        'common-plus-versioned-windows-backends-v1') `
    'The platform matrix must preserve the public and runtime identities.'

$requiredRoots = @(
    'src/common',
    'src/platforms/windows10',
    'src/platforms/windows11',
    'mods/common',
    'mods/windows10',
    'mods/windows11',
    'tests/native/common',
    'tests/native/windows10',
    'tests/native/windows11'
)
Add-Check `
    'layout.required-roots' `
    (@($requiredRoots | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $root $_) -PathType Container)
    }).Count -eq 0) `
    'Every common, Win10 and Win11 source/mod/test root must exist.'

$rootSourceDirectories =
    @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Directory |
        Select-Object -ExpandProperty Name |
        Sort-Object)
Add-Check `
    'layout.no-legacy-source-roots' `
    ($rootSourceDirectories.Count -eq 2 -and
     $rootSourceDirectories[0] -eq 'common' -and
     $rootSourceDirectories[1] -eq 'platforms') `
    'src must expose only common and platforms at its root.'

$commonProjects =
    @(Get-ChildItem -LiteralPath (Join-Path $root 'src\common') -Directory |
        Select-Object -ExpandProperty Name |
        Sort-Object)
$expectedCommonProjects =
    @(
        'Jarvis.ControlCenter',
        'Jarvis.ControlCenter.Diagnostics',
        'Jarvis.DesktopPresence',
        'Jarvis.DesktopStyleProbe',
        'Jarvis.DesktopStyleSession',
        'Jarvis.PiAgentHost',
        'Jarvis.VisualEffects'
    )
$declaredCommonProjects =
    @($matrix.common.projects | ForEach-Object { [string]$_ } | Sort-Object)
Add-Check `
    'common.reviewed-project-set' `
    (($commonProjects -join '|') -eq ($expectedCommonProjects -join '|') -and
     ($declaredCommonProjects -join '|') -eq
        ($expectedCommonProjects -join '|')) `
    ('Common disk layout and platform matrix must name the same seven ' +
     'reviewed Windows-family candidates.')

$windows10Projects =
    @(Get-ChildItem -LiteralPath (
        Join-Path $root 'src\platforms\windows10') -Directory |
        Select-Object -ExpandProperty Name |
        Sort-Object)
$expectedWindows10Projects =
    @(
        'Jarvis.Win10.DesktopStyleSession',
        'Jarvis.Win10.ExplorerBridgeCore',
        'Jarvis.Win10.ExplorerCallWndProcBridge',
        'Jarvis.Win10.ExplorerCaptionOverlay',
        'Jarvis.Win10.ExplorerCaptionPlan',
        'Jarvis.Win10.ExplorerCaptionSession',
        'Jarvis.Win10.ExplorerExactThreadTransport',
        'Jarvis.Win10.HostAdmission',
        'Jarvis.Win10.NativeStyleProbe',
        'Jarvis.Win10.NeuralVoidPreview',
        'Jarvis.Win10.OwnedTaskManager',
        'Jarvis.Win10.RgbThemeModel',
        'Jarvis.Win10.ShellSurfaceProbe',
        'Jarvis.Win10.SurfaceSelectorModel',
        'Jarvis.Win10.TaskbarEdgeOverlay'
    )
Add-Check `
    'windows10.reviewed-project-set' `
    (($windows10Projects -join '|') -eq
        ($expectedWindows10Projects -join '|')) `
    'Windows 10 contains only the fifteen reviewed platform projects.'

$windows10 = @($matrix.platforms | Where-Object id -eq 'windows10')
$windows11 = @($matrix.platforms | Where-Object id -eq 'windows11')
Add-Check `
    'platforms.fail-closed-status' `
    ($windows10.Count -eq 1 -and
     $windows11.Count -eq 1 -and
     -not $windows10[0].activationPermitted -and
     -not $windows11[0].activationPermitted -and
     $windows10[0].liveExplorer -eq 'not-run' -and
     $windows11[0].liveExplorer -eq 'not-run') `
    'Both platform entries must remain non-activating and non-live.'

Add-Check `
    'windows10.owned-task-manager-roadmap' `
    ($windows10[0].status -eq
        'owned-horizon-desktop-with-read-only-task-manager-and-owned-layouts-and-offline-callback-dll-only' -and
     ([string]$windows10[0].nextVisibleSlice).Contains(
        'read-only Task Manager') -and
     ([string]$windows10[0].nextInjectionSlice).Contains(
        'publication/install lease') -and
     ([string]$windows10[0].nextInjectionSlice).Contains(
        'exact-thread drain barrier') -and
     ([string]$windows10[0].nextInjectionSlice).Contains(
        'full HookProc-envelope proof')) `
    ('The Win10 matrix must track owned Task Manager UI separately from the ' +
     'still-blocked exact-thread injection lifecycle work.')

Add-Check `
    'visual-backends.contract-identity' `
    ($matrix.visualBackendContract.contractId -eq
        'jarvisv2-visual-backend-manifest-v1' -and
     $matrix.visualBackendContract.primaryDevelopmentBackend -eq
        'windows10-wpf-owned-preview' -and
     $matrix.visualBackendContract.currentWorkstreamScope -eq 'windows10' -and
     $matrix.visualBackendContract.unregisteredBackendPolicy -eq
        'unsupported' -and
     (Test-Path -LiteralPath $visualBackendSchemaPath -PathType Leaf) -and
     (Test-Path -LiteralPath $visualBackendManifestRoot -PathType Container)) `
    ('The portable visual boundary must remain manifest-based, Win10-first, ' +
     'and fail closed for unregistered backends.')

$visualBackendFiles = @()
if (Test-Path -LiteralPath $visualBackendManifestRoot -PathType Container) {
    $visualBackendFiles = @(
        Get-ChildItem `
            -LiteralPath $visualBackendManifestRoot `
            -Filter *.json `
            -File |
                Sort-Object Name)
}
$visualBackendRecords = @(
    foreach ($file in $visualBackendFiles) {
        $rawManifest = Get-Content -LiteralPath $file.FullName -Raw
        $schemaValid = $false
        $backend = $null
        try {
            $schemaValid = [bool](Test-Json `
                -Json $rawManifest `
                -SchemaFile $visualBackendSchemaPath `
                -ErrorAction Stop)
            if ($schemaValid) {
                $backend = $rawManifest | ConvertFrom-Json -Depth 100
            }
        }
        catch {
            $schemaValid = $false
        }
        [pscustomobject]@{
            file = $file
            schemaValid = $schemaValid
            backend = $backend
        }
    })
$visualBackendSchemaFailures = @(
    $visualBackendRecords |
        Where-Object { -not $_.schemaValid } |
        ForEach-Object { $_.file.Name })
Add-Check `
    'visual-backends.schema-conformance' `
    ($visualBackendFiles.Count -gt 0 -and
     $visualBackendSchemaFailures.Count -eq 0) `
    ('Every renderer manifest must satisfy the versioned schema before any ' +
     'field is trusted. Failures: ' +
     ($visualBackendSchemaFailures -join ', '))

$visualBackends = @(
    $visualBackendRecords |
        Where-Object schemaValid |
        ForEach-Object backend)
$visualBackendIds = @(
    $visualBackends | ForEach-Object { [string]$_.backendId })
Add-Check `
    'visual-backends.unique-file-owned-registration' `
    ($visualBackends.Count -gt 0 -and
     @($visualBackendIds | Sort-Object -Unique).Count -eq
        $visualBackendIds.Count -and
     @($visualBackendRecords | Where-Object {
        $_.schemaValid -and
        $_.file.BaseName -cne [string]$_.backend.backendId
     }).Count -eq 0) `
    ('Each renderer backend must own one uniquely named manifest so parallel ' +
     'platform pull requests do not share an implementation file.')

$visualBackendBoundaryFailures = @(
    foreach ($backend in $visualBackends) {
        $platform = @($matrix.platforms | Where-Object {
            $_.id -ceq [string]$backend.platformId
        })
        $sourceRoot = ([string]$backend.ownership.sourceRoot).Replace('\', '/')
        $testEntry = ([string]$backend.ownership.testEntry).Replace('\', '/')
        $platformRoot = if ($platform.Count -eq 1) {
            ([string]$platform[0].sourceRoot).TrimEnd('/')
        }
        else {
            ''
        }
        $sourceSegments = @($sourceRoot -split '/')
        $sourcePath = ''
        $platformPath = ''
        try {
            $sourcePath = [IO.Path]::GetFullPath((Join-Path $root $sourceRoot))
            $platformPath = [IO.Path]::GetFullPath((Join-Path $root $platformRoot))
        }
        catch {
            $sourcePath = ''
            $platformPath = ''
        }
        $platformPathPrefix =
            $platformPath.TrimEnd(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar
        $sourcePathIsPlatformOwned =
            $sourcePath.Length -gt 0 -and
            $platformPath.Length -gt 0 -and
            -not [IO.Path]::IsPathRooted($sourceRoot) -and
            @($sourceSegments | Where-Object { $_ -in @('.', '..') }).Count -eq 0 -and
            $sourcePath.StartsWith($platformPathPrefix, $pathComparison)
        $identityValid =
            $backend.schemaVersion -eq 1 -and
            $backend.contractId -ceq
                'jarvisv2-visual-backend-manifest-v1' -and
            $platform.Count -eq 1 -and
            $backend.target.operatingSystemFamily -ceq
                [string]$platform[0].operatingSystemFamily -and
            $backend.target.desktopEnvironment -ceq
                [string]$platform[0].desktopEnvironment -and
            $backend.target.compositor -ceq
                [string]$platform[0].compositor -and
            $backend.ownership.pullRequestScope -ceq
                [string]$backend.platformId -and
            $sourcePathIsPlatformOwned -and
            (Test-Path -LiteralPath $sourcePath -PathType Container) -and
            $testEntry.StartsWith('scripts/Test-', [StringComparison]::Ordinal) -and
            (Test-Path -LiteralPath (Join-Path $root $testEntry) -PathType Leaf)
        $portableInputsValid =
            $backend.contracts.visualSignal -ceq 'jarvis-visual-signal-v1' -and
            $backend.contracts.retainedVectorScene -ceq
                'jarvis-retained-vector-scene-v1'
        $capabilitiesCoherent =
            @($backend.capabilities.primitiveKinds |
                Sort-Object -Unique).Count -eq
                @($backend.capabilities.primitiveKinds).Count -and
            (-not $backend.capabilities.injectionRequired -or
                ($backend.capabilities.foreignProcessCode -and
                 $backend.capabilities.shellIntegration)) -and
            ($backend.capabilities.shellIntegration -or
                (-not $backend.evidence.activationPermitted -and
                 $backend.evidence.liveShell -ceq 'not-run' -and
                 -not $backend.evidence.mutationPerformed))
        $evidenceCoherent =
            ($backend.evidence.highestEvidence -cne 'owned-process-live' -or
                $backend.capabilities.ownedProcessRendering) -and
            ($backend.evidence.highestEvidence -cne 'shell-live' -or
                ($backend.capabilities.shellIntegration -and
                 $backend.evidence.liveShell -ceq 'passed')) -and
            ($backend.evidence.liveShell -cne 'passed' -or
                ($backend.capabilities.shellIntegration -and
                 $backend.evidence.highestEvidence -ceq 'shell-live')) -and
            ($backend.evidence.liveShell -cne 'failed' -or
                ($backend.capabilities.shellIntegration -and
                 $backend.evidence.highestEvidence -cne 'shell-live')) -and
            (-not $backend.evidence.activationPermitted -or
                $backend.capabilities.shellIntegration) -and
            (-not $backend.evidence.mutationPerformed -or
                ($backend.capabilities.shellIntegration -and
                 $backend.evidence.liveShell -cne 'not-run'))
        $supportClaimCoherent =
            (-not $backend.evidence.publicSupportDeclared -or
                ($backend.evidence.implementationStatus -ceq 'supported' -and
                 (($backend.capabilities.shellIntegration -and
                   $backend.evidence.highestEvidence -ceq 'shell-live' -and
                   $backend.evidence.liveShell -ceq 'passed') -or
                  (-not $backend.capabilities.shellIntegration -and
                   $backend.capabilities.ownedProcessRendering -and
                   $backend.evidence.highestEvidence -ceq
                        'owned-process-live'))))
        if (-not (
            $identityValid -and
            $portableInputsValid -and
            $capabilitiesCoherent -and
            $evidenceCoherent -and
            $supportClaimCoherent)) {
            [string]$backend.backendId
        }
    })
Add-Check `
    'visual-backends.fail-closed-capability-contract' `
    ($visualBackendBoundaryFailures.Count -eq 0) `
    ('Every registered backend must belong to one platform and pull-request ' +
     'scope, expose the two admitted data-plane contracts, and keep capability ' +
     'and evidence claims coherent. Failures: ' +
     ($visualBackendBoundaryFailures -join ', '))

$primaryVisualBackend = @($visualBackends | Where-Object {
    $_.backendId -ceq
        [string]$matrix.visualBackendContract.primaryDevelopmentBackend
})
Add-Check `
    'visual-backends.windows10-primary-owned-process' `
    ($primaryVisualBackend.Count -eq 1 -and
     $primaryVisualBackend[0].platformId -ceq 'windows10' -and
     $primaryVisualBackend[0].ownership.pullRequestScope -ceq 'windows10' -and
     $primaryVisualBackend[0].capabilities.surfaceScope -ceq 'owned-process' -and
     $primaryVisualBackend[0].capabilities.ownedProcessRendering -and
     -not $primaryVisualBackend[0].capabilities.shellIntegration -and
     -not $primaryVisualBackend[0].capabilities.foreignProcessCode -and
     -not $primaryVisualBackend[0].capabilities.injectionRequired -and
     -not $primaryVisualBackend[0].capabilities.physicalDeviceIo -and
     (@($primaryVisualBackend[0].capabilities.primitiveKinds) -join '|') -ceq
        'point|line|polyline|arc|path|rectangle|ellipse|plane' -and
     -not $primaryVisualBackend[0].evidence.publicSupportDeclared -and
     -not $primaryVisualBackend[0].evidence.activationPermitted -and
     $primaryVisualBackend[0].evidence.liveShell -ceq 'not-run' -and
     -not $primaryVisualBackend[0].evidence.mutationPerformed) `
    ('The current primary backend is the Win10 owned-process WPF renderer, ' +
     'not a Shell integration or injection support claim.')

$compatibility =
    Get-Content -LiteralPath (Join-Path $root 'config\compatibility.json') -Raw |
        ConvertFrom-Json
Add-Check `
    'windows11.compatibility-source-paths' `
    (@($compatibility.modules | Where-Object {
        -not ([string]$_.source).StartsWith(
            'mods/windows11/',
            [StringComparison]::Ordinal)
    }).Count -eq 0) `
    'Current module sources remain explicitly under the Win11 backend.'

$inventoryScript =
    [IO.File]::ReadAllText((Join-Path $root 'scripts\Inspect-Windows10Host.ps1'))
Add-Check `
    'windows10.inventory-read-only' `
    ($inventoryScript.Contains("mutationPerformed = `$false") -and
     $inventoryScript.Contains("activationPermitted = `$false") -and
     -not [regex]::IsMatch(
        $inventoryScript,
        '(?i)\b(?:Set-ItemProperty|New-ItemProperty|Remove-Item|' +
        'Start-Service|Stop-Service|Restart-Computer|Stop-Process|' +
        'Start-Process|DwmSetWindowAttribute|SendMessage)\b')) `
    'The Win10 entry inventory may read identity only.'

$publicationManifest =
    Get-Content -LiteralPath (Join-Path $root 'config\publication-manifest.json') -Raw |
        ConvertFrom-Json
$handoffRequired = @(
    'WINDOWS10-HANDOFF.md',
    'config/platform-matrix.json',
    'docs/PLATFORM-ARCHITECTURE.md',
    'scripts/Inspect-Windows10Host.ps1',
    'scripts/New-Windows10HandoffPackage.ps1',
    'scripts/Test-PlatformLayout.ps1',
    'scripts/Test-Windows10HandoffPackage.ps1'
)
Add-Check `
    'publication.handoff-entry-set' `
    (@($handoffRequired | Where-Object {
        $_ -notin @($publicationManifest.requiredFiles)
    }).Count -eq 0) `
    'The public boundary must require every handoff entry and gate.'

$ownedTaskManagerRequired = @(
    'docs/WINDOWS10-OWNED-TASK-MANAGER.md',
    'scripts/Test-Windows10OwnedTaskManager.ps1',
    'src/platforms/windows10/Jarvis.Win10.OwnedTaskManager/AssemblyInfo.cs',
    'src/platforms/windows10/Jarvis.Win10.OwnedTaskManager/Contracts.cs',
    'src/platforms/windows10/Jarvis.Win10.OwnedTaskManager/Jarvis.Win10.OwnedTaskManager.csproj',
    'src/platforms/windows10/Jarvis.Win10.OwnedTaskManager/ModelScenarios.cs',
    'src/platforms/windows10/Jarvis.Win10.OwnedTaskManager/Program.cs',
    'src/platforms/windows10/Jarvis.Win10.OwnedTaskManager/TaskSnapshotSampler.cs',
    'src/platforms/windows10/Jarvis.Win10.OwnedTaskManager/Tests/Jarvis.Win10.OwnedTaskManager.ContractTests.csproj',
    'src/platforms/windows10/Jarvis.Win10.OwnedTaskManager/Tests/Program.cs',
    'src/platforms/windows10/Jarvis.Win10.OwnedTaskManager/WindowsProcessSnapshotSource.cs',
    'src/platforms/windows10/Jarvis.Win10.NeuralVoidPreview/TaskManagerSurface.xaml',
    'src/platforms/windows10/Jarvis.Win10.NeuralVoidPreview/TaskManagerSurface.xaml.cs',
    'src/platforms/windows10/Jarvis.Win10.NeuralVoidPreview/TaskManagerSurfaceScenarios.cs',
    'src/platforms/windows10/Jarvis.Win10.NeuralVoidPreview/OwnedWindowLayoutCoordinator.cs',
    'src/platforms/windows10/Jarvis.Win10.NeuralVoidPreview/OwnedWindowLayoutScenarios.cs',
    'src/platforms/windows10/Jarvis.Win10.NeuralVoidPreview/OwnedPartitionCoordinator.cs',
    'src/platforms/windows10/Jarvis.Win10.NeuralVoidPreview/PartitionGeometry.cs',
    'src/platforms/windows10/Jarvis.Win10.NeuralVoidPreview/PartitionDivider.cs',
    'src/platforms/windows10/Jarvis.Win10.NeuralVoidPreview/DesktopPartitionSurface.cs',
    'src/platforms/windows10/Jarvis.Win10.NeuralVoidPreview/PartitionScenarios.cs',
    'docs/WINDOWS10-OWNED-WINDOW-LAYOUT.md'
)
Add-Check `
    'publication.owned-task-manager-entry-set' `
    (@($ownedTaskManagerRequired | Where-Object {
        $_ -notin @($publicationManifest.requiredFiles) -or
        -not (Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf)
    }).Count -eq 0) `
    ('The public boundary must include the complete owned Task Manager ' +
     'model, external tests, UI surface, document and validation entry.')

$result = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-platform-layout'
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    result = if ($errors.Count -eq 0) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = @($checks)
    errors = @($errors)
}
$result | ConvertTo-Json -Depth 8
if ($errors.Count -ne 0) {
    exit 1
}
