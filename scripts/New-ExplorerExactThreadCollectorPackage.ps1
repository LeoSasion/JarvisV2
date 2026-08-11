[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$bridgeRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.ExplorerBridgeCore')
$callbackRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.ExplorerCallWndProcBridge')
$bridgeCorePath = Join-Path $bridgeRoot 'jarvis_explorer_bridge_core.cpp'
$callbackCorePath = Join-Path $callbackRoot (
    'jarvis_explorer_callwndproc_bridge.cpp')
$callbackWindowsPath = Join-Path $callbackRoot (
    'jarvis_explorer_callwndproc_bridge_windows.cpp')
$sourcePaths = @(
    $bridgeCorePath,
    (Join-Path $bridgeRoot 'jarvis_explorer_bridge_core.h'),
    (Join-Path $bridgeRoot 'jarvis_explorer_bridge_core_internal.h'),
    $callbackCorePath,
    $callbackWindowsPath,
    (Join-Path $callbackRoot 'jarvis_explorer_callwndproc_bridge.h'),
    (Join-Path $callbackRoot 'jarvis_explorer_callwndproc_bridge_internal.h'),
    $PSCommandPath
)
$zigPath = Join-Path $root (
    'artifacts\toolchains\zig-0.16.0-extract\' +
    'zig-x86_64-windows-0.16.0\zig.exe')
$expectedZigVersion = '0.16.0'

function Import-MsvcEnvironment {
    param(
        [Parameter(Mandatory)]
        [string]$TemporaryDirectory
    )
    if (
        $null -ne (Get-Command cl.exe -ErrorAction SilentlyContinue) -and
        $null -ne (Get-Command dumpbin.exe -ErrorAction SilentlyContinue)
    ) {
        return $true
    }
    $programFilesX86 = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFilesX86)
    $vswherePath = Join-Path $programFilesX86 (
        'Microsoft Visual Studio\Installer\vswhere.exe')
    if (-not (Test-Path -LiteralPath $vswherePath -PathType Leaf)) {
        return $false
    }
    $installationPath = @(
        & $vswherePath `
            -latest `
            -products '*' `
            -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -property installationPath 2>$null
    ) | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($installationPath)) {
        return $false
    }
    $devCommand = Join-Path $installationPath 'Common7\Tools\VsDevCmd.bat'
    $environmentScript = Join-Path $TemporaryDirectory 'msvc-environment.cmd'
    [IO.File]::WriteAllText(
        $environmentScript,
        "@call `"$devCommand`" -no_logo -arch=x64 -host_arch=x64`r`n@set`r`n",
        [Text.Encoding]::ASCII)
    $environmentLines = @(& $env:ComSpec /d /c $environmentScript 2>$null)
    if ($LASTEXITCODE -ne 0) {
        return $false
    }
    foreach ($line in $environmentLines) {
        if ($line -match '^([^=]+)=(.*)$') {
            [Environment]::SetEnvironmentVariable(
                $Matches[1],
                $Matches[2],
                [EnvironmentVariableTarget]::Process)
        }
    }
    return (
        $null -ne (Get-Command cl.exe -ErrorAction SilentlyContinue) -and
        $null -ne (Get-Command dumpbin.exe -ErrorAction SilentlyContinue)
    )
}

function Assert-CompileSucceeded {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [int]$ExitCode,
        [Parameter(Mandatory)]
        [AllowNull()]
        [AllowEmptyCollection()]
        [object[]]$Output,
        [Parameter(Mandatory)]
        [string]$Artifact
    )
    if (
        $ExitCode -ne 0 -or
        -not (Test-Path -LiteralPath $Artifact -PathType Leaf)
    ) {
        throw (
            "$Name failed with exit $ExitCode. " +
            (($Output | Select-Object -Last 40) -join [Environment]::NewLine)
        )
    }
}

