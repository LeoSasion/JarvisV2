[CmdletBinding()]
param(
    [switch]$StaticOnly,
    [string]$NodePath = 'node'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root 'src\common\Jarvis.PiAgentHost'
$contractPath = Join-Path $root (
    'config\pi-agent-desktop-host-contract.json')
$schemaPath = Join-Path $root (
    'config\pi-agent-desktop-host-contract.schema.json')
$packagePath = Join-Path $sourceRoot 'package.json'
$lockPath = Join-Path $sourceRoot 'pnpm-lock.yaml'
$hostPath = Join-Path $sourceRoot 'src\host.mjs'
$protocolTestPath = Join-Path $sourceRoot 'test\protocol.test.mjs'

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

$contract =
    Get-Content -LiteralPath $contractPath -Raw |
        ConvertFrom-Json -Depth 40
$schema =
    Get-Content -LiteralPath $schemaPath -Raw |
        ConvertFrom-Json -Depth 40
$package =
    Get-Content -LiteralPath $packagePath -Raw |
        ConvertFrom-Json -Depth 20
$runtimeSourceText = @(
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'src') `
        -File `
        -Filter '*.mjs' |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine

Add-Check `
    -Name 'contract.official-exact-upstream' `
    -Passed (
        $contract.schemaVersion -eq 1 -and
        $contract.contractId -eq
            'jarvisv2-pi-agent-desktop-host-v1' -and
        $contract.upstream.package -eq
            '@earendil-works/pi-coding-agent' -and
        $contract.upstream.exactVersion -eq '0.82.1' -and
        $contract.upstream.repository -eq
            'https://github.com/earendil-works/pi' -and
        $contract.upstream.license -eq 'MIT' -and
        $package.dependencies.'@earendil-works/pi-coding-agent' -eq
            '0.82.1') `
    -Detail (
        'The sidecar must pin the reviewed official Pi package exactly, ' +
        'without a floating range.')

Add-Check `
    -Name 'contract.fail-closed-session-and-tools' `
    -Passed (
        -not $contract.runtime.sessionCreationEnabled -and
        -not $contract.runtime.desktopLaunchImplemented -and
        $contract.runtime.launchState -eq 'transport-probe-only' -and
        -not $contract.session.enabled -and
        $contract.session.credentialTransport -eq 'forbidden' -and
        (@($contract.tools.initialAllowlist) -join '|') -eq
            'read|grep|find|ls' -and
        (@($contract.tools.initiallyDenied) -join '|') -eq
            'bash|edit|write' -and
        -not $contract.tools.unattendedSelfIteration) `
    -Detail (
        'The first embedded boundary exposes a transport probe only; ' +
        'session creation, credentials and mutation tools remain denied.')

Add-Check `
    -Name 'contract.jsonl-and-shell-boundary' `
    -Passed (
        $contract.runtime.integrationMode -eq 'sdk-sidecar-jsonl' -and
        $contract.runtime.piOfflineRequired -and
        $contract.transport.framing -eq 'lf-delimited-jsonl' -and
        $contract.transport.maxFrameBytes -eq 65536 -and
        -not $contract.transport.credentialFieldsAllowed -and
        -not $contract.boundaries.shellMutationSupported -and
        -not $contract.boundaries.explorerMutationSupported -and
        -not $contract.boundaries.systemMutationSupported -and
        -not $contract.boundaries.activationPermitted -and
        $contract.boundaries.liveExplorer -eq 'not-run') `
    -Detail (
        'The language-neutral boundary uses bounded LF-delimited JSONL and ' +
        'cannot mutate the Shell, Explorer or system.')

Add-Check `
    -Name 'schema.fixed-safety-values' `
    -Passed (
        $schema.'$schema' -eq
            'https://json-schema.org/draft/2020-12/schema' -and
        $schema.title -eq
            'JarvisV2 Pi Agent desktop host contract' -and
        $schema.properties.upstream.properties.exactVersion.const -eq
            '0.82.1' -and
        $schema.properties.runtime.properties.nodeMinimumMajor.const -eq
            22 -and
        $schema.properties.transport.properties.maxFrameBytes.const -eq
            65536 -and
        $schema.properties.runtime.properties.sessionCreationEnabled.const `
            -eq $false -and
        $schema.properties.transport.properties.credentialFieldsAllowed.const `
            -eq $false -and
        $schema.properties.boundaries.properties.activationPermitted.const `
            -eq $false) `
    -Detail (
        'The published schema must hard-code the initial disabled ' +
        'session, credential and activation boundary.')

$forbiddenRuntimePattern = (
    '(?i)\b(?:child_process|spawn|execFile|execSync|shell\s*:|' +
    'createAgentSession\s*\(|ModelRuntime\.create\s*\(|' +
    'ANTHROPIC_API_KEY|OPENAI_API_KEY|auth\.json|' +
    'writeFile|appendFile|rmSync|unlinkSync)\b'
)
Add-Check `
    -Name 'source.transport-probe-only' `
    -Passed (
        -not [regex]::IsMatch(
            $runtimeSourceText,
            $forbiddenRuntimePattern) -and
        $runtimeSourceText.Contains('process.env.PI_OFFLINE = "1"') -and
        $runtimeSourceText.Contains(
            'Pi Agent session creation is disabled') -and
        $runtimeSourceText.Contains('Buffer.byteLength') -and
        $runtimeSourceText.Contains('Buffer.byteLength(line, "utf8")') -and
        $runtimeSourceText.Contains('buffer.indexOf("\n")') -and
        $runtimeSourceText.Contains('credential-field-forbidden')) `
    -Detail (
        'Runtime source may inspect the pinned SDK and serve bounded JSONL; ' +
        'it may not launch children, create sessions, read credentials or ' +
        'write files.')

