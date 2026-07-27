[CmdletBinding()]
param(
    [string]$ToolCache = (
        Join-Path $env:LOCALAPPDATA 'JARVIS2\tool-cache\windhawk-1.7.3'
    ),
    [switch]$StaticOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root 'src\Jarvis.ExplorerTapReadOnly'
$transportRoot = Join-Path $root 'src\Jarvis.ExplorerTransportModel'
$headerPath = Join-Path $sourceRoot 'jarvis_explorer_tap_readonly.h'
$protocolPath = Join-Path $sourceRoot 'jarvis_explorer_tap_protocol.cpp'
$admissionHeaderPath = Join-Path $sourceRoot 'jarvis_explorer_tap_admission.h'
$admissionPath = Join-Path $sourceRoot 'jarvis_explorer_tap_admission.cpp'
$fingerprintHeaderPath = Join-Path $sourceRoot 'jarvis_explorer_tap_fingerprint.h'
$fingerprintPath = Join-Path $sourceRoot 'jarvis_explorer_tap_fingerprint.cpp'
$adapterHeaderPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_tap_inspectable_adapter.h'
$adapterPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_tap_inspectable_adapter.cpp'
$targetPath = Join-Path $sourceRoot 'jarvis_explorer_tap_target.cpp'
$tapPath = Join-Path $sourceRoot 'jarvis_explorer_tap_readonly.cpp'
$controllerPath = Join-Path $sourceRoot 'jarvis_explorer_tap_controller.cpp'
$harnessPath = Join-Path (
    $root
) 'tests\native\jarvis_explorer_tap_protocol_harness.cpp'
$contractPath = Join-Path (
    $root
) 'config\explorer-readonly-tap-build-contract.json'
$contractSchemaPath = Join-Path (
    $root
) 'config\explorer-readonly-tap-build-contract.schema.json'
$taskPath = Join-Path (
    $root
) 'docs\PHASE-12-EXPLORER-READONLY-TAP-OFFLINE-BUILD-TASK.md'
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) ("jarvis2-explorer-readonly-tap-" + [Guid]::NewGuid().ToString('N'))

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

function Get-UInt16 {
    param(
        [Parameter(Mandatory)] [byte[]]$Bytes,
        [Parameter(Mandatory)] [int]$Offset
    )
    if ($Offset -lt 0 -or $Offset + 2 -gt $Bytes.Length) {
        throw "16-bit read exceeds the PE image at offset $Offset."
    }
    return [BitConverter]::ToUInt16($Bytes, $Offset)
}

function Get-UInt32 {
    param(
        [Parameter(Mandatory)] [byte[]]$Bytes,
        [Parameter(Mandatory)] [int]$Offset
    )
    if ($Offset -lt 0 -or $Offset + 4 -gt $Bytes.Length) {
        throw "32-bit read exceeds the PE image at offset $Offset."
    }
    return [BitConverter]::ToUInt32($Bytes, $Offset)
}

function Get-UInt64 {
    param(
        [Parameter(Mandatory)] [byte[]]$Bytes,
        [Parameter(Mandatory)] [int]$Offset
    )
    if ($Offset -lt 0 -or $Offset + 8 -gt $Bytes.Length) {
        throw "64-bit read exceeds the PE image at offset $Offset."
    }
    return [BitConverter]::ToUInt64($Bytes, $Offset)
}

function Resolve-PeRva {
    param(
        [Parameter(Mandatory)] [uint32]$Rva,
        [Parameter(Mandatory)] [uint32]$RequiredBytes,
        [Parameter(Mandatory)] [object[]]$Sections,
        [Parameter(Mandatory)] [uint32]$SizeOfHeaders,
        [Parameter(Mandatory)] [int64]$FileLength
    )

    if (
        [uint64]$Rva + [uint64]$RequiredBytes -le
        [uint64]$SizeOfHeaders -and
        [uint64]$Rva + [uint64]$RequiredBytes -le
        [uint64]$FileLength
    ) {
        return [uint64]$Rva
    }

    foreach ($section in $Sections) {
        $span = [Math]::Max(
            [uint64]$section.virtualSize,
            [uint64]$section.sizeOfRawData
        )
        if (
            [uint64]$Rva -lt [uint64]$section.virtualAddress -or
            [uint64]$Rva + [uint64]$RequiredBytes -gt
                [uint64]$section.virtualAddress + $span
        ) {
            continue
        }
        $delta = [uint64]$Rva - [uint64]$section.virtualAddress
        if (
            $delta + [uint64]$RequiredBytes -gt
                [uint64]$section.sizeOfRawData
        ) {
            throw "RVA 0x$($Rva.ToString('X8')) has no complete file backing."
        }
        $offset = [uint64]$section.pointerToRawData + $delta
        if ($offset + [uint64]$RequiredBytes -gt [uint64]$FileLength) {
            throw "RVA 0x$($Rva.ToString('X8')) exceeds the PE file."
        }
        return $offset
    }
    throw "RVA 0x$($Rva.ToString('X8')) isn't mapped by the PE image."
}