function Get-PeMetadata {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 512 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw 'PE image is missing the DOS signature.'
    }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if (
        $peOffset -lt 0 -or $peOffset + 264 -ge $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550
    ) {
        throw 'PE image is missing the NT signature.'
    }
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    $sectionCount = [BitConverter]::ToUInt16($bytes, $peOffset + 6)
    $optionalSize = [BitConverter]::ToUInt16($bytes, $peOffset + 20)
    $optionalOffset = $peOffset + 24
    $magic = [BitConverter]::ToUInt16($bytes, $optionalOffset)
    if ($machine -ne 0x8664 -or $magic -ne 0x020B) {
        throw 'PE image must be x64 PE32+.'
    }
    $sectionTable = $optionalOffset + $optionalSize
    $sections = @()
    for ($index = 0; $index -lt $sectionCount; $index++) {
        $offset = $sectionTable + ($index * 40)
        if ($offset + 40 -gt $bytes.Length) {
            throw 'PE section table exceeds the image.'
        }
        $sections += [pscustomobject]@{
            name = [Text.Encoding]::ASCII.GetString(
                $bytes,
                $offset,
                8).TrimEnd([char]0)
            virtualSize = [BitConverter]::ToUInt32($bytes, $offset + 8)
            virtualAddress = [BitConverter]::ToUInt32($bytes, $offset + 12)
            rawSize = [BitConverter]::ToUInt32($bytes, $offset + 16)
            rawOffset = [BitConverter]::ToUInt32($bytes, $offset + 20)
            characteristics = [BitConverter]::ToUInt32($bytes, $offset + 36)
            headerOffset = $offset
        }
    }
    $rvaToOffset = {
        param([uint32]$Rva)
        foreach ($section in $sections) {
            $extent = [Math]::Max(
                [uint64]$section.virtualSize,
                [uint64]$section.rawSize)
            if (
                $Rva -ge $section.virtualAddress -and
                [uint64]$Rva -lt ([uint64]$section.virtualAddress + $extent)
            ) {
                return [int](
                    [uint64]$section.rawOffset +
                    ([uint64]$Rva - [uint64]$section.virtualAddress))
            }
        }
        throw "PE RVA is not mapped: $Rva"
    }
    $exportRva = [BitConverter]::ToUInt32($bytes, $optionalOffset + 112)
    $exports = @()
    if ($exportRva -ne 0) {
        $exportOffset = & $rvaToOffset $exportRva
        $nameCount = [BitConverter]::ToUInt32($bytes, $exportOffset + 24)
        $nameArrayRva = [BitConverter]::ToUInt32($bytes, $exportOffset + 32)
        $nameArrayOffset = & $rvaToOffset $nameArrayRva
        for ($index = 0; $index -lt $nameCount; $index++) {
            $nameRva = [BitConverter]::ToUInt32(
                $bytes,
                $nameArrayOffset + ($index * 4))
            $nameOffset = & $rvaToOffset $nameRva
            $nameEnd = $nameOffset
            while ($nameEnd -lt $bytes.Length -and $bytes[$nameEnd] -ne 0) {
                $nameEnd++
            }
            if ($nameEnd -ge $bytes.Length) {
                throw 'PE export name exceeds the image.'
            }
            $exports += [Text.Encoding]::ASCII.GetString(
                $bytes,
                $nameOffset,
                $nameEnd - $nameOffset)
        }
    }
    [pscustomobject]@{
        bytes = $bytes
        peOffset = $peOffset
        optionalOffset = $optionalOffset
        entryPoint = [BitConverter]::ToUInt32($bytes, $optionalOffset + 16)
        sections = $sections
        exports = @($exports | Sort-Object)
    }
}