$lockValid = $false
if (Test-Path -LiteralPath $lockPath -PathType Leaf) {
    $lockText = [IO.File]::ReadAllText($lockPath)
    $lockValid =
        $lockText.Contains(
            "'@earendil-works/pi-coding-agent@0.82.1'") -and
        $lockText.Contains('integrity:')
}
Add-Check `
    -Name 'dependency.frozen-lock' `
    -Passed $lockValid `
    -Detail (
        'pnpm-lock.yaml must pin Pi 0.82.1 and retain registry integrity ' +
        'hashes; lifecycle scripts are not required.')

if (-not $StaticOnly) {
    $nodeVersionOutput = @(& $NodePath --version 2>&1)
    $nodeExitCode = $LASTEXITCODE
    $nodeVersion = $null
    if ($nodeExitCode -eq 0 -and
        ($nodeVersionOutput -join '') -match '^v(?<major>\d+)') {
        $nodeVersion = [int]$Matches['major']
    }
    Add-Check `
        -Name 'runtime.node-version' `
        -Passed ($nodeVersion -ge $contract.runtime.nodeMinimumMajor) `
        -Detail (
            "Node exit $nodeExitCode; version " +
            "$($nodeVersionOutput -join '').")

    $inspectOutput = @(
        & $NodePath $hostPath inspect 2>&1
    )
    $inspectExitCode = $LASTEXITCODE
    $inspectReceipt = $null
    try {
        $inspectReceipt =
            ($inspectOutput -join [Environment]::NewLine) |
                ConvertFrom-Json -Depth 30
    }
    catch {
        $inspectReceipt = $null
    }
    $inspectResult = if ($null -ne $inspectReceipt) {
        $inspectReceipt.result
    }
    else {
        'unparsed'
    }
    $installedVersion = if ($null -ne $inspectReceipt) {
        $inspectReceipt.installedVersion
    }
    else {
        'unknown'
    }
    Add-Check `
        -Name 'runtime.embedded-sdk-inspection' `
        -Passed (
            $inspectExitCode -eq 0 -and
            $null -ne $inspectReceipt -and
            $inspectReceipt.result -eq
                'passed-embedded-dependency' -and
            $inspectReceipt.installedVersion -eq '0.82.1' -and
            @($inspectReceipt.missingExports).Count -eq 0 -and
            $inspectReceipt.piOffline -and
            $inspectReceipt.transportReady -and
            -not $inspectReceipt.sessionCreationEnabled -and
            -not $inspectReceipt.credentialTransportAllowed -and
            -not $inspectReceipt.shellMutationSupported -and
            -not $inspectReceipt.explorerMutationSupported -and
            -not $inspectReceipt.activationPermitted -and
            $inspectReceipt.liveExplorer -eq 'not-run') `
        -Detail (
            "Inspect exit $inspectExitCode; result " +
            "$inspectResult; installed $installedVersion.")

    $protocolOutput = @(
        & $NodePath $protocolTestPath 2>&1
    )
    $protocolExitCode = $LASTEXITCODE
    $protocolReceipt = $null
    try {
        $protocolReceipt =
            ($protocolOutput -join [Environment]::NewLine) |
                ConvertFrom-Json -Depth 30
    }
    catch {
        $protocolReceipt = $null
    }
    $protocolResult = if ($null -ne $protocolReceipt) {
        $protocolReceipt.result
    }
    else {
        'unparsed'
    }
    $recordCount = if ($null -ne $protocolReceipt) {
        $protocolReceipt.recordCount
    }
    else {
        0
    }
    Add-Check `
        -Name 'runtime.jsonl-policy-probe' `
        -Passed (
            $protocolExitCode -eq 0 -and
            $null -ne $protocolReceipt -and
            $protocolReceipt.result -eq 'passed' -and
            $protocolReceipt.recordCount -eq 6 -and
            $protocolReceipt.framing -eq 'lf-delimited-jsonl' -and
            $protocolReceipt.credentialFieldsRejected -and
            $protocolReceipt.batchedFramesAccepted -eq 81 -and
            $protocolReceipt.oversizedFrameRejected -and
            -not $protocolReceipt.sessionCreationEnabled -and
            -not $protocolReceipt.credentialTransportAllowed -and
            (@($protocolReceipt.initialTools) -join '|') -eq
                'read|grep|find|ls' -and
            -not $protocolReceipt.shellMutationSupported -and
            -not $protocolReceipt.explorerMutationSupported -and
            -not $protocolReceipt.activationPermitted -and
            $protocolReceipt.liveExplorer -eq 'not-run') `
        -Detail (
            "Protocol exit $protocolExitCode; result " +
            "$protocolResult; records $recordCount.")
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-pi-agent-host-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    integrationMode = 'sdk-sidecar-jsonl'
    embeddedPackage = '@earendil-works/pi-coding-agent'
    embeddedVersion = '0.82.1'
    transportProbeImplemented = $true
    sessionCreationEnabled = $false
    desktopLaunchImplemented = $false
    credentialTransportAllowed = $false
    shellMutationSupported = $false
    explorerMutationSupported = $false
    systemMutationSupported = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 12

if (-not $passed) {
    exit 1
}