function Read-AsciiZ {
    param(
        [Parameter(Mandatory)] [byte[]]$Bytes,
        [Parameter(Mandatory)] [uint64]$Offset,
        [int]$MaximumLength = 4096
    )

    $value = [Collections.Generic.List[byte]]::new()
    for ($index = 0; $index -lt $MaximumLength; $index++) {
        $current = $Offset + [uint64]$index
        if ($current -ge [uint64]$Bytes.LongLength) {
            throw 'ASCII string exceeds the PE file.'
        }
        $character = $Bytes[[int]$current]
        if ($character -eq 0) {
            return [Text.Encoding]::ASCII.GetString($value.ToArray())
        }
        if ($character -gt 0x7F) {
            throw 'PE import/export name contains non-ASCII data.'
        }
        $value.Add($character)
    }
    throw "PE import/export name exceeds $MaximumLength bytes."
}

function Get-PeMetadata {
    param([Parameter(Mandatory)] [string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if (
        -not [BitConverter]::IsLittleEndian -or
        $bytes.Length -lt 64 -or
        (Get-UInt16 -Bytes $bytes -Offset 0) -ne 0x5A4D
    ) {
        throw "$Path isn't a little-endian PE image."
    }

    $peOffset = Get-UInt32 -Bytes $bytes -Offset 0x3C
    if (
        [uint64]$peOffset + 24U -gt [uint64]$bytes.LongLength -or
        (Get-UInt32 -Bytes $bytes -Offset ([int]$peOffset)) -ne 0x00004550
    ) {
        throw "$Path has an invalid PE signature."
    }

    $fileHeader = [int]$peOffset + 4
    $machine = Get-UInt16 -Bytes $bytes -Offset $fileHeader
    $sectionCount = Get-UInt16 -Bytes $bytes -Offset ($fileHeader + 2)
    $optionalSize = Get-UInt16 -Bytes $bytes -Offset ($fileHeader + 16)
    $characteristics = Get-UInt16 -Bytes $bytes -Offset ($fileHeader + 18)
    $optionalHeader = $fileHeader + 20
    if (
        $machine -ne 0x8664 -or
        $sectionCount -lt 1 -or
        $sectionCount -gt 96 -or
        $optionalSize -ne 0xF0 -or
        (Get-UInt16 -Bytes $bytes -Offset $optionalHeader) -ne 0x20B
    ) {
        throw "$Path isn't an expected AMD64 PE32+ image."
    }

    $sizeOfHeaders = Get-UInt32 -Bytes $bytes -Offset ($optionalHeader + 60)
    $sectionOffset = $optionalHeader + $optionalSize
    if (
        [uint64]$sectionOffset + ([uint64]$sectionCount * 40U) -gt
        [uint64]$bytes.LongLength
    ) {
        throw "$Path has truncated section headers."
    }

    $sections = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $sectionCount; $index++) {
        $offset = $sectionOffset + ($index * 40)
        $section = [pscustomobject]@{
            virtualSize = Get-UInt32 -Bytes $bytes -Offset ($offset + 8)
            virtualAddress = Get-UInt32 -Bytes $bytes -Offset ($offset + 12)
            sizeOfRawData = Get-UInt32 -Bytes $bytes -Offset ($offset + 16)
            pointerToRawData = Get-UInt32 -Bytes $bytes -Offset ($offset + 20)
        }
        if (
            [uint64]$section.pointerToRawData +
                [uint64]$section.sizeOfRawData -gt
            [uint64]$bytes.LongLength
        ) {
            throw "$Path has section data outside the file."
        }
        $sections.Add($section)
    }

    $exports = [Collections.Generic.List[string]]::new()
    $exportRva = Get-UInt32 -Bytes $bytes -Offset ($optionalHeader + 112)
    $exportSize = Get-UInt32 -Bytes $bytes -Offset ($optionalHeader + 116)
    if ($exportRva -ne 0U) {
        if ($exportSize -lt 40U) {
            throw "$Path has a truncated export directory."
        }
        $exportOffset = Resolve-PeRva `
            -Rva $exportRva `
            -RequiredBytes 40 `
            -Sections $sections.ToArray() `
            -SizeOfHeaders $sizeOfHeaders `
            -FileLength $bytes.LongLength
        $functionCount = Get-UInt32 `
            -Bytes $bytes `
            -Offset ([int]$exportOffset + 20)
        $nameCount = Get-UInt32 `
            -Bytes $bytes `
            -Offset ([int]$exportOffset + 24)
        $namesRva = Get-UInt32 `
            -Bytes $bytes `
            -Offset ([int]$exportOffset + 32)
        $ordinalsRva = Get-UInt32 `
            -Bytes $bytes `
            -Offset ([int]$exportOffset + 36)
        if (
            $nameCount -gt $functionCount -or
            $nameCount -gt 4096U
        ) {
            throw "$Path has unreasonable export counts."
        }
        if ($nameCount -ne 0U) {
            $namesOffset = Resolve-PeRva `
                -Rva $namesRva `
                -RequiredBytes ([uint32]($nameCount * 4U)) `
                -Sections $sections.ToArray() `
                -SizeOfHeaders $sizeOfHeaders `
                -FileLength $bytes.LongLength
            $null = Resolve-PeRva `
                -Rva $ordinalsRva `
                -RequiredBytes ([uint32]($nameCount * 2U)) `
                -Sections $sections.ToArray() `
                -SizeOfHeaders $sizeOfHeaders `
                -FileLength $bytes.LongLength
            for ($index = 0U; $index -lt $nameCount; $index++) {
                $nameRva = Get-UInt32 `
                    -Bytes $bytes `
                    -Offset ([int]$namesOffset + ([int]$index * 4))
                $nameOffset = Resolve-PeRva `
                    -Rva $nameRva `
                    -RequiredBytes 1 `
                    -Sections $sections.ToArray() `
                    -SizeOfHeaders $sizeOfHeaders `
                    -FileLength $bytes.LongLength
                $exports.Add(
                    (Read-AsciiZ -Bytes $bytes -Offset $nameOffset)
                )
            }
        }
    }

    $imports = [Collections.Generic.List[string]]::new()
    $importDlls = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    $importRva = Get-UInt32 -Bytes $bytes -Offset ($optionalHeader + 120)
    $importSize = Get-UInt32 -Bytes $bytes -Offset ($optionalHeader + 124)
    if ($importRva -ne 0U) {
        if ($importSize -lt 20U) {
            throw "$Path has a truncated import directory."
        }
        $descriptorOffset = Resolve-PeRva `
            -Rva $importRva `
            -RequiredBytes 20 `
            -Sections $sections.ToArray() `
            -SizeOfHeaders $sizeOfHeaders `
            -FileLength $bytes.LongLength
        $descriptorLimit = [Math]::Min(
            1024,
            [Math]::Max(1, [int]($importSize / 20U))
        )
        for ($descriptorIndex = 0;
             $descriptorIndex -lt $descriptorLimit;
             $descriptorIndex++) {
            $current = $descriptorOffset + [uint64]($descriptorIndex * 20)
            if ($current + 20U -gt [uint64]$bytes.LongLength) {
                throw "$Path has an import descriptor outside the file."
            }
            $originalThunkRva = Get-UInt32 `
                -Bytes $bytes `
                -Offset ([int]$current)
            $nameRva = Get-UInt32 `
                -Bytes $bytes `
                -Offset ([int]$current + 12)
            $firstThunkRva = Get-UInt32 `
                -Bytes $bytes `
                -Offset ([int]$current + 16)
            if (
                $originalThunkRva -eq 0U -and
                $nameRva -eq 0U -and
                $firstThunkRva -eq 0U
            ) {
                break
            }
            if ($nameRva -eq 0U -or $firstThunkRva -eq 0U) {
                throw "$Path has an incomplete import descriptor."
            }
            $nameOffset = Resolve-PeRva `
                -Rva $nameRva `
                -RequiredBytes 1 `
                -Sections $sections.ToArray() `
                -SizeOfHeaders $sizeOfHeaders `
                -FileLength $bytes.LongLength
            $dllName = Read-AsciiZ -Bytes $bytes -Offset $nameOffset
            $null = $importDlls.Add($dllName)
            $thunkRva = if ($originalThunkRva -ne 0U) {
                $originalThunkRva
            }
            else {
                $firstThunkRva
            }
            for ($thunkIndex = 0; $thunkIndex -lt 65536; $thunkIndex++) {
                $entryRva64 = [uint64]$thunkRva +
                    ([uint64]$thunkIndex * 8U)
                if ($entryRva64 -gt [uint32]::MaxValue) {
                    throw "$Path has an overflowing import thunk."
                }
                $entryOffset = Resolve-PeRva `
                    -Rva ([uint32]$entryRva64) `
                    -RequiredBytes 8 `
                    -Sections $sections.ToArray() `
                    -SizeOfHeaders $sizeOfHeaders `
                    -FileLength $bytes.LongLength
                $entry = Get-UInt64 `
                    -Bytes $bytes `
                    -Offset ([int]$entryOffset)
                if ($entry -eq 0U) {
                    break
                }
                if ($entry -ge 0x8000000000000000) {
                    $functionName = "#ordinal-$($entry -band 0xFFFFU)"
                }
                else {
                    if ($entry -gt [uint32]::MaxValue) {
                        throw "$Path has a non-RVA import name."
                    }
                    $hintNameOffset = Resolve-PeRva `
                        -Rva ([uint32]$entry) `
                        -RequiredBytes 3 `
                        -Sections $sections.ToArray() `
                        -SizeOfHeaders $sizeOfHeaders `
                        -FileLength $bytes.LongLength
                    $functionName = Read-AsciiZ `
                        -Bytes $bytes `
                        -Offset ($hintNameOffset + 2U)
                }
                $imports.Add("${dllName}!${functionName}")
            }
        }
    }

    $exportNames = [string[]]$exports.ToArray()
    [Array]::Sort($exportNames, [StringComparer]::Ordinal)
    $importNames = [string[]]($imports | Sort-Object -Unique)
    $dllNames = [string[]]($importDlls | Sort-Object)
    return [pscustomobject]@{
        size = $bytes.LongLength
        sha256 = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($bytes)
        )
        machine = '0x8664'
        isDll = ($characteristics -band 0x2000) -ne 0
        exportNames = $exportNames
        importDlls = $dllNames
        imports = $importNames
    }
}