function Set-PeNoEntrySharedBridgeSection {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )
    $metadata = Get-PeMetadata -Path $Path
    $bridgeSection = @(
        $metadata.sections | Where-Object name -eq '.jvbrdg'
    )
    if ($bridgeSection.Count -ne 1) {
        throw 'Callback DLL must contain one exact .jvbrdg section.'
    }
    [Array]::Copy(
        [BitConverter]::GetBytes([uint32]0),
        0,
        $metadata.bytes,
        $metadata.optionalOffset + 16,
        4)
    $updatedCharacteristics = [uint32](
        ([uint32]$bridgeSection[0].characteristics -bor [uint32]3489660992) -band
        [uint32]3758096383)
    [Array]::Copy(
        [BitConverter]::GetBytes($updatedCharacteristics),
        0,
        $metadata.bytes,
        $bridgeSection[0].headerOffset + 36,
        4)
    [IO.File]::WriteAllBytes($Path, $metadata.bytes)

    $verified = Get-PeMetadata -Path $Path
    $verifiedBridge = @(
        $verified.sections | Where-Object name -eq '.jvbrdg'
    )
    $flags = [uint32]$verifiedBridge[0].characteristics
    if (
        $verified.entryPoint -ne 0 -or
        ($flags -band [uint32]268435456) -eq 0 -or
        ($flags -band [uint32]1073741824) -eq 0 -or
        ($flags -band [uint32]2147483648) -eq 0 -or
        ($flags -band [uint32]536870912) -ne 0
    ) {
        throw 'Callback DLL PE hardening verification failed.'
    }
    $verified
}

function Invoke-Zig {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )
    @(& $zigPath @Arguments 2>&1)
}

$missing = @($sourcePaths | Where-Object {
    -not (Test-Path -LiteralPath $_ -PathType Leaf)
})
if ($missing.Count -ne 0) {
    throw "Offline callback source set is incomplete: $($missing -join ', ')"
}

$sourceIdentity = [ordered]@{}
foreach ($sourcePath in $sourcePaths) {
    $relative = [IO.Path]::GetRelativePath($root, $sourcePath).Replace('\', '/')
    $item = Get-Item -LiteralPath $sourcePath
    $sourceIdentity[$relative] = [ordered]@{
        bytes = [long]$item.Length
        sha256 = (
            Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256
        ).Hash.ToLowerInvariant()
    }
}
$sourceSetMaterial = @(
    foreach ($entry in $sourceIdentity.GetEnumerator() | Sort-Object Name) {
        "$($entry.Key)=$($entry.Value.sha256)"
    }
) -join "`n"
$sourceSetSha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($sourceSetMaterial)
    )).ToLowerInvariant()

