[CmdletBinding()]
param(
    [string]$GatePath = (Join-Path $PSScriptRoot 'Test-Windows10NeuralVoidPreview.ps1'),
    [string]$EvidenceDirectory = '',
    [string]$FixturePath = ''
)
# Deterministic fixtures exercise the actual gate consumers, not WPF or dotnet.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

function Get-GateAst {
    $tokens = $null
    $errors = $null
    $tree = [Management.Automation.Language.Parser]::ParseFile(
        $GatePath, [ref]$tokens, [ref]$errors)
    if (@($errors).Count -ne 0) { throw 'Gate parse failed.' }
    return $tree
}

$scenarioConsumers = @{
    vector = @('adapter', 'test-vector-adapter', 'vector.wpf-adapter-fail-closed-scenarios', 13, 1)
    edge = @('edge', 'test-edge-bars', 'interaction.edge-bar-scenarios', 25, 9)
    explorer = @('explorer', 'test-explorer-workspace', 'interaction.explorer-workspace-scenarios', 40, 1)
    'task-surface' = @('taskManagerSurface', 'test-task-manager-surface', 'interaction.owned-task-manager-scenarios', 10, 1)
    'owned-layout' = @('ownedLayout', 'test-owned-window-layout', 'interaction.owned-window-layout-scenarios', 137, 1)
}