$headerText = [IO.File]::ReadAllText($headerPath)
$protocolText = [IO.File]::ReadAllText($protocolPath)
$admissionHeaderText = [IO.File]::ReadAllText($admissionHeaderPath)
$admissionText = [IO.File]::ReadAllText($admissionPath)
$fingerprintHeaderText = [IO.File]::ReadAllText($fingerprintHeaderPath)
$fingerprintText = [IO.File]::ReadAllText($fingerprintPath)
$adapterHeaderText = [IO.File]::ReadAllText($adapterHeaderPath)
$adapterText = [IO.File]::ReadAllText($adapterPath)
$targetText = [IO.File]::ReadAllText($targetPath)
$tapText = [IO.File]::ReadAllText($tapPath)
$controllerText = [IO.File]::ReadAllText($controllerPath)
$harnessText = [IO.File]::ReadAllText($harnessPath)
$contractText = [IO.File]::ReadAllText($contractPath)
$contractSchemaText = [IO.File]::ReadAllText($contractSchemaPath)
$taskText = [IO.File]::ReadAllText($taskPath)
$contract = $contractText | ConvertFrom-Json -Depth 100
$contractSchema = $contractSchemaText | ConvertFrom-Json -Depth 100
$allSourceText = @(
    $headerText,
    $protocolText,
    $admissionHeaderText,
    $admissionText,
    $fingerprintHeaderText,
    $fingerprintText,
    $adapterHeaderText,
    $adapterText,
    $targetText,
    $tapText,
    $controllerText,
    $harnessText
) -join [Environment]::NewLine