$resolvedOutput = [IO.Path]::GetFullPath(
    $(if ([IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory
    } else {
        Join-Path $root $OutputDirectory
    }))
$allowedArtifactRoot = [IO.Path]::GetFullPath(
    (Join-Path $root 'artifacts')).TrimEnd('\') + '\'
if (
    -not $resolvedOutput.StartsWith(
        $allowedArtifactRoot,
        [StringComparison]::OrdinalIgnoreCase) -or
    -not [IO.Path]::GetFileName($resolvedOutput).StartsWith(
        'win10-explorer-exact-thread-collector-',
        [StringComparison]::Ordinal)
) {
    throw 'OutputDirectory must be a named win10-explorer-exact-thread-collector-* directory under artifacts.'
}
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "OutputDirectory already exists: $resolvedOutput"
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'jarvis2-win10-exact-thread-collector-build-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
try {
    $modulePath = Join-Path $temporaryRoot (
        'jarvis-win10-explorer-callwndproc-bridge.dll')
    $usingMsvc = Import-MsvcEnvironment -TemporaryDirectory $temporaryRoot
    if ($usingMsvc) {
        $compiler = (Get-Command cl.exe -ErrorAction Stop).Source
        $moduleOutput = @(
            & $compiler `
                /nologo /std:c++20 /O2 /W4 /WX /permissive- `
                /Zc:preprocessor /GS- /GR- /Zl /LD `
                /DJARVIS_BRIDGE_CORE_SHARED_INSTANCE `
                "/I$callbackRoot" "/I$bridgeRoot" `
                $bridgeCorePath $callbackCorePath $callbackWindowsPath `
                user32.lib kernel32.lib "/Fe$modulePath" `
                /link /NOENTRY /NODEFAULTLIB 2>&1
        )
        Assert-CompileSucceeded `
            -Name 'Callback DLL build' `
            -ExitCode $LASTEXITCODE `
            -Output $moduleOutput `
            -Artifact $modulePath
        $toolchain = [ordered]@{
            kind = 'msvc'
            compilerPath = $compiler
            compilerSha256 = (
                Get-FileHash -LiteralPath $compiler -Algorithm SHA256
            ).Hash.ToLowerInvariant()
        }
    } else {
        if (-not (Test-Path -LiteralPath $zigPath -PathType Leaf)) {
            throw 'Neither MSVC nor the pinned Zig 0.16.0 toolchain is available.'
        }
        $zigVersion = @(& $zigPath version 2>&1) | Select-Object -First 1
        if ($LASTEXITCODE -ne 0 -or $zigVersion -ne $expectedZigVersion) {
            throw "Pinned Zig identity mismatch: $zigVersion"
        }
        $zigCacheRoot = Join-Path $root 'artifacts\toolchains\zig-cache'
        $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $zigCacheRoot 'global'
        $env:ZIG_LOCAL_CACHE_DIR = Join-Path $zigCacheRoot 'local'
        [IO.Directory]::CreateDirectory($env:ZIG_GLOBAL_CACHE_DIR) | Out-Null
        [IO.Directory]::CreateDirectory($env:ZIG_LOCAL_CACHE_DIR) | Out-Null

        $bridgeObject = Join-Path $temporaryRoot 'bridge-core.obj'
        $callbackCoreObject = Join-Path $temporaryRoot 'callback-core.obj'
        $callbackWindowsObject = Join-Path $temporaryRoot 'callback-windows.obj'
        $commonModuleArguments = @(
            'c++', '-target', 'x86_64-windows-gnu', '-std=c++20', '-O2',
            '-Wall', '-Wextra', '-Werror', '-Wno-nullability-completeness',
            '-Wno-unknown-pragmas', '-fno-exceptions', '-fno-rtti',
            '-fno-stack-protector', '-DJARVIS_BRIDGE_CORE_SHARED_INSTANCE',
            "-I$bridgeRoot", "-I$callbackRoot"
        )
        foreach ($compile in @(
            @($bridgeCorePath, $bridgeObject, @()),
            @($callbackCorePath, $callbackCoreObject, @()),
            @(
                $callbackWindowsPath,
                $callbackWindowsObject,
                @('-DJARVIS_ZIG_ZERO_ENTRY_LINK_STUB'))
        )) {
            $arguments = @($commonModuleArguments) + @($compile[2]) + @(
                '-c', $compile[0], '-o', $compile[1])
            $compileOutput = Invoke-Zig -Arguments $arguments
            Assert-CompileSucceeded `
                -Name "Callback object build: $($compile[0])" `
                -ExitCode $LASTEXITCODE `
                -Output $compileOutput `
                -Artifact $compile[1]
        }
        $moduleOutput = Invoke-Zig -Arguments @(
            'build-lib', '-target', 'x86_64-windows-gnu', '-dynamic',
            '-fno-emit-implib', '-fno-compiler-rt', '-fno-ubsan-rt',
            $bridgeObject, $callbackCoreObject, $callbackWindowsObject,
            '-lkernel32', '-luser32', "-femit-bin=$modulePath")
        Assert-CompileSucceeded `
            -Name 'Callback DLL link' `
            -ExitCode $LASTEXITCODE `
            -Output $moduleOutput `
            -Artifact $modulePath

        $toolchain = [ordered]@{
            kind = 'zig'
            version = $zigVersion
            compilerPath = [IO.Path]::GetRelativePath($root, $zigPath).Replace('\', '/')
            compilerSha256 = (
                Get-FileHash -LiteralPath $zigPath -Algorithm SHA256
            ).Hash.ToLowerInvariant()
        }
    }

    $peMetadata = Set-PeNoEntrySharedBridgeSection -Path $modulePath
    $expectedExports = @(
        'JarvisBridge_AcquireSharedInstance',
        'JarvisBridge_CallWndProc',
        'JarvisBridge_Initialize',
        'JarvisBridge_QueryContract',
        'JarvisBridge_QueryState',
        'JarvisBridge_Quiesce'
    ) | Sort-Object
    $actualExports = @($peMetadata.exports)
    if (@(Compare-Object $expectedExports $actualExports).Count -ne 0) {
        throw "Callback DLL export set drifted: $($actualExports -join ', ')"
    }
    $bridgeSection = @(
        $peMetadata.sections | Where-Object name -eq '.jvbrdg'
    )[0]

    foreach ($sourcePath in $sourcePaths) {
        $relative = [IO.Path]::GetRelativePath(
            $root,
            $sourcePath).Replace('\', '/')
        $expected = $sourceIdentity[$relative]
        $item = Get-Item -LiteralPath $sourcePath
        $actualSha256 = (
            Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256
        ).Hash.ToLowerInvariant()
        if (
            [long]$item.Length -ne [long]$expected.bytes -or
            $actualSha256 -cne [string]$expected.sha256
        ) {
            throw "Offline callback source changed during build: $relative"
        }
    }

    [IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
    $packagedModule = Join-Path $resolvedOutput (
        'jarvis-win10-explorer-callwndproc-bridge.dll')
    Copy-Item -LiteralPath $modulePath -Destination $packagedModule

    $receipt = [ordered]@{
        schemaVersion = 1
        receiptType = 'jarvisv2-win10-explorer-exact-thread-collector-package'
        result = 'passed'
        builtAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        moduleId = 'jarvis-win10-explorer-callwndproc-bridge'
        offlineOnly = $true
        collectorExecutablePublished = $false
        packageFileCount = 2
        packageFileSet = @(
            'jarvis-win10-explorer-callwndproc-bridge.dll',
            'package-receipt.json'
        )
        architecture = 'x64'
        transport = 'not-published-offline-callback-envelope'
        callbackBody = 'empty-pass-through'
        callbackDllExecuted = $false
        toolchain = $toolchain
        callbackDll = [ordered]@{
            relativePath = 'jarvis-win10-explorer-callwndproc-bridge.dll'
            bytes = [long](Get-Item -LiteralPath $packagedModule).Length
            sha256 = (
                Get-FileHash -LiteralPath $packagedModule -Algorithm SHA256
            ).Hash.ToLowerInvariant()
            exports = $actualExports
            zeroEntryPoint = ($peMetadata.entryPoint -eq 0)
            bridgeSection = [ordered]@{
                name = $bridgeSection.name
                characteristics = ('0x{0:X8}' -f $bridgeSection.characteristics)
                shared = (
                    ($bridgeSection.characteristics -band [uint32]268435456) -ne 0)
                readable = (
                    ($bridgeSection.characteristics -band [uint32]1073741824) -ne 0)
                writable = (
                    ($bridgeSection.characteristics -band [uint32]2147483648) -ne 0)
                executable = (
                    ($bridgeSection.characteristics -band [uint32]536870912) -ne 0)
            }
        }
        sourceSetSha256 = $sourceSetSha256
        sourceIdentity = $sourceIdentity
        activationPermitted = $false
        liveExplorer = 'not-run'
        mutationPerformed = $false
    }
    $receiptPath = Join-Path $resolvedOutput 'package-receipt.json'
    [IO.File]::WriteAllText(
        $receiptPath,
        ($receipt | ConvertTo-Json -Depth 10) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
    $publishedFiles = @(Get-ChildItem -LiteralPath $resolvedOutput -File)
    if (
        $publishedFiles.Count -ne 2 -or
        @($publishedFiles | Where-Object Extension -ieq '.exe').Count -ne 0
    ) {
        throw 'Offline callback package must contain exactly DLL + receipt and no EXE.'
    }
    $receipt | ConvertTo-Json -Depth 10
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $resolvedTemp = [IO.Path]::GetFullPath(
            [IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (
            $resolvedTemporaryRoot.StartsWith(
                $resolvedTemp,
                [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($resolvedTemporaryRoot).StartsWith(
                'jarvis2-win10-exact-thread-collector-build-',
                [StringComparison]::Ordinal)
        ) {
            Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
        }
    }
}
