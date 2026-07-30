[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$matrixPath = Join-Path $root 'config\platform-matrix.json'
$matrix =
    Get-Content -LiteralPath $matrixPath -Raw |
        ConvertFrom-Json
$checks = [System.Collections.Generic.List[object]]::new()
$errors = [System.Collections.Generic.List[string]]::new()

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
        'Jarvis.DesktopStyleProbe',
        'Jarvis.DesktopStyleSession',
        'Jarvis.PiAgentHost',
        'Jarvis.VisualEffects'
    )
Add-Check `
    'common.reviewed-project-set' `
    (($commonProjects -join '|') -eq ($expectedCommonProjects -join '|')) `
    'Common contains only the five reviewed cross-version candidates.'

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