Add-Check `
    -Name 'machine-contract.offline-build-only-and-strict-schema' `
    -Passed (
        $contract.schemaVersion -eq 1 -and
        $contract.contractId -eq
            'jarvis-explorer-readonly-tap-offline-build-v1' -and
        $contract.lifecycleState -eq 'offline-build-only' -and
        $contract.tap.liveCompileSwitchValue -eq 0 -and
        $contract.tap.setSiteResult -eq 'E_ACCESSDENIED' -and
        -not $contract.tap.dllLoadedDuringValidation -and
        $contract.controller.mode -eq 'describe-only' -and
        $contract.controller.existingDiagnosticsConsumerPolicy -eq
            'reject' -and
        $contract.controller.endpointAttemptLimit -eq 0 -and
        -not $contract.controller.tapDllLoadSupported -and
        -not $contract.propertyReadSupported -and
        -not $contract.executionSupported -and
        -not $contract.readyForLiveConnection -and
        -not $contract.readyForExactApproval -and
        -not $contract.activationPermitted -and
        $contract.liveExplorer -eq 'not-run' -and
        -not $contract.mutationPerformed -and
        $contractSchema.'$schema' -eq
            'https://json-schema.org/draft/2020-12/schema' -and
        $contractSchema.additionalProperties -eq $false -and
        $contractSchemaText.Contains(
            '"const": "jarvis-explorer-readonly-tap-offline-build-v1"'
        )
    ) `
    -Detail (
        'The machine contract/schema must freeze the disk-only TAP, permanent ' +
        'SetSite refusal, zero endpoint/load support and all non-live claims.'
    )

Add-Check `
    -Name 'source.live-xaml-compile-gate-hard-disabled' `
    -Passed (
        $headerText.Contains(
            '#define JARVIS_ENABLE_LIVE_XAML_READONLY 0'
        ) -and
        $headerText.Contains(
            '#if JARVIS_ENABLE_LIVE_XAML_READONLY != 0'
        ) -and
        $headerText.Contains(
            '#error Phase 12 must be compiled with live XAML Diagnostics disabled.'
        ) -and
        $tapText.Contains(
            'static_assert(JARVIS_ENABLE_LIVE_XAML_READONLY == 0)'
        )
    ) `
    -Detail (
        'Phase 12 must fail compilation if a live XAML connection is enabled.'
    )

