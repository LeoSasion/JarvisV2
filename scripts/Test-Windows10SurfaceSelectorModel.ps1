[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.SurfaceSelectorModel')
$projectPath = Join-Path $sourceRoot (
    'Jarvis.Win10.SurfaceSelectorModel.csproj')
$candidatePath = Join-Path $root (
    'config\windows10-surface-selector-candidate.json')
$schemaPath = Join-Path $root (
    'config\windows10-surface-selector-candidate.schema.json')
$evidencePath = Join-Path $root (
    'tests\native\windows10\fixtures\' +
    'win10-22h2-19045.6466-shell-selector-evidence.json')

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

$candidate =
    Get-Content -LiteralPath $candidatePath -Raw |
        ConvertFrom-Json -Depth 50
$schema =
    Get-Content -LiteralPath $schemaPath -Raw |
        ConvertFrom-Json -Depth 50
$evidence =
    Get-Content -LiteralPath $evidencePath -Raw |
        ConvertFrom-Json -Depth 50
$sourceText = @(
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.csproj') |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine

$requiredRoles = @(
    'desktop-icon-list',
    'explorer-command-bar',
    'explorer-content-host',
    'explorer-folder-view',
    'taskbar-start-button',
    'taskbar-task-list',
    'taskbar-notification-area',
    'taskbar-clock'
)
Add-Check `
    -Name 'candidate.exact-eight-role-set' `
    -Passed (
        @($candidate.selectors).Count -eq 8 -and
        (@($candidate.selectors.role | Sort-Object) -join '|') -eq
            (($requiredRoles | Sort-Object) -join '|') -and
        @($candidate.selectors |
            Where-Object expectedMatchCount -ne 1).Count -eq 0) `
    -Detail (
        'The candidate must define exactly the eight reviewed Shell roles ' +
        'and require one match for each role.')

Add-Check `
    -Name 'candidate.offline-nonvisual-boundary' `
    -Passed (
        $candidate.platform -eq 'windows10' -and
        $candidate.profileId -eq 'win10-22h2-19045.6466-x64' -and
        $candidate.status -eq
            'offline-candidate-not-live-authorized' -and
        -not $candidate.styleValuesDefined -and
        -not $candidate.executionSupported -and
        -not $candidate.mutationSupported -and
        -not $candidate.activationPermitted -and
        $candidate.liveExplorer -eq 'not-run') `
    -Detail (
        'The selector set contains no style values and cannot execute, ' +
        'mutate, activate or contact live Explorer.')

$forbiddenSourcePattern = (
    '(?i)\b(?:DllImport|LibraryImport|Process\.|GetProcessesByName|' +
    'Registry|SendMessage|PostMessage|SetWindowLong|SetWindowPos|' +
    'DwmSetWindowAttribute|Windhawk|Start-Process|Stop-Process)\b'
)
Add-Check `
    -Name 'source.pure-offline-model' `
    -Passed (-not [regex]::IsMatch(
        $sourceText,
        $forbiddenSourcePattern)) `
    -Detail (
        'The model may parse embedded JSON and compare class paths only; ' +
        'native calls, process access, registry access and mutation APIs ' +
        'are forbidden.')

$evidenceText = [IO.File]::ReadAllText($evidencePath)
$sensitiveEvidencePattern = (
    '(?i)"(?:windowHandle|windowTitle|processId|threadId|path|' +
    'rectangle|name)"\s*:'
)
Add-Check `
    -Name 'evidence.sanitized-structural-only' `
    -Passed (
        $evidence.fixtureType -eq
            'sanitized-selector-evidence-excerpt' -and
        -not $evidence.windowTextCollected -and
        -not $evidence.containsUserContent -and
        -not [regex]::IsMatch(
            $evidenceText,
            $sensitiveEvidencePattern)) `
    -Detail (
        'The fixture may retain node keys, parent keys, classes, ' +
        'visibility, counts and topology hashes only.')

Add-Check `
    -Name 'schema.hard-false-capabilities' `
    -Passed (
        $schema.properties.styleValuesDefined.const -eq $false -and
        $schema.properties.executionSupported.const -eq $false -and
        $schema.properties.mutationSupported.const -eq $false -and
        $schema.properties.activationPermitted.const -eq $false -and
        $schema.properties.liveExplorer.const -eq 'not-run') `
    -Detail (
        'The published schema must make visual intent, execution, ' +
        'mutation, activation and live Explorer use impossible.')

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
    -Detail (($buildOutput | Select-Object -Last 12) -join
        [Environment]::NewLine)

if ($buildExitCode -eq 0) {
    $assemblyPath = Join-Path $sourceRoot (
        'bin\Release\net8.0-windows\' +
        'jarvis-win10-surface-selector-model.dll')
    $modelOutput = @(
        & $DotnetPath $assemblyPath test 2>&1
    )
    $modelExitCode = $LASTEXITCODE
    $receipt = $null
    try {
        $receipt =
            ($modelOutput -join [Environment]::NewLine) |
                ConvertFrom-Json -Depth 50
    }
    catch {
        $receipt = $null
    }

    Add-Check `
        -Name 'model.fail-closed-scenarios' `
        -Passed (
            $modelExitCode -eq 0 -and
            $null -ne $receipt -and
            $receipt.result -eq 'passed' -and
            $receipt.scenarioCount -ge 17 -and
            $receipt.passedCount -eq $receipt.scenarioCount -and
            -not $receipt.readyForVisualIntent -and
            -not $receipt.styleValuesDefined -and
            -not $receipt.executionSupported -and
            -not $receipt.mutationSupported -and
            -not $receipt.activationPermitted -and
            $receipt.liveExplorer -eq 'not-run' -and
            -not $receipt.mutationPerformed) `
        -Detail (
            "Model exit $modelExitCode; scenarios " +
            "$($receipt.passedCount)/$($receipt.scenarioCount).")
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-win10-surface-selector-model-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    readyForVisualIntent = $false
    styleValuesDefined = $false
    executionSupported = $false
    mutationSupported = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 12

if (-not $passed) {
    exit 1
}