if ($FixturePath) {
    $fixture = Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json
    if ($fixture.evidenceKind -cne 'deterministic-test-input-not-product-output') {
        throw 'Unlabelled fixture.'
    }
    $ast = Get-GateAst
    $text = [IO.File]::ReadAllText($GatePath)
    function Get-Assignment {
        param([string]$Name)
        $nodes = @($ast.FindAll({ param($n)
            $n -is [Management.Automation.Language.AssignmentStatementAst] -and
            $n.Left -is [Management.Automation.Language.VariableExpressionAst] -and
            $n.Left.VariablePath.UserPath -ceq $Name -and
            $n.Right.Extent.Text -cne '$false'
        }.GetNewClosure(), $true))
        if ($nodes.Count -ne 1) { throw "Assignment selection not unique: $Name ($($nodes.Count))." }
        return $nodes[0]
    }
    function Get-CheckText {
        param([string]$Name)
        $nodes = @($ast.FindAll({ param($n)
            $n -is [Management.Automation.Language.CommandAst] -and
            $n.GetCommandName() -ceq 'Add-Check' -and
            @($n.CommandElements | Where-Object {
                $_ -is [Management.Automation.Language.StringConstantExpressionAst] -and
                $_.Value -ceq $Name
            }).Count -eq 1
        }.GetNewClosure(), $true))
        if ($nodes.Count -ne 1) { throw "Check selection not unique: $Name." }
        return $nodes[0].Extent.Text
    }
    foreach ($function in @($ast.EndBlock.Statements | Where-Object {
        $_ -is [Management.Automation.Language.FunctionDefinitionAst]
    })) {
        . ([scriptblock]::Create($function.Extent.Text))
    }
    $checks = [Collections.Generic.List[object]]::new()
    $failures = [Collections.Generic.List[string]]::new()
    $approvedFeatherTargetSha256 = 'F472A0C14D08348B49A44D878202A3F69330D78A6226244D2B2A499EE2C70AC2'
    $approvedFeatherHashWellFormed = $true
    $approvedFeatherHashConfigured = $true
    $targetPixelBaselineBuild = 19045
    $targetPixelBaselineUbr = 6466
    $targetPixelBaselineEnforced = $true
    $currentFeatherBaselineHash = $approvedFeatherTargetSha256
    $featherRenderAttempted = $false
    $featherStructuralRenderPassed = $false
    $featherPixelComparable = $false
    $featherPixelApproved = $false
    $observedFeatherSha256 = $null
    $featherEvidenceState = 'locked-profile-not-run'
    $taskManagerRenderAttempted = $false
    $taskManagerStructuralRenderPassed = $false
    $taskManagerRenderSha256 = $null
    $taskManagerVisualEvidenceState = 'not-run'
    $renderCase = [pscustomobject]@{
        name = 'deterministic-test-fixture'; expectedHex = '#FFF000'
        approvedFeatherTargetSha256 = $approvedFeatherTargetSha256
    }
    $renderPassed = $true
    $renderDetails = [Collections.Generic.List[string]]::new()
    $renderHashes = [Collections.Generic.List[string]]::new()
    $outputPath = Join-Path (Split-Path $FixturePath) 'absent-fixture.png'
    if (Test-Path -LiteralPath $outputPath) { throw 'Absent fixture path exists.' }
    $renderOutput = @($fixture.output)
    $renderExitCode = $fixture.exitCode
    $pngValid = $fixture.pngValid # Predicate seam only, never pixel evidence.
    $taskManagerRenderOutput = $renderOutput
    $taskManagerRenderExitCode = $renderExitCode
    $taskManagerPngValid = $pngValid
    $isTaskManager = $fixture.name.StartsWith('task-manager.')
    $exitVariable = if ($isTaskManager) { 'taskManagerRenderExitCode' } else { 'renderExitCode' }
    $pngVariable = if ($isTaskManager) { '$taskManagerPngValid' } else { '$pngValid' }
    $predicateVariable = if ($isTaskManager) { 'taskManagerStructuralRenderPassed' } else { 'structuralCasePassed' }
    try {
        $consumer = $fixture.name.Split('.')[0]
        if ($scenarioConsumers.ContainsKey($consumer)) {
            $spec = $scenarioConsumers[$consumer]
            $prefix = $spec[0]
            Set-Variable -Name ($prefix + 'Output') -Value @($fixture.output)
            Set-Variable -Name ($prefix + 'ExitCode') -Value $fixture.exitCode
            $exitNodes = @($ast.FindAll({param($n)
                $n -is [Management.Automation.Language.AssignmentStatementAst] -and
                $n.Left.Extent.Text -ceq ('$' + $prefix + 'ExitCode') -and
                $n.Right.Extent.Text -ceq '$LASTEXITCODE'
            }.GetNewClosure(), $true) | Sort-Object { $_.Extent.StartOffset })
            # ownedLayout uses its variable again for the later renderer.
            $expectedExitNodes = if ($consumer -ceq 'owned-layout') { 2 } else { 1 }
            if ($exitNodes.Count -ne $expectedExitNodes) { throw 'Scenario exit selection changed.' }
            $checkText = Get-CheckText $spec[2]
            $end = $text.IndexOf($checkText, $exitNodes[0].Extent.EndOffset, [StringComparison]::Ordinal)
            if ($end -lt 0) { throw 'Missing scenario consumer.' }
            . ([scriptblock]::Create($text.Substring($exitNodes[0].Extent.EndOffset,
                $end + $checkText.Length - $exitNodes[0].Extent.EndOffset)))
            $footer = Get-Assignment 'featherEvidenceStateConsistent'
            . ([scriptblock]::Create($text.Substring($footer.Extent.StartOffset)))
            exit 0
        }
        if ($consumer -ceq 'layout-render') {
            $loop = @($ast.FindAll({param($n)
                $n -is [Management.Automation.Language.ForEachStatementAst] -and
                $n.Variable.VariablePath.UserPath -ceq 'ownedLayoutPreset'
            }, $true))
            if ($loop.Count -ne 1) { throw 'Layout renderer selection not unique.' }
            $exitNode = @($loop[0].Body.Statements | Where-Object {
                $_ -is [Management.Automation.Language.AssignmentStatementAst] -and
                $_.Left.Extent.Text -ceq '$ownedLayoutExitCode'
            })
            if ($exitNode.Count -ne 1) { throw 'Layout renderer exit selection not unique.' }
            $ownedLayoutOutput = @($fixture.output)
            $ownedLayoutExitCode = $fixture.exitCode
            $ownedLayoutPreset = $fixture.sentinel
            $ownedLayoutOutputPath = Join-Path (Split-Path $FixturePath) ($fixture.name + '.exists-only-fixture')
            if ($fixture.pngValid) {
                [IO.File]::WriteAllText($ownedLayoutOutputPath, 'TEST INPUT: exists-only predicate seam, not PNG evidence')
            }
            $ownedLayoutRenderPassed = $true
            $ownedLayoutRenderDetails = [Collections.Generic.List[string]]::new()
            . ([scriptblock]::Create($text.Substring($exitNode[0].Extent.EndOffset,
                $loop[0].Body.Extent.EndOffset - 1 - $exitNode[0].Extent.EndOffset)))
            . ([scriptblock]::Create((Get-CheckText 'render.owned-two-pane-layout-chrome')))
            $footer = Get-Assignment 'featherEvidenceStateConsistent'
            . ([scriptblock]::Create($text.Substring($footer.Extent.StartOffset)))
            exit 0
        }
        if ($consumer -ceq 'collection') {
            $checkText = Get-CheckText 'test.failure-reporting-regression'
            $startNode = Get-Assignment 'failureReportingScriptPath'
            $buildNode = Get-Assignment 'buildOutput'
            if ($startNode.Extent.StartOffset -ge $buildNode.Extent.StartOffset) { throw 'Regression is not prebuild.' }
            $end = $text.IndexOf($checkText, $startNode.Extent.EndOffset, [StringComparison]::Ordinal)
            if ($end -lt 0) { throw 'Missing prebuild collection sink.' }
            # A scratch-only child script substitutes just the regression producer.
            # The real launch, exit capture, admission, check and footer still execute.
            $collectionDirectory = Join-Path (Split-Path $FixturePath) $fixture.name
            $null = New-Item -ItemType Directory -Path $collectionDirectory -Force
            $stubPath = Join-Path $collectionDirectory 'Test-Windows10NeuralVoidPreviewFailureReporting.ps1'
            if ($fixture.sentinel -cne 'missing') {
                [IO.File]::WriteAllText($stubPath, $fixture.output)
            }
            $footer = Get-Assignment 'featherEvidenceStateConsistent'
            # A real file supplies PowerShell's automatic script identity variables.
            $consumerPath = Join-Path $collectionDirectory 'consumer.ps1'
            [IO.File]::WriteAllText($consumerPath,
                $text.Substring($startNode.Extent.StartOffset, $end + $checkText.Length - $startNode.Extent.StartOffset) +
                [Environment]::NewLine + $text.Substring($footer.Extent.StartOffset))
            $global:LASTEXITCODE = 0
            . $consumerPath
            exit $LASTEXITCODE
        }
        $exitAssignment = Get-Assignment $exitVariable
        $pngAssignments = @($ast.FindAll({param($n)
            $n -is [Management.Automation.Language.AssignmentStatementAst] -and
            $n.Left.Extent.Text -ceq $pngVariable
        }.GetNewClosure(), $true) | Sort-Object { $_.Extent.StartOffset })
        if ($pngAssignments.Count -ne 2) { throw 'PNG seam selection changed.' }
        $pngAssignment = $pngAssignments[0]
        # Dot-source in this scope so the actual admission variables reach the predicate.
        . ([scriptblock]::Create($text.Substring($exitAssignment.Extent.EndOffset,
            $pngAssignment.Extent.StartOffset - $exitAssignment.Extent.EndOffset)))
        $predicate = Get-Assignment $predicateVariable
        if ($fixture.mode -ceq 'predicate') {
            . ([scriptblock]::Create($predicate.Extent.Text))
            $structural = if ($isTaskManager) { $taskManagerStructuralRenderPassed } else { $structuralCasePassed }
            [ordered]@{ evidenceKind = 'fixture-real-consumer-predicate'; structuralPassed = $structural } |
                ConvertTo-Json -Depth 12
            exit 0
        }
        if ($isTaskManager) {
            $taskManagerRenderAttempted = $true
            $checkText = Get-CheckText 'render.owned-task-manager-evidence'
            $end = $text.IndexOf($checkText, $predicate.Extent.EndOffset, [StringComparison]::Ordinal)
            if ($end -lt 0) { throw 'Missing Task Manager report sink.' }
            . ([scriptblock]::Create($text.Substring($predicate.Extent.StartOffset,
                $end + $checkText.Length - $predicate.Extent.StartOffset)))
            $footer = Get-Assignment 'featherEvidenceStateConsistent'
            . ([scriptblock]::Create($text.Substring($footer.Extent.StartOffset)))
            exit 0
        }
        $featherRenderAttempted = $true
        $detail = @($ast.FindAll({param($n)
            $n -is [Management.Automation.Language.InvokeMemberExpressionAst] -and
            $n.Extent.Text.StartsWith('$renderDetails.Add(')
        }, $true))
        if ($detail.Count -ne 1) { throw 'Feather detail sink selection not unique.' }
        . ([scriptblock]::Create($text.Substring($predicate.Extent.StartOffset,
            $detail[0].Extent.EndOffset - $predicate.Extent.StartOffset)))
        . ([scriptblock]::Create((Get-CheckText 'render.feather-current-evidence')))
        $footer = Get-Assignment 'featherEvidenceStateConsistent'
        . ([scriptblock]::Create($text.Substring($footer.Extent.StartOffset)))
    }
    catch {
        [ordered]@{ evidenceKind = 'fixture-consumer-exception'; errorId = $_.FullyQualifiedErrorId
            message = $_.Exception.Message; statement = $_.InvocationInfo.Line } | ConvertTo-Json -Depth 12
        exit 97
    }
    exit 0
}