Add-Check `
    -Name 'protocol.canonical-fixed-width-bind-only' `
    -Passed (
        $headerText.Contains('L"JARVIS2-XAML-RO-V1:"') -and
        $headerText.Contains(
            'static_assert(JARVIS_TAP_INITIALIZATION_CHARS == 1251U)'
        ) -and
        $protocolText.Contains('JARVIS_TAP_PROTOCOL_RESULT_NONCANONICAL_HEX') -and
        $protocolText.Contains('ExactTitleHashMatches') -and
        $protocolText.Contains('.live_connection_compiled = 0U') -and
        $protocolText.Contains('.mutation_performed = 0U')
    ) `
    -Detail (
        'Initialization data must be one canonical uppercase encoding of the ' +
        'fixed Phase 11 bind request and every receipt must remain non-live.'
    )

Add-Check `
    -Name 'target.exact-current-process-window-and-start-time-only' `
    -Passed (
        $targetText.Contains('GetCurrentProcessId()') -and
        $targetText.Contains('GetShellWindow()') -and
        $targetText.Contains('GetWindowThreadProcessId(') -and
        $targetText.Contains('GetCurrentThreadId()') -and
        $targetText.Contains('L"CabinetWClass"') -and
        $targetText.Contains('L"C:\\"') -and
        $targetText.Contains('GetProcessTimes(')
    ) `
    -Detail (
        'The in-target verifier must bind the current process and one exact ' +
        'CabinetWClass C:\ window without process or window enumeration.'
    )

Add-Check `
    -Name 'tap.reviewed-com-surface-and-permanent-site-refusal' `
    -Passed (
        $tapText.Contains('IObjectWithSite') -and
        $tapText.Contains('IClassFactory') -and
        $tapText.Contains('DllGetClassObject(') -and
        $tapText.Contains('DllCanUnloadNow()') -and
        -not $tapText.Contains('DllRegisterServer') -and
        -not $tapText.Contains('DllMain') -and
        $tapText.Contains('return E_ACCESSDENIED;')
    ) `
    -Detail (
        'The disk-only TAP must expose only its class factory/unload surface ' +
        'and must reject every SetSite attempt in Phase 12.'
    )

$tapObservationForbidden = (
    '(?i)(?:\-\>|\.)\s*(?:GetInitializationData|AdviseVisualTreeChange|' +
    'UnadviseVisualTreeChange|GetIInspectableFromHandle|GetProperty|' +
    'SetProperty|ClearProperty|AddChild|RemoveChild|ReplaceResource)\s*\('
)
Add-Check `
    -Name 'tap.callback-observation-count-only' `
    -Passed (
        $tapText.Contains('IVisualTreeServiceCallback2') -and
        $tapText.Contains('event_count_.fetch_add(') -and
        -not [regex]::IsMatch($tapText, $tapObservationForbidden)
    ) `
    -Detail (
        'The compiled callback may only count bounded notifications; it may ' +
        'not obtain diagnostics, resolve objects, read properties or mutate.'
    )

