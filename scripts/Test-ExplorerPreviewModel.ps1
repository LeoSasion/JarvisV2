[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectPath =
    Join-Path $root 'src\Jarvis.ExplorerPreviewModel\Jarvis.ExplorerPreviewModel.csproj'
$sourceRoot =
    Join-Path $root 'src\Jarvis.ExplorerPreviewModel'
$profilePath =
    Join-Path $root 'config\explorer-frame-selector-candidate.json'
$schemaPath =
    Join-Path $root 'config\explorer-frame-selector-candidate.schema.json'
$compatibilityPath =
    Join-Path $root 'config\compatibility.json'
$upstreamLockPath =
    Join-Path $root 'config\upstream-lock.json'

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

$profileJson = [IO.File]::ReadAllText($profilePath)
$profile = $profileJson | ConvertFrom-Json -Depth 40
$schemaPassed =
    $profileJson |
    Test-Json -SchemaFile $schemaPath -ErrorAction Stop
Add-Check `
    -Name 'profile.schema-and-locked-boundary' `
    -Passed (
        $schemaPassed -and
        $profile.lifecycleState -eq 'offline-candidate' -and
        $profile.liveEvidence -eq 'not-run' -and
        -not $profile.executionSupported -and
        -not $profile.activationPermitted -and
        -not $profile.mutationPerformed
    ) `
    -Detail (
        'The selector candidate must validate against its schema and remain ' +
        'offline, non-executable, non-live and non-mutating.'
    )

$upstreamLock =
    Get-Content -LiteralPath $upstreamLockPath -Raw |
    ConvertFrom-Json -Depth 40
$fileExplorerLock = @(
    $upstreamLock.dependencies |
        Where-Object name -eq 'Windows 11 File Explorer Styler'
)
Add-Check `
    -Name 'profile.gpl-upstream-identity-pinned' `
    -Passed (
        $fileExplorerLock.Count -eq 1 -and
        $fileExplorerLock[0].version -eq '1.5' -and
        $fileExplorerLock[0].auditedCommit -eq
            '109589023dde428deaee2fe80e4ce446283a7935' -and
        $fileExplorerLock[0].gitBlob -eq
            '6f67b714c271db1235a5f937c30c5cae55b180bf' -and
        $fileExplorerLock[0].sourceSize -eq 326922 -and
        $fileExplorerLock[0].sourceSha256 -eq
            'ECD6189A76439518E84938F4CA42FDB7F78AA1CCE3151EE0FE93638918D2DCED' -and
        $profile.upstreamIdentity.sourceSha256 -eq
            $fileExplorerLock[0].sourceSha256
    ) `
    -Detail (
        'The GPL File Explorer Styler commit, blob, size and SHA-256 must ' +
        'be exact in both the upstream lock and candidate profile.'
    )

$sourceText = @(
    Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File |
        Sort-Object Name |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$forbiddenLivePattern = (
    '(?i)\b(?:DllImport|LibraryImport|ComImport|' +
    'System\.Windows\.Automation|System\.Diagnostics\.Process|' +
    'InitializeXamlDiagnosticsEx|IXamlDiagnostics|IVisualTreeService|' +
    'OpenProcess|CreateRemoteThread|VirtualAllocEx|WriteProcessMemory|' +
    'SetWindowsHookEx|LoadLibrary|StartService|ServiceController|' +
    'Microsoft\.Win32\.Registry|TerminateProcess)\b'
)
Add-Check `
    -Name 'source.offline-compiler-and-planner-only' `
    -Passed (
        -not [regex]::IsMatch($sourceText, $forbiddenLivePattern) -and
        $sourceText.Contains('compile-candidate') -and
        $sourceText.Contains('ReadyForPreview: false') -and
        $sourceText.Contains('ReadyForExactApproval: false')
    ) `
    -Detail (
        'The preview model may compile files and simulate review plans but ' +
        'must expose no process, XAML, hook, injection or system API.'
    )

Add-Check `
    -Name 'contract.exact-selector-and-preview-policy' `
    -Passed (
        @($profile.surfaces).Count -eq 3 -and
        @($profile.surfaces.role | Sort-Object -Unique) -join ',' -eq
            'command-bar,navigation-pane,tab-strip' -and
        @($profile.surfaces |
            Where-Object expectedMatchCount -ne 1).Count -eq 0 -and
        $profile.previewPolicy.durationSeconds -eq 60 -and
        $profile.previewPolicy.restoreOrder -eq 'strict-reverse' -and
        @($profile.previewPolicy.screenshotCheckpoints) -join ',' -eq
            'before,during,after'
    ) `
    -Detail (
        'The candidate must cover exactly three surfaces, require one match ' +
        'each, preview for 60 seconds and capture before/during/after.'
    )

Add-Check `
    -Name 'contract.strict-reverse-review-plan' `
    -Passed (
        $sourceText.Contains(
            '["tab-strip", "command-bar", "navigation-pane"]') -and
        $sourceText.Contains('foreach (string role in ApplyOrder.Reverse())') -and
        $sourceText.Contains('journal-all-original-properties') -and
        $sourceText.Contains('verify-all-originals-restored') -and
        $sourceText.Contains('close-temporary-window')
    ) `
    -Detail (
        'The review plan must journal before apply, restore all surfaces in ' +
        'strict reverse order, verify originals and close the temp window.'
    )

$buildOutput = @(
    & dotnet build `
        $projectPath `
        --configuration Release `
        --nologo `
        --warnaserror 2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release-warning-free' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 8) -join [Environment]::NewLine)

$compileReceipt = $null
$compileExitCode = $null
if ($buildExitCode -eq 0) {
    $compileOutput = @(
        & dotnet run `
            --project $projectPath `
            --configuration Release `
            --no-build `
            -- compile-candidate `
            $profilePath `
            $compatibilityPath 2>&1
    )
    $compileExitCode = $LASTEXITCODE
    try {
        $compileReceipt =
            ($compileOutput -join [Environment]::NewLine) |
            ConvertFrom-Json -Depth 30
    }
    catch {
        $compileReceipt = $null
    }
}
$compilePassed =
    $compileExitCode -eq 0 -and
    $null -ne $compileReceipt -and
    $compileReceipt.result -eq 'compiled-offline-candidate' -and
    @($compileReceipt.surfaces).Count -eq 3 -and
    $compileReceipt.readyForReadOnlyDiscovery -and
    -not $compileReceipt.readyForPreview -and
    -not $compileReceipt.readyForExactApproval -and
    -not $compileReceipt.executionSupported -and
    -not $compileReceipt.activationPermitted -and
    $compileReceipt.liveExplorer -eq 'not-run' -and
    -not $compileReceipt.mutationPerformed -and
    @($compileReceipt.failures).Count -eq 0
$compileDetail = if ($null -eq $compileReceipt) {
    "Candidate compiler receipt unavailable; exit $compileExitCode."
}
else {
    "Compiler exit $compileExitCode; profile " +
        "$($compileReceipt.profileSha256); surfaces " +
        "$(@($compileReceipt.surfaces).Count)."
}
Add-Check `
    -Name 'compiler.real-candidate-bound-offline' `
    -Passed $compilePassed `
    -Detail $compileDetail

$modelReceipt = $null
$modelExitCode = $null
if ($buildExitCode -eq 0) {
    $modelOutput = @(
        & dotnet run `
            --project $projectPath `
            --configuration Release `
            --no-build `
            -- model-test 2>&1
    )
    $modelExitCode = $LASTEXITCODE
    try {
        $modelReceipt =
            ($modelOutput -join [Environment]::NewLine) |
            ConvertFrom-Json -Depth 30
    }
    catch {
        $modelReceipt = $null
    }
}
$modelPassed =
    $modelExitCode -eq 0 -and
    $null -ne $modelReceipt -and
    $modelReceipt.result -eq 'passed' -and
    $modelReceipt.scenarioCount -eq 43 -and
    $modelReceipt.passedCount -eq 43 -and
    -not $modelReceipt.executionSupported -and
    -not $modelReceipt.activationPermitted -and
    $modelReceipt.liveExplorer -eq 'not-run' -and
    -not $modelReceipt.mutationPerformed
$modelDetail = if ($null -eq $modelReceipt) {
    "Model receipt unavailable; exit $modelExitCode."
}
else {
    "Model exit $modelExitCode; passed " +
        "$($modelReceipt.passedCount)/$($modelReceipt.scenarioCount)."
}
Add-Check `
    -Name 'model.profile-and-preview-fault-matrix' `
    -Passed $modelPassed `
    -Detail $modelDetail

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-explorer-preview-model-audit'
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