$fixtures = [Collections.Generic.List[object]]::new()
function Add-Fixture {
    param([string]$Name, [string]$Mode, [string]$Output, [int]$ExitCode = 0,
        [bool]$Expected = $false, [bool]$PngValid = $true, [string]$Sentinel = '')
    $fixtures.Add([pscustomobject]@{ evidenceKind = 'deterministic-test-input-not-product-output'
        name = $Name; mode = $Mode; output = $Output; exitCode = $ExitCode
        expected = $Expected; pngValid = $PngValid; sentinel = $Sentinel })
}
$valid = @{
    schemaVersion = 1; receiptType = 'fixture-render'; result = 'rendered-own-process-preview'
    accentHex = '#FFF000'; surfaceState = 'desktop-preview'; width = 1600; height = 900
    desktopContainsDeviceUi = $false; ownProcessOnly = $true; shellMutationSupported = $false
    deviceIntegrationSupported = $false; activationPermitted = $false; liveExplorer = 'not-run'
    mutationPerformed = $false; readOnlyProcessTelemetry = $true; processControlSupported = $false
    failures = @()
}
Add-Fixture 'feather.valid-typed' 'predicate' ($valid | ConvertTo-Json -Depth 12) -Expected $true
foreach ($pair in @(@('empty', ''), @('invalid-json', '{bad'), @('null', 'null'), @('partial', '{}'))) {
    Add-Fixture "feather.report-$($pair[0])" 'report' $pair[1] -ExitCode 12 -PngValid $false
}
$blocked = $valid.Clone(); $blocked.result = 'blocked'; $blocked.accentHex = ''
$blocked.failures = @('fixture-producer-failure-sentinel')
Add-Fixture 'feather.report-blocked' 'report' ($blocked | ConvertTo-Json -Depth 12) -ExitCode 12 -PngValid $false -Sentinel 'fixture-producer-failure-sentinel'
$missing = $valid.Clone(); $missing.Remove('accentHex')
Add-Fixture 'feather.report-missing-accent' 'report' ($missing | ConvertTo-Json -Depth 12) -PngValid $false
foreach ($shape in @('array', 'string', 'number')) {
    $output = switch ($shape) {
        'array' { '[' + ($valid | ConvertTo-Json -Depth 12 -Compress) + ']' }
        'string' { '"fixture-scalar"' }
        'number' { '42' }
    }
    Add-Fixture "feather.root-$shape" 'predicate' $output
}
foreach ($key in @($valid.Keys)) {
    $copy = $valid.Clone(); $copy.Remove($key)
    Add-Fixture "feather.missing-$key" 'predicate' ($copy | ConvertTo-Json -Depth 12)
}
foreach ($key in @('desktopContainsDeviceUi', 'ownProcessOnly', 'shellMutationSupported',
        'deviceIntegrationSupported', 'activationPermitted', 'mutationPerformed')) {
    foreach ($kind in @('string', 'null', 'number', 'array')) {
        $copy = $valid.Clone()
        $copy[$key] = switch ($kind) { 'string' { 'false' }; 'null' { $null }; 'number' { 0 }; 'array' { ,@($false) } }
        Add-Fixture "feather.type-$key-$kind" 'predicate' ($copy | ConvertTo-Json -Depth 12)
    }
}
foreach ($pair in @(@('accentHex', 7), @('liveExplorer', @('not-run')), @('width', '1600'),
        @('schemaVersion', 1.5), @('failures', 'not-an-array'), @('failures', @(3)))) {
    $copy = $valid.Clone(); $copy[$pair[0]] = $pair[1]
    Add-Fixture "feather.type-extra-$($fixtures.Count)" 'predicate' ($copy | ConvertTo-Json -Depth 12)
}