Add-Check `
    -Name 'controller.describe-only-and-existing-consumer-reject' `
    -Passed (
        $controllerText.Contains('L"--describe"') -and
        $controllerText.Contains('\"phase12-describe-only\"') -and
        $controllerText.Contains(
            '\"existingDiagnosticsConsumerPolicy\":\"reject\"'
        ) -and
        $controllerText.Contains('\"endpointAttemptLimit\":0') -and
        $controllerText.Contains('\"tapDllLoadSupported\":false') -and
        -not $controllerText.Contains('InitializeXamlDiagnosticsEx') -and
        -not $controllerText.Contains('LoadLibrary') -and
        -not $controllerText.Contains('GetProcAddress')
    ) `
    -Detail (
        'The controller must accept no target or execution command, reject ' +
        'coexistence by policy, and advertise zero endpoint/load support.'
    )

$dangerousCallPattern = (
    '(?i)\b(?:InitializeXamlDiagnosticsEx|LoadLibrary|GetProcAddress|' +
    'EnumWindows|EnumChildWindows|OpenProcess|VirtualAllocEx|' +
    'WriteProcessMemory|CreateRemoteThread|SetWindowsHookEx|' +
    'UnhookWindowsHookEx|SendMessage|PostMessage|TerminateProcess|' +
    'ExitProcess|RegOpenKey|RegCreateKey|RegSetValue|StartService|' +
    'ControlService)\w*\s*\('
)
Add-Check `
    -Name 'source.no-loader-injection-hook-enumeration-or-system-mutation' `
    -Passed (-not [regex]::IsMatch($allSourceText, $dangerousCallPattern)) `
    -Detail (
        'Phase 12 source must contain no diagnostics call, DLL loader, ' +
        'process/window enumeration, injection, hook, termination, registry ' +
        'or service call.'
    )

