[CmdletBinding()]
param(
    [ValidateRange(1, 3600)]
    [int]$DurationSeconds = 60,

    [ValidateRange(250, 5000)]
    [int]$IntervalMilliseconds = 1000,

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$supervisorDll = Join-Path $root (
    'src\Jarvis.Supervisor\bin\Release\net8.0-windows\' +
    'jarvis-supervisor.dll'
)
$allowedOutputRoot =
    Join-Path $root 'artifacts\m2-host-baseline\runs'
$stateRoot =
    Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'JARVIS2'
$killSwitchPath = Join-Path $stateRoot 'disabled.flag'
$permitPath = Join-Path $stateRoot 'active-module.txt'

if (-not (Test-Path -LiteralPath $killSwitchPath -PathType Leaf)) {
    throw 'The locked pre-activation baseline requires disabled.flag.'
}
if (Test-Path -LiteralPath $permitPath -PathType Leaf) {
    throw 'The locked pre-activation baseline refuses an active-module permit.'
}
if (-not (Test-Path -LiteralPath $supervisorDll -PathType Leaf)) {
    throw 'Build Jarvis.Supervisor Release before measuring the host baseline.'
}

$inspectOutput = & dotnet $supervisorDll inspect 2>&1
if ($LASTEXITCODE -ne 0) {
    throw 'Supervisor inspect failed; baseline sampling was not started.'
}
$report =
    (($inspectOutput | ForEach-Object { [string]$_ }) -join
        [Environment]::NewLine) |
        ConvertFrom-Json -Depth 100
if (-not $report.compatible -or
    $report.host.killSwitchState -ne 'armed' -or
    $report.host.activeModuleState -ne 'absent') {
    throw 'The host is not in the compatible locked state.'
}
$shellIds = @($report.host.explorerProcessIds)
if ($shellIds.Count -ne 1) {
    throw 'Expected one verified desktop Shell process.'
}
$shellProcessId = [int]$shellIds[0]
$logicalProcessorCount = [Environment]::ProcessorCount
$samples = [System.Collections.Generic.List[object]]::new()
$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$previousCpuMilliseconds = $null
$previousElapsedMilliseconds = $null

while ($stopwatch.Elapsed.TotalSeconds -lt $DurationSeconds -or
       $samples.Count -eq 0) {
    $process = Get-Process -Id $shellProcessId -ErrorAction Stop
    $process.Refresh()
    $elapsedMilliseconds = $stopwatch.Elapsed.TotalMilliseconds
    $cpuMilliseconds = $process.TotalProcessorTime.TotalMilliseconds
    $cpuPercent = 0.0
    if ($null -ne $previousCpuMilliseconds -and
        $elapsedMilliseconds -gt $previousElapsedMilliseconds) {
        $cpuPercent = (
            ($cpuMilliseconds - $previousCpuMilliseconds) /
            ($elapsedMilliseconds - $previousElapsedMilliseconds) /
            $logicalProcessorCount *
            100.0
        )
    }
    $samples.Add([ordered]@{
        offsetMilliseconds = [math]::Round($elapsedMilliseconds, 3)
        cpuPercent = [math]::Round([math]::Max(0.0, $cpuPercent), 6)
        workingSetBytes = [int64]$process.WorkingSet64
        privateMemoryBytes = [int64]$process.PrivateMemorySize64
        handleCount = [int]$process.HandleCount
        threadCount = @($process.Threads).Count
    })
    $previousCpuMilliseconds = $cpuMilliseconds
    $previousElapsedMilliseconds = $elapsedMilliseconds
    if ($stopwatch.Elapsed.TotalSeconds -lt $DurationSeconds) {
        Start-Sleep -Milliseconds $IntervalMilliseconds
    }
}
$stopwatch.Stop()

$cpuValues = @($samples | ForEach-Object { [double]$_['cpuPercent'] })
$workingSetValues =
    @($samples | ForEach-Object { [int64]$_['workingSetBytes'] })
$privateValues =
    @($samples | ForEach-Object { [int64]$_['privateMemoryBytes'] })
$handleValues =
    @($samples | ForEach-Object { [int]$_['handleCount'] })
$threadValues =
    @($samples | ForEach-Object { [int]$_['threadCount'] })
$receipt = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-m2-locked-host-baseline'
    measuredAtUtc = [DateTime]::UtcNow.ToString('o')
    phase = 'locked-pre-activation'
    mutationPerformed = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    killSwitchState = 'armed'
    activeModuleState = 'absent'
    explorerProcessId = $shellProcessId
    durationSeconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
    intervalMilliseconds = $IntervalMilliseconds
    logicalProcessorCount = $logicalProcessorCount
    sampleCount = $samples.Count
    summary = [ordered]@{
        averageCpuPercent =
            [math]::Round(
                ($cpuValues | Measure-Object -Average).Average,
                6)
        peakCpuPercent =
            [math]::Round(
                ($cpuValues | Measure-Object -Maximum).Maximum,
                6)
        minimumWorkingSetBytes =
            [int64]($workingSetValues | Measure-Object -Minimum).Minimum
        peakWorkingSetBytes =
            [int64]($workingSetValues | Measure-Object -Maximum).Maximum
        minimumPrivateMemoryBytes =
            [int64]($privateValues | Measure-Object -Minimum).Minimum
        peakPrivateMemoryBytes =
            [int64]($privateValues | Measure-Object -Maximum).Maximum
        minimumHandleCount =
            [int]($handleValues | Measure-Object -Minimum).Minimum
        peakHandleCount =
            [int]($handleValues | Measure-Object -Maximum).Maximum
        minimumThreadCount =
            [int]($threadValues | Measure-Object -Minimum).Minimum
        peakThreadCount =
            [int]($threadValues | Measure-Object -Maximum).Maximum
    }
    samples = @($samples)
}
$json = $receipt | ConvertTo-Json -Depth 12

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $candidate = if ([IO.Path]::IsPathRooted($OutputPath)) {
        [IO.Path]::GetFullPath($OutputPath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $root $OutputPath))
    }
    $allowed = [IO.Path]::GetFullPath($allowedOutputRoot).TrimEnd('\')
    if (-not $candidate.StartsWith(
            $allowed + '\',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputPath must stay under $allowed."
    }
    if (Test-Path -LiteralPath $candidate) {
        throw 'Refusing to overwrite an existing baseline receipt.'
    }
    $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $candidate))
    [IO.File]::WriteAllText(
        $candidate,
        $json + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

Write-Output $json