# Reuse identical malformed-input contracts through the second real consumer.
foreach ($original in @($fixtures)) {
    Add-Fixture ($original.name.Replace('feather.', 'task-manager.')) $original.mode `
        ($original.output.Replace('desktop-preview', 'owned-task-manager-preview')) `
        -ExitCode $original.exitCode -Expected $original.expected -PngValid $original.pngValid -Sentinel $original.sentinel
}
$taskValid = $valid.Clone(); $taskValid.surfaceState = 'owned-task-manager-preview'
foreach ($key in @('readOnlyProcessTelemetry', 'processControlSupported')) {
    foreach ($kind in @('string', 'null', 'number', 'array')) {
        $copy = $taskValid.Clone()
        $copy[$key] = switch ($kind) { 'string' { 'false' }; 'null' { $null }; 'number' { 0 }; 'array' { ,@($false) } }
        Add-Fixture "task-manager.type-$key-$kind" 'predicate' ($copy | ConvertTo-Json -Depth 12)
    }
}
$missing = $taskValid.Clone(); $missing.Remove('failures'); $missing.result = 'blocked'
Add-Fixture 'task-manager.report-missing-failures' 'report' ($missing | ConvertTo-Json -Depth 12) -ExitCode 12 -PngValid $false

foreach ($consumer in @($scenarioConsumers.Keys | Sort-Object)) {
    $spec = $scenarioConsumers[$consumer]
    $count = $spec[3]
    $scenarioValid = @{
        schemaVersion = $spec[4]; receiptType = 'fixture-scenarios'; result = 'passed'
        scenarioCount = $count; passedCount = $count
        scenarios = @(foreach ($n in 1..$count) { @{ name = "fixture-scenario-$n"; passed = $true; detail = 'fixture-detail' } })
    }
    $flags = switch ($consumer) {
        vector { @{ shellMutationSupported = $false; deviceIntegrationSupported = $false; activationPermitted = $false; mutationPerformed = $false; liveExplorer = 'not-run' } }
        edge { @{ ownProcessOnly = $true; shellMutationSupported = $false; liveExplorer = 'not-run' } }
        explorer { @{ ownProcessOnly = $true; operatingSystemLaunchSupported = $false; realFileSystemAccessSupported = $false } }
        'task-surface' { @{ ownProcessOnly = $true; readOnlyProcessTelemetry = $true; processControlSupported = $false; samplingStopsWhenHidden = $true; liveExplorer = 'not-run'; mutationPerformed = $false } }
        'owned-layout' { @{ ownProcessOnly = $true; thirdPartyWindowControlSupported = $false; shellMutationSupported = $false; liveExplorer = 'not-run'; mutationPerformed = $false } }
    }
    foreach ($key in $flags.Keys) { $scenarioValid[$key] = $flags[$key] }
    if ($consumer -ceq 'owned-layout') { $scenarioValid.receiptType = 'jarvisv2-win10-owned-window-layout-test' }
    $json = $scenarioValid | ConvertTo-Json -Depth 12
    Add-Fixture "$consumer.valid" 'scenario' $json -Expected $true
    foreach ($pair in @(@('empty', ''), @('invalid-json', '{bad'), @('null', 'null'), @('partial', '{}'), @('array', ('[' + $json + ']')))) {
        Add-Fixture "$consumer.$($pair[0])" 'scenario' $pair[1] -ExitCode 12
    }
    Add-Fixture "$consumer.nonzero-valid" 'scenario' $json -ExitCode 12
    foreach ($key in @($scenarioValid.Keys | Sort-Object)) {
        $copy = $scenarioValid.Clone(); $copy.Remove($key)
        Add-Fixture "$consumer.missing-$key" 'scenario' ($copy | ConvertTo-Json -Depth 12)
    }
    foreach ($pair in @(@('scenarioCount', "$count"), @('passedCount', 1.5), @('schemaVersion', '1'), @('result', @('passed')))) {
        $copy = $scenarioValid.Clone(); $copy[$pair[0]] = $pair[1]
        Add-Fixture "$consumer.type-$($pair[0])" 'scenario' ($copy | ConvertTo-Json -Depth 12)
    }
    $boolKey = @($flags.Keys | Where-Object { $flags[$_] -is [bool] } | Sort-Object)[0]
    foreach ($kind in @('string', 'null', 'number', 'array')) {
        $copy = $scenarioValid.Clone()
        $copy[$boolKey] = switch ($kind) { 'string' { 'false' }; 'null' { $null }; 'number' { 0 }; 'array' { ,@($false) } }
        Add-Fixture "$consumer.bool-$kind" 'scenario' ($copy | ConvertTo-Json -Depth 12)
    }
    foreach ($kind in @('missing-name', 'missing-passed', 'missing-detail', 'type-name', 'type-passed', 'type-detail', 'not-object', 'not-array', 'duplicate', 'failure')) {
        $copy = $json | ConvertFrom-Json -AsHashtable
        switch ($kind) {
            'missing-name' { $copy.scenarios[0].Remove('name') | Out-Null }
            'missing-passed' { $copy.scenarios[0].Remove('passed') | Out-Null }
            'missing-detail' { $copy.scenarios[0].Remove('detail') | Out-Null }
            'type-name' { $copy.scenarios[0].name = 3 }
            'type-passed' { $copy.scenarios[0].passed = 'false' }
            'type-detail' { $copy.scenarios[0].detail = $null }
            'not-object' { $copy.scenarios[0] = 'fixture-scalar' }
            'not-array' { $copy.scenarios = $copy.scenarios[0] }
            'duplicate' { $copy.scenarios[1].name = $copy.scenarios[0].name }
            'failure' { $copy.scenarios[0].passed = $false; $copy.scenarios[0].name = 'fixture-failed-scenario-sentinel'; $copy.scenarios[0].detail = 'fixture-full-detail-sentinel'; $copy.passedCount = $count - 1; $copy.result = 'failed' }
        }
        $sentinel = if ($kind -ceq 'failure') { 'fixture-full-detail-sentinel' } else { '' }
        Add-Fixture "$consumer.scenario-$kind" 'scenario' ($copy | ConvertTo-Json -Depth 12) -Sentinel $sentinel
    }
}
foreach ($preset in @('EqualColumns', 'EqualRows')) {
    $layoutValid = $taskValid.Clone(); $layoutValid.surfaceState = 'owned-window-layout-preview'
    Add-Fixture "layout-render.$preset-valid" 'layout' ($layoutValid | ConvertTo-Json -Depth 12) -Expected $true -Sentinel $preset
    foreach ($pair in @(@('empty', ''), @('partial', '{}'), @('array', ('[' + ($layoutValid | ConvertTo-Json -Depth 12) + ']')))) {
        Add-Fixture "layout-render.$preset-$($pair[0])" 'layout' $pair[1] -ExitCode 12 -Sentinel $preset
    }
    foreach ($key in @('width', 'ownProcessOnly', 'processControlSupported', 'surfaceState')) {
        $copy = $layoutValid.Clone(); $copy.Remove($key)
        Add-Fixture "layout-render.$preset-missing-$key" 'layout' ($copy | ConvertTo-Json -Depth 12) -Sentinel $preset
    }
}
# Explicit collection negative controls never overwrite the repository test.
$collectionReceipt = @{
    schemaVersion = 1; receiptType = 'jarvisv2-preview-failure-reporting-regression'
    evidenceKind = 'deterministic-real-consumer-fixtures-not-product-execution'; result = 'passed'
    testCount = 1; passedCount = 1; tests = @(@{ name = 'fixture-child-test'; passed = $true; detail = '' })
    gateSha256 = (Get-FileHash -LiteralPath $GatePath -Algorithm SHA256).Hash
}
foreach ($kind in @('valid', 'missing', 'invalid-json', 'nonzero', 'empty-tests', 'wrong-count', 'string-bool')) {
    $copy = $collectionReceipt.Clone()
    if ($kind -ceq 'empty-tests') { $copy.tests = @(); $copy.testCount = 0; $copy.passedCount = 0 }
    if ($kind -ceq 'wrong-count') { $copy.testCount = 2 }
    if ($kind -ceq 'string-bool') { $copy.tests = @(@{ name = 'fixture-child-test'; passed = 'true'; detail = '' }) }
    $output = $copy | ConvertTo-Json -Depth 12 -Compress
    $stub = "[Console]::OutputEncoding = [Text.UTF8Encoding]::new(`$false)`n" +
        "`$receipt = ConvertFrom-Json -InputObject '$($output.Replace("'", "''"))'`n" +
        "`$receipt.gateSha256 = (Get-FileHash -LiteralPath (Join-Path `$PSScriptRoot 'consumer.ps1') -Algorithm SHA256).Hash`n" +
        "`$receipt | Add-Member -NotePropertyName testSha256 -NotePropertyValue (Get-FileHash -LiteralPath `$PSCommandPath -Algorithm SHA256).Hash`n" +
        "`$receipt | ConvertTo-Json -Depth 12`n"
    if ($kind -ceq 'invalid-json') { $stub = "Write-Output '{bad'" }
    if ($kind -ceq 'nonzero') { $stub += 'exit 23' }
    Add-Fixture "collection.$kind" 'collection' $stub -Expected ($kind -ceq 'valid') -Sentinel $kind
}

if (-not $EvidenceDirectory) {
    $EvidenceDirectory = Join-Path ([IO.Path]::GetTempPath()) ('jarvis-preview-report-tests-' + [guid]::NewGuid().ToString('N'))
}
$null = New-Item -ItemType Directory -Path $EvidenceDirectory -Force
$results = [Collections.Generic.List[object]]::new()
foreach ($fixture in $fixtures) {
    $path = Join-Path $EvidenceDirectory ($fixture.name + '.input.json')
    $fixture | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding utf8
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME 'pwsh.exe'))
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile', '-File', $PSCommandPath, '-GatePath', $GatePath, '-FixturePath', $path)) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $EvidenceDirectory ($fixture.name + '.stdout.txt')), $stdout)
    [IO.File]::WriteAllText((Join-Path $EvidenceDirectory ($fixture.name + '.stderr.txt')), $stderr)
    $ok = $false; $reason = ''
    try {
        $actual = ConvertFrom-Json -InputObject $stdout -NoEnumerate
        if ($fixture.mode -ceq 'predicate') {
            $ok = $process.ExitCode -eq 0 -and $stderr.Length -eq 0 -and
                $actual.structuralPassed -is [bool] -and $actual.structuralPassed -eq $fixture.expected
        }
        else {
            $isTaskManager = $fixture.name.StartsWith('task-manager.')
            $checkName = if ($isTaskManager) { 'render.owned-task-manager-evidence' } else { 'render.feather-current-evidence' }
            $command = if ($isTaskManager) { 'render-task-manager' } else { 'render' }
            $consumer = $fixture.name.Split('.')[0]
            if ($scenarioConsumers.ContainsKey($consumer)) { $checkName = $scenarioConsumers[$consumer][2]; $command = $scenarioConsumers[$consumer][1] }
            if ($consumer -ceq 'layout-render') { $checkName = 'render.owned-two-pane-layout-chrome'; $command = 'render-owned-layout' }
            if ($consumer -ceq 'collection') { $checkName = 'test.failure-reporting-regression'; $command = 'Test-Windows10NeuralVoidPreviewFailureReporting.ps1' }
            $check = @($actual.checks | Where-Object name -CEQ $checkName)
            $expectedExit = if ($fixture.expected) { 0 } else { 1 }
            $expectedResult = if ($fixture.expected) { 'passed' } else { 'failed' }
            $ok = $process.ExitCode -eq $expectedExit -and $stderr.Length -eq 0 -and $actual.result -ceq $expectedResult -and
                $check.Count -eq 1 -and $check[0].passed -eq $fixture.expected -and
                $actual.readyForStructuralReview -eq $fixture.expected -and -not $actual.readyForVisualReview -and
                -not $actual.readyForCanonicalPixelPublication -and
                $actual.checkCount -eq @($actual.checks).Count -and
                $check[0].detail.Contains('combinedOutput') -and $check[0].detail.Contains('parseError') -and
                $check[0].detail.Contains('schemaErrors') -and $check[0].detail.Contains('"command":"' + $command + '"') -and
                ($consumer -ceq 'collection' -or $check[0].detail.Contains('"exitCode":' + $fixture.exitCode)) -and
                ($consumer -in @('collection', 'layout-render') -or -not $fixture.sentinel -or $check[0].detail.Contains($fixture.sentinel))
            if ($ok -and $consumer -ne 'collection') {
                # Decode the embedded diagnostic and require the original combined text exactly.
                $match = [regex]::Match($check[0].detail, '(?s)\{"command":.*\}')
                $diagnostic = ConvertFrom-Json -InputObject $match.Value -NoEnumerate
                $ok = $diagnostic.command -ceq $command -and $diagnostic.exitCode -eq $fixture.exitCode -and
                    $diagnostic.combinedOutput -ceq $fixture.output
                if ($fixture.sentinel -ceq 'fixture-full-detail-sentinel') {
                    $ok = $ok -and $diagnostic.producer.failedScenarios.Count -eq 1 -and
                        $diagnostic.producer.failedScenarios[0].name -ceq 'fixture-failed-scenario-sentinel' -and
                        $diagnostic.producer.failedScenarios[0].detail -ceq $fixture.sentinel
                }
            }
            if ($ok -and $consumer -ceq 'collection') {
                $diagnostic = ConvertFrom-Json -InputObject $check[0].detail -NoEnumerate
                $childExit = switch ($fixture.sentinel) { 'missing' { -1 }; 'nonzero' { 23 }; default { 0 } }
                $ok = $diagnostic.exitCode -eq $childExit -and $diagnostic.combinedOutput.Length -gt 0
            }
        }
        if (-not $ok) { $reason = 'Consumer result violated the fixture contract.' }
    }
    catch { $reason = $_.Exception.Message }
    $results.Add([pscustomobject]@{ name = $fixture.name; passed = $ok; detail = $reason
        exitCode = $process.ExitCode; evidenceKind = 'fixture-not-product'; inputPath = $path })
    $process.Dispose()
}
$failed = @($results | Where-Object { -not $_.passed })
[ordered]@{
    schemaVersion = 1; receiptType = 'jarvisv2-preview-failure-reporting-regression'
    evidenceKind = 'deterministic-real-consumer-fixtures-not-product-execution'
    result = if ($failed.Count -eq 0) { 'passed' } else { 'failed' }
    testCount = $results.Count; passedCount = $results.Count - $failed.Count
    gateSha256 = (Get-FileHash -LiteralPath $GatePath -Algorithm SHA256).Hash
    testSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    evidenceDirectory = $EvidenceDirectory; tests = @($results)
} | ConvertTo-Json -Depth 20
if ($failed.Count -ne 0) { exit 1 }