Add-Check `
    -Name 'docs.build-is-not-live-readonly-validation' `
    -Passed (
        $taskText.Contains(
            'OFFLINE TAP BUILD COMPLETE — DLL NEVER LOADED'
        ) -and
        $taskText.Contains(
            'This is an ABI and binary-shape milestone, not a live read-only probe.'
        ) -and
        $taskText.Contains('propertyReadSupported=false') -and
        $taskText.Contains('Building this DLL does not grant permission to load it.')
    ) `
    -Detail (
        'Phase 12 documentation must distinguish an offline DLL build from a ' +
        'live read-only connection and preserve the next approval boundary.'
    )

$compiler = $null
$scenarioCount = 0
$scenarioPassedCount = 0
$tapMetadata = $null
$controllerMetadata = $null
$controllerReceipt = $null
$tapDllLoaded = $false

if (-not $StaticOnly) {
    $compiler = Join-Path (
        Join-Path $ToolCache 'portable'
    ) 'Compiler\bin\clang++.exe'
    Add-Check `
        -Name 'toolchain.pinned-portable-compiler-available' `
        -Passed (Test-Path -LiteralPath $compiler -PathType Leaf) `
        -Detail "Required compiler: $compiler"

    if (Test-Path -LiteralPath $compiler -PathType Leaf) {
        [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
        try {
            $harnessExecutable = Join-Path $temporaryRoot 'protocol-harness.exe'
            $controllerExecutable = Join-Path $temporaryRoot 'controller.exe'
            $tapDll = Join-Path $temporaryRoot 'readonly-tap.dll'
            $commonArguments = @(
                '-std=c++20',
                '-O2',
                '-Wall',
                '-Wextra',
                '-Wpedantic',
                '-Werror',
                '-Wconversion',
                '-Wsign-conversion',
                '-Wshadow',
                '-fno-color-diagnostics',
                '-static',
                '-target',
                'x86_64-w64-mingw32',
                '-I',
                $sourceRoot,
                '-I',
                $transportRoot
            )

            $harnessBuildOutput = @(
                & $compiler `
                    @commonArguments `
                    $protocolPath `
                    $harnessPath `
                    -o $harnessExecutable 2>&1
            )
            $harnessBuildExitCode = $LASTEXITCODE
            Add-Check `
                -Name 'build.protocol-harness-warning-free' `
                -Passed (
                    $harnessBuildExitCode -eq 0 -and
                    (Test-Path -LiteralPath $harnessExecutable -PathType Leaf)
                ) `
                -Detail (
                    "Compiler exit $harnessBuildExitCode. " +
                    (($harnessBuildOutput | Select-Object -Last 12) -join ' ')
                )

            if ($harnessBuildExitCode -eq 0) {
                $harnessOutput = @(& $harnessExecutable 2>&1)
                $harnessExitCode = $LASTEXITCODE
                try {
                    $harnessReceipt = (
                        $harnessOutput -join [Environment]::NewLine
                    ) | ConvertFrom-Json
                }
                catch {
                    $harnessReceipt = $null
                }
                if ($null -ne $harnessReceipt) {
                    $scenarioCount = [int]$harnessReceipt.scenarioCount
                    $scenarioPassedCount = [int]$harnessReceipt.passedCount
                }
                Add-Check `
                    -Name 'harness.protocol-fault-matrix' `
                    -Passed (
                        $harnessExitCode -eq 0 -and
                        $null -ne $harnessReceipt -and
                        $harnessReceipt.result -eq 'passed' -and
                        $scenarioCount -eq 38 -and
                        $scenarioPassedCount -eq 38 -and
                        -not $harnessReceipt.tapDllLoaded -and
                        -not $harnessReceipt.liveConnectionCompiled -and
                        -not $harnessReceipt.executionSupported -and
                        -not $harnessReceipt.activationPermitted -and
                        $harnessReceipt.liveExplorer -eq 'not-run' -and
                        -not $harnessReceipt.mutationPerformed
                    ) `
                    -Detail (
                        "Harness exit $harnessExitCode; passed " +
                        "$scenarioPassedCount/$scenarioCount."
                    )
            }

            $controllerBuildOutput = @(
                & $compiler `
                    @commonArguments `
                    -municode `
                    $protocolPath `
                    $admissionPath `
                    $fingerprintPath `
                    $adapterPath `
                    $controllerPath `
                    -o $controllerExecutable 2>&1
            )
            $controllerBuildExitCode = $LASTEXITCODE
            Add-Check `
                -Name 'build.describe-only-controller-warning-free' `
                -Passed (
                    $controllerBuildExitCode -eq 0 -and
                    (Test-Path `
                        -LiteralPath $controllerExecutable `
                        -PathType Leaf)
                ) `
                -Detail (
                    "Compiler exit $controllerBuildExitCode. " +
                    (($controllerBuildOutput | Select-Object -Last 12) -join ' ')
                )

            if ($controllerBuildExitCode -eq 0) {
                $controllerOutput = @(
                    & $controllerExecutable --describe 2>&1
                )
                $controllerExitCode = $LASTEXITCODE
                try {
                    $controllerReceipt = (
                        $controllerOutput -join [Environment]::NewLine
                    ) | ConvertFrom-Json
                }
                catch {
                    $controllerReceipt = $null
                }
                Add-Check `
                    -Name 'controller.describe-only-receipt' `
                    -Passed (
                        $controllerExitCode -eq 0 -and
                        $null -ne $controllerReceipt -and
                        $controllerReceipt.result -eq
                            'passed-build-description' -and
                        $controllerReceipt.existingDiagnosticsConsumerPolicy -eq
                            'reject' -and
                        $controllerReceipt.endpointAttemptLimit -eq 0 -and
                        -not $controllerReceipt.tapDllLoadSupported -and
                        $controllerReceipt.offlineAdmissionModelSupported -and
                        $controllerReceipt.offlineEndpointCandidateLimit -eq
                            1 -and
                        $controllerReceipt.offlineFingerprintModelSupported -and
                        $controllerReceipt.offlineInspectableAdapterModelSupported -and
                        -not $controllerReceipt.propertyReadSupported -and
                        -not $controllerReceipt.liveConnectionCompiled -and
                        -not $controllerReceipt.executionSupported -and
                        -not $controllerReceipt.activationPermitted -and
                        $controllerReceipt.liveExplorer -eq 'not-run' -and
                        -not $controllerReceipt.mutationPerformed
                    ) `
                    -Detail "Controller describe exit $controllerExitCode."
            }

            $tapBuildOutput = @(
                & $compiler `
                    @commonArguments `
                    -shared `
                    -DJARVIS_ENABLE_LIVE_XAML_READONLY=0 `
                    -DJARVIS_ENABLE_LIVE_XAML_PROPERTY_READ=0 `
                    $protocolPath `
                    $admissionPath `
                    $fingerprintPath `
                    $adapterPath `
                    $targetPath `
                    $tapPath `
                    -lole32 `
                    -loleaut32 `
                    -lruntimeobject `
                    -luuid `
                    -luser32 `
                    -o $tapDll 2>&1
            )
            $tapBuildExitCode = $LASTEXITCODE
            Add-Check `
                -Name 'build.readonly-tap-dll-warning-free' `
                -Passed (
                    $tapBuildExitCode -eq 0 -and
                    (Test-Path -LiteralPath $tapDll -PathType Leaf)
                ) `
                -Detail (
                    "Compiler exit $tapBuildExitCode. " +
                    (($tapBuildOutput | Select-Object -Last 12) -join ' ')
                )

            if ($tapBuildExitCode -eq 0) {
                try {
                    $tapMetadata = Get-PeMetadata -Path $tapDll
                    $tapPeError = $null
                }
                catch {
                    $tapMetadata = $null
                    $tapPeError = $_.Exception.Message
                }
                $tapPePassed =
                    $null -ne $tapMetadata -and
                    $tapMetadata.machine -eq '0x8664' -and
                    $tapMetadata.isDll -and
                    @($tapMetadata.exportNames).Count -eq 2 -and
                    @($tapMetadata.exportNames) -contains
                        'DllCanUnloadNow' -and
                    @($tapMetadata.exportNames) -contains
                        'DllGetClassObject'
                $tapPeDetail = if ($null -eq $tapMetadata) {
                    "PE inspection failed: $tapPeError"
                }
                else {
                    "Exports: " +
                    (@($tapMetadata.exportNames) -join ', ') +
                    "; SHA-256 $($tapMetadata.sha256)."
                }
                Add-Check `
                    -Name 'binary.tap-exact-amd64-com-exports' `
                    -Passed $tapPePassed `
                    -Detail $tapPeDetail
            }

            if ($controllerBuildExitCode -eq 0) {
                try {
                    $controllerMetadata =
                        Get-PeMetadata -Path $controllerExecutable
                    $controllerPeError = $null
                }
                catch {
                    $controllerMetadata = $null
                    $controllerPeError = $_.Exception.Message
                }
            }

            $forbiddenBinaryImportPattern = (
                '(?i)!(?:InitializeXamlDiagnosticsEx|LoadLibraryA?W?|' +
                'GetProcAddress|OpenProcess|VirtualAllocEx|' +
                'WriteProcessMemory|CreateRemoteThread|SetWindowsHookExA?W?|' +
                'TerminateProcess|RegSetValueExA?W?|StartServiceA?W?)$'
            )
            $tapImportsPassed =
                $null -ne $tapMetadata -and
                -not (@($tapMetadata.imports) -match
                    $forbiddenBinaryImportPattern) -and
                -not (@($tapMetadata.importDlls) -match
                    '(?i)(?:windows\.ui\.xaml|microsoft\.internal\.frameworkudk)')
            $tapImportsDetail = if ($null -eq $tapMetadata) {
                'TAP PE metadata is unavailable.'
            }
            else {
                "Audited $(@($tapMetadata.imports).Count) imports " +
                "from $(@($tapMetadata.importDlls).Count) DLLs."
            }
            Add-Check `
                -Name 'binary.tap-no-loader-injection-or-xaml-runtime-import' `
                -Passed $tapImportsPassed `
                -Detail $tapImportsDetail

            $controllerImportsPassed =
                $null -ne $controllerMetadata -and
                -not $controllerMetadata.isDll -and
                @($controllerMetadata.exportNames).Count -eq 0 -and
                -not (@($controllerMetadata.imports) -match
                    $forbiddenBinaryImportPattern) -and
                -not (@($controllerMetadata.importDlls) -match
                    '(?i)(?:xaml|frameworkudk)')
            $controllerImportsDetail = if ($null -eq $controllerMetadata) {
                "Controller PE inspection failed: $controllerPeError"
            }
            else {
                "Audited $(@($controllerMetadata.imports).Count) " +
                "imports; SHA-256 $($controllerMetadata.sha256)."
            }
            Add-Check `
                -Name 'binary.controller-no-export-loader-or-xaml-import' `
                -Passed $controllerImportsPassed `
                -Detail $controllerImportsDetail
        }
        finally {
            $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
            $resolvedTemp = [IO.Path]::GetFullPath(
                [IO.Path]::GetTempPath()
            ).TrimEnd('\') + '\'
            if (
                $resolvedTemporaryRoot.StartsWith(
                    $resolvedTemp,
                    [StringComparison]::OrdinalIgnoreCase
                ) -and
                [IO.Path]::GetFileName($resolvedTemporaryRoot).StartsWith(
                    'jarvis2-explorer-readonly-tap-',
                    [StringComparison]::Ordinal
                )
            ) {
                Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
            }
            else {
                throw "Refusing to remove unexpected temp path: $temporaryRoot"
            }
        }
    }
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-explorer-readonly-tap-offline-build-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    scenarioCount = $scenarioCount
    scenarioPassedCount = $scenarioPassedCount
    tapDllBuilt = $null -ne $tapMetadata
    tapDllSha256 = if ($null -eq $tapMetadata) {
        $null
    }
    else {
        $tapMetadata.sha256
    }
    controllerBuilt = $null -ne $controllerMetadata
    controllerSha256 = if ($null -eq $controllerMetadata) {
        $null
    }
    else {
        $controllerMetadata.sha256
    }
    controllerExecutedDescribeOnly = $null -ne $controllerReceipt
    tapDllLoaded = $tapDllLoaded
    liveConnectionCompiled = $false
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
