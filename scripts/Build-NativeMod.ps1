[CmdletBinding()]
param(
    [ValidateSet('all', 'jarvis-native-taskbar', 'jarvis-taskbar-icon-size')]
    [string[]]$Module = @('all'),
    [string]$ToolCache = (Join-Path $env:LOCALAPPDATA 'JARVIS2\tool-cache\windhawk-1.7.3'),
    [string]$OutputDirectory,
    [switch]$ValidateInputsOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'JARVIS2 native builds require PowerShell 7 or newer for safe process argument handling.'
}

$root = Split-Path -Parent $PSScriptRoot
$defaultOutputRoot = Join-Path $root 'artifacts\native'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = $defaultOutputRoot
}

$moduleSpecs = [ordered]@{
    'jarvis-native-taskbar' = [pscustomobject]@{
        id = 'jarvis-native-taskbar'
        source = Join-Path $root 'mods\jarvis-native-taskbar.wh.cpp'
        architecture = 'amd64'
        supportingSources = @(
            [pscustomobject]@{
                path = Join-Path $root 'mods\jarvis-resource-protocol.hpp'
                includeFileName = 'jarvis-resource-protocol.hpp'
            }
        )
    }
    'jarvis-taskbar-icon-size' = [pscustomobject]@{
        id = 'jarvis-taskbar-icon-size'
        source = Join-Path $root 'mods\jarvis-taskbar-icon-size.wh.cpp'
        architecture = 'amd64'
        supportingSources = @()
    }
}

if ($Module -contains 'all') {
    if ($Module.Count -ne 1) {
        throw "'all' can't be combined with individual module ids."
    }
    $selectedSpecs = @($moduleSpecs.Values)
}
else {
    if ($Module.Count -eq 0) {
        throw 'At least one module must be selected.'
    }
    if (@($Module | Sort-Object -Unique).Count -ne $Module.Count) {
        throw 'Duplicate module ids are not allowed in one build run.'
    }
    $selectedSpecs = @($Module | ForEach-Object { $moduleSpecs[$_] })
}

$toolchainLockPath = Join-Path $root 'config\toolchain-lock.json'
$buildScriptPath = $PSCommandPath
$testScriptPath = Join-Path $root 'scripts\Test-Project.ps1'
$committedReceiptPath = Join-Path $root 'docs\receipts\native-build-2026-07-22.json'
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$canonicalOutputRoot = [System.IO.Path]::GetFullPath($defaultOutputRoot)
$stagingRoot = Join-Path $outputRoot '.staging'
$runsRoot = Join-Path $outputRoot 'runs'
$runId = '{0}-{1}' -f [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$runStartedAtUtc = [DateTimeOffset]::UtcNow

$environmentVariablesClearedForBuild = @(
    'CCC_OVERRIDE_OPTIONS',
    'CFLAGS',
    'CPPFLAGS',
    'C_INCLUDE_PATH',
    'CXXFLAGS',
    'CPLUS_INCLUDE_PATH',
    'CPATH',
    'GCC_EXEC_PREFIX',
    'INCLUDE',
    'LIB',
    'LDFLAGS',
    'LIBRARY_PATH',
    'OBJC_INCLUDE_PATH',
    'PYTHONHOME',
    'PYTHONINSPECT',
    'PYTHONPATH',
    'PYTHONSTARTUP',
    'SDKROOT'
)

function Get-NormalizedRelativePath {
    param(
        [Parameter(Mandatory)] [string]$BasePath,
        [Parameter(Mandatory)] [string]$Path
    )

    return [System.IO.Path]::GetRelativePath(
        [System.IO.Path]::GetFullPath($BasePath),
        [System.IO.Path]::GetFullPath($Path)
    ).Replace('\', '/')
}

function Test-PathEqual {
    param(
        [Parameter(Mandatory)] [string]$Left,
        [Parameter(Mandatory)] [string]$Right
    )

    return [string]::Equals(
        [System.IO.Path]::GetFullPath($Left).TrimEnd('\'),
        [System.IO.Path]::GetFullPath($Right).TrimEnd('\'),
        [StringComparison]::OrdinalIgnoreCase
    )
}

function Assert-PathWithin {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Parent
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($fullParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing filesystem operation outside $fullParent`: $fullPath"
    }
    return $fullPath
}

function Assert-NoReparsePointsInPath {
    param([Parameter(Mandatory)] [string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrEmpty($pathRoot)) {
        throw "Path has no filesystem root: $fullPath"
    }

    $current = $pathRoot
    $relative = $fullPath.Substring($pathRoot.Length)
    foreach ($segment in $relative.Split(@('\', '/'), [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current)) {
            continue
        }

        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Reparse points aren't allowed in build paths: $($item.FullName)"
        }
    }

    return $fullPath
}

function Assert-NonSystemBuildRoot {
    param([Parameter(Mandatory)] [string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $forbiddenRoots = @(
        $env:SystemRoot,
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)}
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($forbidden in $forbiddenRoots) {
        $fullForbidden = [System.IO.Path]::GetFullPath($forbidden).TrimEnd('\')
        if ($fullPath.Equals($fullForbidden, [StringComparison]::OrdinalIgnoreCase) -or
            $fullPath.StartsWith($fullForbidden + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Build roots must stay outside Windows and Program Files: $fullPath"
        }
    }

    return $fullPath
}

function Remove-SafeTree {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedParent
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $verifiedPath = Assert-PathWithin -Path $Path -Parent $AllowedParent
    $null = Assert-NoReparsePointsInPath -Path $verifiedPath
    foreach ($item in Get-ChildItem -LiteralPath $verifiedPath -Recurse -Force -ErrorAction Stop) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing recursive cleanup because a reparse point exists: $($item.FullName)"
        }
    }

    Remove-Item -LiteralPath $verifiedPath -Recurse -Force -ErrorAction Stop
}

function Assert-FileHash {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Expected
    )

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not $actual.Equals($Expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 mismatch for $Path. Expected $Expected, got $actual."
    }
    return $actual.ToUpperInvariant()
}

function Write-AtomicUtf8Text {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Text
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $parent = Split-Path -Parent $fullPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $null = Assert-NoReparsePointsInPath -Path $parent

    $tempPath = Join-Path $parent ('.{0}.{1}.tmp' -f [System.IO.Path]::GetFileName($fullPath), [Guid]::NewGuid().ToString('N'))
    $backupPath = Join-Path $parent ('.{0}.{1}.bak' -f [System.IO.Path]::GetFileName($fullPath), [Guid]::NewGuid().ToString('N'))
    $encoding = [System.Text.UTF8Encoding]::new($false)
    try {
        [System.IO.File]::WriteAllText($tempPath, $Text, $encoding)
        $stream = [System.IO.File]::Open($tempPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        try {
            $stream.Flush($true)
        }
        finally {
            $stream.Dispose()
        }

        if (Test-Path -LiteralPath $fullPath) {
            [System.IO.File]::Replace($tempPath, $fullPath, $backupPath, $true)
            [System.IO.File]::Delete($backupPath)
        }
        else {
            [System.IO.File]::Move($tempPath, $fullPath)
        }
    }
    finally {
        if (Test-Path -LiteralPath $tempPath) {
            Remove-Item -LiteralPath $tempPath -Force
        }
        if (Test-Path -LiteralPath $backupPath) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }
}

function Write-AtomicUtf8Json {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [object]$Value,
        [int]$Depth = 12
    )

    $json = ($Value | ConvertTo-Json -Depth $Depth) + [Environment]::NewLine
    Write-AtomicUtf8Text -Path $Path -Text $json
}

function Get-VerifiedDownload {
    param(
        [Parameter(Mandatory)] [string]$Uri,
        [Parameter(Mandatory)] [string]$Destination,
        [Parameter(Mandatory)] [string]$Sha256
    )

    if (Test-Path -LiteralPath $Destination) {
        $null = Assert-FileHash -Path $Destination -Expected $Sha256
        return
    }

    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $null = Assert-NoReparsePointsInPath -Path $parent
    $tempPath = Join-Path $parent ('.download-{0}.tmp' -f [Guid]::NewGuid().ToString('N'))
    try {
        Invoke-WebRequest -Uri $Uri -OutFile $tempPath
        $null = Assert-FileHash -Path $tempPath -Expected $Sha256
        [System.IO.File]::Move($tempPath, $Destination)
    }
    finally {
        if (Test-Path -LiteralPath $tempPath) {
            Remove-Item -LiteralPath $tempPath -Force
        }
    }
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [switch]$SanitizeBuildEnvironment
    )

    $resolvedFilePath = [System.IO.Path]::GetFullPath($FilePath)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $resolvedFilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    if ($SanitizeBuildEnvironment) {
        foreach ($name in $environmentVariablesClearedForBuild) {
            $null = $startInfo.Environment.Remove($name)
        }
        $safePath = @(
            (Split-Path -Parent $resolvedFilePath),
            (Split-Path -Parent $portableCompiler),
            (Join-Path $env:SystemRoot 'System32'),
            $env:SystemRoot
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
        $startInfo.Environment['PATH'] = $safePath -join [System.IO.Path]::PathSeparator
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Failed to start process: $resolvedFilePath"
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($stdoutTask, $stderrTask))

        return [pscustomobject]@{
            filePath = $resolvedFilePath
            exitCode = $process.ExitCode
            stdout = $stdoutTask.Result
            stderr = $stderrTask.Result
        }
    }
    finally {
        $process.Dispose()
    }
}

function Get-CompileInputAggregate {
    param(
        [Parameter(Mandatory)] [string]$PortablePath,
        [Parameter(Mandatory)] [object[]]$Scopes
    )

    $portableFullPath = [System.IO.Path]::GetFullPath($PortablePath)
    $null = Assert-NoReparsePointsInPath -Path $portableFullPath
    $filesByRelativePath = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)

    foreach ($scope in $Scopes) {
        $scopePath = Join-Path $portableFullPath ([string]$scope.relativePath)
        $scopePath = Assert-PathWithin -Path $scopePath -Parent $portableFullPath
        if (-not (Test-Path -LiteralPath $scopePath)) {
            throw "Locked compiler input scope is missing: $($scope.relativePath)"
        }
        $null = Assert-NoReparsePointsInPath -Path $scopePath

        $scopeFiles = switch ([string]$scope.kind) {
            'tree' {
                $scopeItems = @(Get-ChildItem -LiteralPath $scopePath -Recurse -Force -ErrorAction Stop)
                foreach ($scopeItem in $scopeItems) {
                    if (($scopeItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        throw "Reparse points aren't allowed in compiler inputs: $($scopeItem.FullName)"
                    }
                }
                @($scopeItems | Where-Object { -not $_.PSIsContainer })
            }
            'file' { @(Get-Item -LiteralPath $scopePath -Force -ErrorAction Stop) }
            default { throw "Unknown compiler input scope kind: $($scope.kind)" }
        }

        foreach ($file in $scopeFiles) {
            if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points aren't allowed in compiler inputs: $($file.FullName)"
            }
            $relativePath = Get-NormalizedRelativePath -BasePath $portableFullPath -Path $file.FullName
            if (-not $filesByRelativePath.TryAdd($relativePath, $file.FullName)) {
                throw "Compiler input scopes overlap at: $relativePath"
            }
        }
    }

    $relativePaths = [string[]]$filesByRelativePath.Keys
    [Array]::Sort($relativePaths, [StringComparer]::Ordinal)
    $incrementalHash = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [uint64]$totalBytes = 0
    try {
        foreach ($relativePath in $relativePaths) {
            $filePath = $filesByRelativePath[$relativePath]
            $item = Get-Item -LiteralPath $filePath -Force
            $fileHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToUpperInvariant()
            $totalBytes += [uint64]$item.Length
            $record = "$relativePath`0$($item.Length)`0$fileHash`n"
            $incrementalHash.AppendData($utf8.GetBytes($record))
        }

        return [pscustomobject]@{
            algorithm = 'sha256-path-size-content-v1'
            fileCount = $relativePaths.Count
            bytes = $totalBytes
            sha256 = [Convert]::ToHexString($incrementalHash.GetHashAndReset())
        }
    }
    finally {
        $incrementalHash.Dispose()
    }
}

function Assert-CompileInputAggregate {
    param(
        [Parameter(Mandatory)] [object]$Actual,
        [Parameter(Mandatory)] [object]$Expected
    )

    if ($Actual.algorithm -ne $Expected.algorithm -or
        [uint64]$Actual.fileCount -ne [uint64]$Expected.fileCount -or
        [uint64]$Actual.bytes -ne [uint64]$Expected.bytes -or
        -not ([string]$Actual.sha256).Equals([string]$Expected.sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Portable compiler input aggregate mismatch. Expected $($Expected | ConvertTo-Json -Compress), got $($Actual | ConvertTo-Json -Compress)."
    }
}

function Get-UInt16 {
    param([byte[]]$Bytes, [int]$Offset)
    if ($Offset -lt 0 -or [uint64]$Offset + 2 -gt [uint64]$Bytes.LongLength) {
        throw "PE read exceeds the file at offset $Offset (2 bytes)."
    }
    return [BitConverter]::ToUInt16($Bytes, $Offset)
}

function Get-UInt32 {
    param([byte[]]$Bytes, [int]$Offset)
    if ($Offset -lt 0 -or [uint64]$Offset + 4 -gt [uint64]$Bytes.LongLength) {
        throw "PE read exceeds the file at offset $Offset (4 bytes)."
    }
    return [BitConverter]::ToUInt32($Bytes, $Offset)
}

function Resolve-PeRva {
    param(
        [uint32]$Rva,
        [uint32]$RequiredBytes,
        [object[]]$Sections,
        [uint32]$SizeOfHeaders,
        [uint64]$FileLength
    )

    $rvaEnd = [uint64]$Rva + [uint64]$RequiredBytes
    if ([uint64]$Rva -lt [uint64]$SizeOfHeaders -and
        $rvaEnd -le [uint64]$SizeOfHeaders -and
        $rvaEnd -le $FileLength) {
        return [uint64]$Rva
    }

    foreach ($section in $Sections) {
        if ([uint64]$Rva -lt [uint64]$section.virtualAddress) {
            continue
        }
        $delta = [uint64]$Rva - [uint64]$section.virtualAddress
        if ($delta -ge [uint64]$section.sizeOfRawData -or
            $delta + [uint64]$RequiredBytes -gt [uint64]$section.sizeOfRawData) {
            continue
        }
        $offset = [uint64]$section.pointerToRawData + $delta
        if ($offset + [uint64]$RequiredBytes -gt $FileLength) {
            throw ('RVA 0x{0:X8} maps beyond the PE file.' -f $Rva)
        }
        return $offset
    }

    throw ('RVA 0x{0:X8} is not backed by PE file data.' -f $Rva)
}

function Assert-PeVirtualRva {
    param(
        [uint32]$Rva,
        [uint32]$RequiredBytes,
        [object[]]$Sections,
        [uint32]$SizeOfHeaders,
        [uint32]$SizeOfImage
    )

    $rvaEnd = [uint64]$Rva + [uint64]$RequiredBytes
    if ($rvaEnd -gt [uint64]$SizeOfImage) {
        throw ('RVA 0x{0:X8} exceeds SizeOfImage.' -f $Rva)
    }
    if ([uint64]$Rva -lt [uint64]$SizeOfHeaders -and $rvaEnd -le [uint64]$SizeOfHeaders) {
        return
    }
    foreach ($section in $Sections) {
        $mappedSize = [Math]::Max([uint64]$section.virtualSize, [uint64]$section.sizeOfRawData)
        if ([uint64]$Rva -ge [uint64]$section.virtualAddress -and
            $rvaEnd -le [uint64]$section.virtualAddress + $mappedSize) {
            return
        }
    }
    throw ('RVA 0x{0:X8} is outside all mapped PE sections.' -f $Rva)
}

function Get-StrictPeInfo {
    param([Parameter(Mandatory)] [string]$Path)

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        if ($stream.Length -gt [int]::MaxValue) {
            throw "PE image is unexpectedly large: $Path"
        }
        $bytes = [byte[]]::new([int]$stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -eq 0) {
                throw "Unexpected EOF while reading: $Path"
            }
            $offset += $read
        }
    }
    finally {
        $stream.Dispose()
    }

    if (-not [BitConverter]::IsLittleEndian -or $bytes.Length -lt 64 -or (Get-UInt16 $bytes 0) -ne 0x5A4D) {
        throw "$Path is not a valid little-endian DOS/PE image."
    }

    $peOffset = Get-UInt32 $bytes 0x3C
    if ([uint64]$peOffset + 24 -gt [uint64]$bytes.LongLength -or (Get-UInt32 $bytes ([int]$peOffset)) -ne 0x00004550) {
        throw "$Path has an invalid PE signature offset."
    }

    $fileHeader = [int]$peOffset + 4
    $machine = Get-UInt16 $bytes $fileHeader
    $numberOfSections = Get-UInt16 $bytes ($fileHeader + 2)
    $timeDateStamp = Get-UInt32 $bytes ($fileHeader + 4)
    $sizeOfOptionalHeader = Get-UInt16 $bytes ($fileHeader + 16)
    $characteristics = Get-UInt16 $bytes ($fileHeader + 18)
    $optionalHeader = $fileHeader + 20

    if ($machine -ne 0x8664 -or $numberOfSections -lt 1 -or $numberOfSections -gt 96 -or $sizeOfOptionalHeader -ne 0xF0) {
        throw "$Path isn't an expected AMD64 PE32+ image."
    }
    if ([uint64]$optionalHeader + [uint64]$sizeOfOptionalHeader + ([uint64]$numberOfSections * 40) -gt [uint64]$bytes.LongLength) {
        throw "$Path has truncated PE headers."
    }
    if ((Get-UInt16 $bytes $optionalHeader) -ne 0x20B -or
        ($characteristics -band 0x0002) -eq 0 -or
        ($characteristics -band 0x2000) -eq 0) {
        throw "$Path isn't an executable PE32+ DLL."
    }

    $sizeOfImage = Get-UInt32 $bytes ($optionalHeader + 56)
    $sizeOfHeaders = Get-UInt32 $bytes ($optionalHeader + 60)
    $numberOfRvaAndSizes = Get-UInt32 $bytes ($optionalHeader + 108)
    if ($sizeOfImage -eq 0 -or $sizeOfHeaders -eq 0 -or $sizeOfHeaders -gt $bytes.Length -or $numberOfRvaAndSizes -lt 1) {
        throw "$Path has invalid PE32+ optional-header bounds."
    }

    $sections = [System.Collections.Generic.List[object]]::new()
    $sectionOffset = $optionalHeader + $sizeOfOptionalHeader
    for ($index = 0; $index -lt $numberOfSections; $index++) {
        $headerOffset = $sectionOffset + ($index * 40)
        $virtualSize = Get-UInt32 $bytes ($headerOffset + 8)
        $virtualAddress = Get-UInt32 $bytes ($headerOffset + 12)
        $sizeOfRawData = Get-UInt32 $bytes ($headerOffset + 16)
        $pointerToRawData = Get-UInt32 $bytes ($headerOffset + 20)
        if ($sizeOfRawData -ne 0 -and [uint64]$pointerToRawData + [uint64]$sizeOfRawData -gt [uint64]$bytes.LongLength) {
            throw "$Path has a section whose raw data exceeds the file."
        }
        $sections.Add([pscustomobject]@{
            virtualSize = $virtualSize
            virtualAddress = $virtualAddress
            sizeOfRawData = $sizeOfRawData
            pointerToRawData = $pointerToRawData
        })
    }

    $exportRva = Get-UInt32 $bytes ($optionalHeader + 112)
    $exportSize = Get-UInt32 $bytes ($optionalHeader + 116)
    if ($exportRva -eq 0 -or $exportSize -lt 40) {
        throw "$Path has no valid export directory."
    }
    $exportOffset = Resolve-PeRva -Rva $exportRva -RequiredBytes 40 -Sections $sections.ToArray() -SizeOfHeaders $sizeOfHeaders -FileLength $bytes.LongLength
    $exportEnd = [uint64]$exportRva + [uint64]$exportSize

    $numberOfFunctions = Get-UInt32 $bytes ([int]$exportOffset + 20)
    $numberOfNames = Get-UInt32 $bytes ([int]$exportOffset + 24)
    $addressOfFunctions = Get-UInt32 $bytes ([int]$exportOffset + 28)
    $addressOfNames = Get-UInt32 $bytes ([int]$exportOffset + 32)
    $addressOfNameOrdinals = Get-UInt32 $bytes ([int]$exportOffset + 36)
    if ($numberOfFunctions -lt 1 -or $numberOfFunctions -gt 200000 -or
        $numberOfNames -lt 1 -or $numberOfNames -gt $numberOfFunctions -or $numberOfNames -gt 200000) {
        throw "$Path has unreasonable export table counts."
    }

    $functionsOffset = Resolve-PeRva -Rva $addressOfFunctions -RequiredBytes ([uint32]($numberOfFunctions * 4)) -Sections $sections.ToArray() -SizeOfHeaders $sizeOfHeaders -FileLength $bytes.LongLength
    $namesOffset = Resolve-PeRva -Rva $addressOfNames -RequiredBytes ([uint32]($numberOfNames * 4)) -Sections $sections.ToArray() -SizeOfHeaders $sizeOfHeaders -FileLength $bytes.LongLength
    $ordinalsOffset = Resolve-PeRva -Rva $addressOfNameOrdinals -RequiredBytes ([uint32]($numberOfNames * 2)) -Sections $sections.ToArray() -SizeOfHeaders $sizeOfHeaders -FileLength $bytes.LongLength

    $exports = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    for ($index = 0; $index -lt $numberOfNames; $index++) {
        $nameRva = Get-UInt32 $bytes ([int]$namesOffset + ($index * 4))
        $nameOffset = Resolve-PeRva -Rva $nameRva -RequiredBytes 1 -Sections $sections.ToArray() -SizeOfHeaders $sizeOfHeaders -FileLength $bytes.LongLength
        $nameBytes = [System.Collections.Generic.List[byte]]::new()
        for ($length = 0; $length -lt 4096; $length++) {
            $currentOffset = $nameOffset + [uint64]$length
            if ($currentOffset -ge [uint64]$bytes.LongLength) {
                throw "$Path has an unterminated export name."
            }
            $value = $bytes[[int]$currentOffset]
            if ($value -eq 0) {
                break
            }
            if ($value -gt 0x7F) {
                throw "$Path has a non-ASCII export name."
            }
            $nameBytes.Add($value)
        }
        if ($nameBytes.Count -eq 4096) {
            throw "$Path has an overlong export name."
        }

        $name = [System.Text.Encoding]::ASCII.GetString($nameBytes.ToArray())
        $ordinalIndex = Get-UInt16 $bytes ([int]$ordinalsOffset + ($index * 2))
        if ($ordinalIndex -ge $numberOfFunctions) {
            throw "$Path has an export ordinal outside the function table."
        }
        $functionRva = Get-UInt32 $bytes ([int]$functionsOffset + ($ordinalIndex * 4))
        if ($functionRva -eq 0) {
            throw "$Path has a named export with a null function RVA: $name"
        }
        $isForwarder = [uint64]$functionRva -ge [uint64]$exportRva -and [uint64]$functionRva -lt $exportEnd
        if (-not $isForwarder) {
            Assert-PeVirtualRva -Rva $functionRva -RequiredBytes 1 -Sections $sections.ToArray() -SizeOfHeaders $sizeOfHeaders -SizeOfImage $sizeOfImage
        }
        if (-not $exports.TryAdd($name, [pscustomobject]@{ functionRva = $functionRva; isForwarder = $isForwarder })) {
            throw "$Path has duplicate export names: $name"
        }
    }

    foreach ($requiredExport in @('InternalWhModPtr', '_Z10Wh_ModInitv')) {
        if (-not $exports.ContainsKey($requiredExport) -or $exports[$requiredExport].isForwarder) {
            throw "$Path is missing a concrete required Windhawk export: $requiredExport"
        }
    }
    $null = Resolve-PeRva -Rva $exports['_Z10Wh_ModInitv'].functionRva -RequiredBytes 1 -Sections $sections.ToArray() -SizeOfHeaders $sizeOfHeaders -FileLength $bytes.LongLength

    $exportNames = [string[]]$exports.Keys
    [Array]::Sort($exportNames, [StringComparer]::Ordinal)
    $windhawkExports = @($exportNames | Where-Object {
        $_ -eq 'InternalWhModPtr' -or $_ -match '^_Z\d+Wh_Mod(?:Init|Uninit|BeforeUninit|AfterInit|SettingsChanged)v$'
    })

    return [pscustomobject]@{
        size = $bytes.LongLength
        sha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes))
        machine = '0x8664'
        optionalHeaderMagic = '0x020B'
        isDll = $true
        isExecutableImage = $true
        numberOfSections = $numberOfSections
        sizeOfOptionalHeader = $sizeOfOptionalHeader
        sizeOfImage = $sizeOfImage
        timeDateStamp = $timeDateStamp
        exportCount = $exportNames.Count
        windhawkExports = $windhawkExports
    }
}

function Assert-ModuleMetadata {
    param([Parameter(Mandatory)] [pscustomobject]$Spec)

    $sourcePath = Assert-PathWithin -Path $Spec.source -Parent $root
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Module source is missing: $sourcePath"
    }
    $null = Assert-NoReparsePointsInPath -Path $sourcePath
    $sourceItem = Get-Item -LiteralPath $sourcePath -Force
    if (($sourceItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Reparse points aren't allowed in module inputs: $sourcePath"
    }
    $text = [System.IO.File]::ReadAllText($sourcePath)

    $includeFileNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($supportingSource in @($Spec.supportingSources)) {
        $supportingSourcePath = Assert-PathWithin -Path ([string]$supportingSource.path) -Parent $root
        if (-not (Test-Path -LiteralPath $supportingSourcePath -PathType Leaf)) {
            throw "Supporting source is missing for $($Spec.id): $supportingSourcePath"
        }
        $null = Assert-NoReparsePointsInPath -Path $supportingSourcePath
        $supportingSourceItem = Get-Item -LiteralPath $supportingSourcePath -Force
        if (($supportingSourceItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Reparse points aren't allowed in module inputs: $supportingSourcePath"
        }

        $includeFileName = [string]$supportingSource.includeFileName
        if ([string]::IsNullOrWhiteSpace($includeFileName) -or
            -not $includeFileName.Equals([System.IO.Path]::GetFileName($includeFileName), [StringComparison]::Ordinal) -or
            -not $includeFileName.Equals([System.IO.Path]::GetFileName($supportingSourcePath), [StringComparison]::Ordinal)) {
            throw "Supporting source include names must be exact leaf names: $includeFileName"
        }
        if (-not $includeFileNames.Add($includeFileName)) {
            throw "Duplicate supporting source include name for $($Spec.id): $includeFileName"
        }
        $quotedIncludePattern = '(?m)^\s*#include\s+"' + [regex]::Escape($includeFileName) + '"\s*$'
        if (-not [regex]::IsMatch($text, $quotedIncludePattern)) {
            throw "Module $($Spec.id) must directly include its supporting source with quotes: $includeFileName"
        }
    }

    $idPattern = '(?m)^// @id\s+' + [regex]::Escape($Spec.id) + '\s*$'
    $architecturePattern = '(?m)^// @architecture\s+' + [regex]::Escape($Spec.architecture) + '\s*$'
    if (-not [regex]::IsMatch($text, $idPattern)) {
        throw "Unexpected or missing @id metadata in $($Spec.source)."
    }
    if (-not [regex]::IsMatch($text, $architecturePattern)) {
        throw "Unexpected or missing @architecture metadata in $($Spec.source)."
    }
    if (-not [regex]::IsMatch($text, '(?m)^// @include\s+%SystemRoot%\\explorer\.exe\s*$')) {
        throw 'The only accepted host is %SystemRoot%\explorer.exe.'
    }
}

foreach ($spec in $selectedSpecs) {
    Assert-ModuleMetadata -Spec $spec
}

$toolchainLock = Get-Content -LiteralPath $toolchainLockPath -Raw | ConvertFrom-Json
if ($toolchainLock.schemaVersion -ne 2 -or $toolchainLock.compileInputTree.algorithm -ne 'sha256-path-size-content-v1') {
    throw 'Unsupported or incomplete toolchain lock schema.'
}

$toolCacheFullPath = Assert-NonSystemBuildRoot -Path $ToolCache
$null = Assert-NoReparsePointsInPath -Path $toolCacheFullPath

$portablePath = Join-Path $toolCacheFullPath 'portable'
$compilerScriptPath = Join-Path $toolCacheFullPath 'compile_mod.py'
$portableCompiler = Join-Path $portablePath 'Compiler\bin\clang++.exe'
$portableIniPath = Join-Path $portablePath 'windhawk.ini'
$portableProvisioningHint = "Manually pre-provision the complete locked Windhawk $($toolchainLock.windhawkVersion) portable directory at '$portablePath' from a trusted offline source, then rerun. This script never downloads or executes the Windhawk installer."

$mutexNameBytes = [System.Text.Encoding]::UTF8.GetBytes($toolCacheFullPath.ToUpperInvariant())
$mutexSuffix = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($mutexNameBytes)).Substring(0, 24)
$toolchainMutex = [System.Threading.Mutex]::new($false, "Local\JARVIS2-NativeBuild-$mutexSuffix")
$mutexAcquired = $false
$runStageDirectory = $null
try {
    try {
        $mutexAcquired = $toolchainMutex.WaitOne([TimeSpan]::FromSeconds(10))
    }
    catch [System.Threading.AbandonedMutexException] {
        $mutexAcquired = $true
    }
    if (-not $mutexAcquired) {
        throw 'Another JARVIS2 native build is using the shared toolchain cache.'
    }

    if (-not (Test-Path -LiteralPath $toolCacheFullPath -PathType Container)) {
        throw "Native tool cache is missing. $portableProvisioningHint"
    }
    $null = Assert-NoReparsePointsInPath -Path $toolCacheFullPath
    if (-not (Test-Path -LiteralPath $portablePath -PathType Container)) {
        throw "Pre-provisioned portable toolchain is missing. $portableProvisioningHint"
    }
    $null = Assert-NoReparsePointsInPath -Path $portablePath
    if (-not (Test-Path -LiteralPath $portableCompiler -PathType Leaf) -or
        -not (Test-Path -LiteralPath $portableIniPath -PathType Leaf)) {
        throw "Pre-provisioned portable toolchain is incomplete. $portableProvisioningHint"
    }

    $portableIni = [System.IO.File]::ReadAllText($portableIniPath)
    if (-not $portableIni.Contains('Portable=1') -or
        -not $portableIni.Contains("EnginePath=Engine\$($toolchainLock.windhawkVersion)") -or
        -not $portableIni.Contains('CompilerPath=Compiler')) {
        throw "Pre-provisioned portable Windhawk configuration failed validation. $portableProvisioningHint"
    }

    try {
        $compileInputAggregateBefore = Get-CompileInputAggregate -PortablePath $portablePath -Scopes $toolchainLock.compileInputTree.scopes
        Assert-CompileInputAggregate -Actual $compileInputAggregateBefore -Expected $toolchainLock.compileInputTree
    }
    catch {
        throw "Pre-provisioned portable toolchain doesn't match the complete locked input tree. $portableProvisioningHint Validation error: $($_.Exception.Message)"
    }

    Get-VerifiedDownload -Uri $toolchainLock.compilerScript.url -Destination $compilerScriptPath -Sha256 $toolchainLock.compilerScript.sha256

    $pyCommand = Get-Command py.exe -CommandType Application -ErrorAction Stop
    $pyLauncherPath = [System.IO.Path]::GetFullPath($pyCommand.Source)
    if ([System.IO.Path]::GetFileName($pyLauncherPath) -ne $toolchainLock.python.launcher.fileName -or
        (Get-Item -LiteralPath $pyLauncherPath).Length -ne [int64]$toolchainLock.python.launcher.size) {
        throw "Unexpected Python launcher identity: $pyLauncherPath"
    }
    $pyLauncherSha256 = Assert-FileHash -Path $pyLauncherPath -Expected $toolchainLock.python.launcher.sha256

    $pythonProbeCode = 'import json,sys; print(json.dumps({"executable":sys.executable,"version":".".join(map(str,sys.version_info[:3])),"basePrefix":sys.base_prefix}))'
    $pythonProbe = Invoke-CapturedProcess -FilePath $pyLauncherPath -Arguments @($toolchainLock.python.selector, '-I', '-S', '-c', $pythonProbeCode) -SanitizeBuildEnvironment
    if ($pythonProbe.exitCode -ne 0) {
        throw "Pinned Python probe failed with exit code $($pythonProbe.exitCode): $($pythonProbe.stderr)"
    }
    $pythonIdentity = $pythonProbe.stdout.Trim() | ConvertFrom-Json
    $pythonExecutablePath = [System.IO.Path]::GetFullPath([string]$pythonIdentity.executable)
    if ($pythonIdentity.version -ne $toolchainLock.python.interpreter.version -or
        [System.IO.Path]::GetFileName($pythonExecutablePath) -ne $toolchainLock.python.interpreter.fileName -or
        (Get-Item -LiteralPath $pythonExecutablePath).Length -ne [int64]$toolchainLock.python.interpreter.size) {
        throw "Unexpected Python interpreter identity: $($pythonProbe.stdout)"
    }
    $pythonExecutableSha256 = Assert-FileHash -Path $pythonExecutablePath -Expected $toolchainLock.python.interpreter.sha256
    $pythonRuntimePaths = [System.Collections.Generic.List[object]]::new()
    foreach ($runtimeFile in $toolchainLock.python.runtimeFiles) {
        $runtimePath = Join-Path ([string]$pythonIdentity.basePrefix) ([string]$runtimeFile.relativePath)
        if ((Get-Item -LiteralPath $runtimePath).Length -ne [int64]$runtimeFile.size) {
            throw "Python runtime file size mismatch: $($runtimeFile.relativePath)"
        }
        $null = Assert-FileHash -Path $runtimePath -Expected $runtimeFile.sha256
        $pythonRuntimePaths.Add([pscustomobject]@{
            path = $runtimePath
            sha256 = [string]$runtimeFile.sha256
        })
    }

    $clangProbe = Invoke-CapturedProcess -FilePath $portableCompiler -Arguments @('--version') -SanitizeBuildEnvironment
    if ($clangProbe.exitCode -ne 0) {
        throw 'The portable Clang compiler failed its version probe.'
    }
    $clangVersion = ($clangProbe.stdout -split '\r?\n' | Select-Object -First 1).Trim()
    $clangSha256 = (Get-FileHash -LiteralPath $portableCompiler -Algorithm SHA256).Hash
    $compilerScriptSha256 = Assert-FileHash -Path $compilerScriptPath -Expected $toolchainLock.compilerScript.sha256
    $toolchainLockSha256 = (Get-FileHash -LiteralPath $toolchainLockPath -Algorithm SHA256).Hash
    $buildScriptSha256 = (Get-FileHash -LiteralPath $buildScriptPath -Algorithm SHA256).Hash
    $testScriptSha256 = (Get-FileHash -LiteralPath $testScriptPath -Algorithm SHA256).Hash

    $inputValidation = [pscustomobject]@{
        schemaVersion = 1
        validatedAtUtc = [DateTimeOffset]::UtcNow
        toolchainLockSha256 = $toolchainLockSha256
        compileInputTree = $compileInputAggregateBefore
        python = [pscustomobject]@{
            launcherPath = $pyLauncherPath
            launcherSha256 = $pyLauncherSha256
            executablePath = $pythonExecutablePath
            executableSha256 = $pythonExecutableSha256
            version = $pythonIdentity.version
        }
        clang = [pscustomobject]@{
            path = $portableCompiler
            version = $clangVersion
            sha256 = $clangSha256
        }
    }
    if ($ValidateInputsOnly) {
        $inputValidation | ConvertTo-Json -Depth 8
        return
    }

    $outputRoot = Assert-NonSystemBuildRoot -Path $outputRoot
    $null = Assert-NoReparsePointsInPath -Path $outputRoot
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    $null = Assert-NoReparsePointsInPath -Path $outputRoot
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $runsRoot -Force | Out-Null
    $null = Assert-NoReparsePointsInPath -Path $stagingRoot
    $null = Assert-NoReparsePointsInPath -Path $runsRoot

    $runStageDirectory = Join-Path $stagingRoot $runId
    $runStageDirectory = Assert-PathWithin -Path $runStageDirectory -Parent $stagingRoot
    New-Item -ItemType Directory -Path $runStageDirectory | Out-Null
    $sourcesDirectory = Join-Path $runStageDirectory 'sources'
    $modulesDirectory = Join-Path $runStageDirectory 'modules'
    New-Item -ItemType Directory -Path $sourcesDirectory | Out-Null
    New-Item -ItemType Directory -Path $modulesDirectory | Out-Null

    $finalRunDirectory = Join-Path $runsRoot $runId
    $finalRunDirectory = Assert-PathWithin -Path $finalRunDirectory -Parent $runsRoot
    if (Test-Path -LiteralPath $finalRunDirectory) {
        throw "Immutable run directory already exists: $finalRunDirectory"
    }
    $runRelativeRoot = Get-NormalizedRelativePath -BasePath $root -Path $finalRunDirectory

    $evidenceIdentity = [pscustomobject]@{
        buildScript = [pscustomobject]@{
            relativePath = Get-NormalizedRelativePath -BasePath $root -Path $buildScriptPath
            sha256 = $buildScriptSha256
        }
        testScript = [pscustomobject]@{
            relativePath = Get-NormalizedRelativePath -BasePath $root -Path $testScriptPath
            sha256 = $testScriptSha256
        }
        toolchainLock = [pscustomobject]@{
            relativePath = Get-NormalizedRelativePath -BasePath $root -Path $toolchainLockPath
            sha256 = $toolchainLockSha256
        }
    }
    $toolchainIdentity = [pscustomobject]@{
        windhawkVersion = $toolchainLock.windhawkVersion
        windhawkCommit = $toolchainLock.windhawkCommit
        sourceInstallerLockSha256 = $toolchainLock.sourceInstaller.sha256
        sourceInstallerLockSigner = $toolchainLock.sourceInstaller.signerSubject
        installerExecution = 'forbidden'
        portableProvisioning = 'preprovisioned-validated'
        compilerScriptSha256 = $compilerScriptSha256
        compileInputTree = $compileInputAggregateBefore
        clangVersion = $clangVersion
        clangSha256 = $clangSha256
        pythonVersion = $pythonIdentity.version
        pythonExecutableSha256 = $pythonExecutableSha256
        pythonLauncherSha256 = $pyLauncherSha256
        environmentVariablesCleared = $environmentVariablesClearedForBuild
    }

    $runModules = [System.Collections.Generic.List[object]]::new()
    foreach ($spec in $selectedSpecs) {
        Assert-ModuleMetadata -Spec $spec
        $sourceSha256 = (Get-FileHash -LiteralPath $spec.source -Algorithm SHA256).Hash
        $moduleSourcesDirectory = Join-Path $sourcesDirectory $spec.id
        $moduleSourcesDirectory = Assert-PathWithin -Path $moduleSourcesDirectory -Parent $sourcesDirectory
        New-Item -ItemType Directory -Path $moduleSourcesDirectory | Out-Null
        $null = Assert-NoReparsePointsInPath -Path $moduleSourcesDirectory
        $sourceSnapshotPath = Join-Path $moduleSourcesDirectory "$($spec.id)-$sourceSha256.wh.cpp"
        Copy-Item -LiteralPath $spec.source -Destination $sourceSnapshotPath
        $null = Assert-FileHash -Path $sourceSnapshotPath -Expected $sourceSha256
        $null = Assert-FileHash -Path $spec.source -Expected $sourceSha256

        $supportingSourceReceipts = [System.Collections.Generic.List[object]]::new()
        foreach ($supportingSource in @($spec.supportingSources)) {
            $supportingSourcePath = [System.IO.Path]::GetFullPath([string]$supportingSource.path)
            $supportingSourceSha256 = (Get-FileHash -LiteralPath $supportingSourcePath -Algorithm SHA256).Hash
            $supportingSourceSnapshotPath = Join-Path $moduleSourcesDirectory ([string]$supportingSource.includeFileName)
            Copy-Item -LiteralPath $supportingSourcePath -Destination $supportingSourceSnapshotPath
            $null = Assert-FileHash -Path $supportingSourceSnapshotPath -Expected $supportingSourceSha256
            $null = Assert-FileHash -Path $supportingSourcePath -Expected $supportingSourceSha256

            $supportingSourceSnapshotRelativePath = "$runRelativeRoot/sources/$($spec.id)/$([System.IO.Path]::GetFileName($supportingSourceSnapshotPath))"
            $supportingSourceReceipts.Add([pscustomobject]@{
                path = Get-NormalizedRelativePath -BasePath $root -Path $supportingSourcePath
                includeFileName = [string]$supportingSource.includeFileName
                size = (Get-Item -LiteralPath $supportingSourcePath).Length
                sha256 = $supportingSourceSha256
                snapshot = [pscustomobject]@{
                    relativePath = $supportingSourceSnapshotRelativePath
                    size = (Get-Item -LiteralPath $supportingSourceSnapshotPath).Length
                    sha256 = $supportingSourceSha256
                }
            })
        }

        $moduleStageDirectory = Join-Path $modulesDirectory $spec.id
        New-Item -ItemType Directory -Path $moduleStageDirectory | Out-Null
        $x86Path = Join-Path $moduleStageDirectory "$($spec.id)-x86.dll"
        $x64Path = Join-Path $moduleStageDirectory "$($spec.id)-x64.dll"
        $arm64Path = Join-Path $moduleStageDirectory "$($spec.id)-arm64.dll"

        $compileResult = Invoke-CapturedProcess -FilePath $pythonExecutablePath -Arguments @(
            '-I', '-S',
            $compilerScriptPath,
            '-w', $portablePath,
            '-f', $sourceSnapshotPath,
            '-o32', $x86Path,
            '-o64', $x64Path,
            '-oarm64', $arm64Path
        ) -SanitizeBuildEnvironment
        $compileLogText = @(
            "process=$($compileResult.filePath)",
            "exitCode=$($compileResult.exitCode)",
            '--- stdout ---',
            $compileResult.stdout.TrimEnd(),
            '--- stderr ---',
            $compileResult.stderr.TrimEnd()
        ) -join [Environment]::NewLine
        $compileLogText += [Environment]::NewLine
        $compileLogPath = Join-Path $moduleStageDirectory 'compile.log'
        Write-AtomicUtf8Text -Path $compileLogPath -Text $compileLogText

        if ($compileResult.exitCode -ne 0) {
            throw "Windhawk compilation failed for $($spec.id) with exit code $($compileResult.exitCode).`n$compileLogText"
        }
        if (-not (Test-Path -LiteralPath $x64Path)) {
            throw "The AMD64 output wasn't produced for $($spec.id)."
        }
        if (Test-Path -LiteralPath $x86Path) {
            throw "Unexpected x86 output was produced for AMD64-only module $($spec.id)."
        }
        if (Test-Path -LiteralPath $arm64Path) {
            throw "Unexpected ARM64 output was produced for AMD64-only module $($spec.id)."
        }

        $warningCount = @($compileLogText -split '\r?\n' | Where-Object { $_ -match '(?i)\bwarning(?:\s+[A-Z]+\d+)?\s*:' }).Count
        $errorCount = @($compileLogText -split '\r?\n' | Where-Object { $_ -match '(?i)\berror(?:\s+[A-Z]+\d+)?\s*:' }).Count
        if ($warningCount -ne 0 -or $errorCount -ne 0) {
            throw "Compilation emitted warnings or errors for $($spec.id)."
        }

        $peInfo = Get-StrictPeInfo -Path $x64Path
        $moduleRelativeBase = "$runRelativeRoot/modules/$($spec.id)"
        $sourceSnapshotRelativePath = "$runRelativeRoot/sources/$($spec.id)/$([System.IO.Path]::GetFileName($sourceSnapshotPath))"
        $artifactRelativePath = "$moduleRelativeBase/$([System.IO.Path]::GetFileName($x64Path))"
        $compileLogRelativePath = "$moduleRelativeBase/compile.log"
        $moduleReceiptRelativePath = "$moduleRelativeBase/build-receipt.json"

        $moduleReceipt = [pscustomobject]@{
            schemaVersion = 3
            runId = $runId
            builtAtUtc = [DateTimeOffset]::UtcNow
            evidence = $evidenceIdentity
            module = [pscustomobject]@{
                id = $spec.id
                architecture = $spec.architecture
                sourcePath = Get-NormalizedRelativePath -BasePath $root -Path $spec.source
                sourceSha256 = $sourceSha256
                sourceSnapshot = [pscustomobject]@{
                    relativePath = $sourceSnapshotRelativePath
                    size = (Get-Item -LiteralPath $sourceSnapshotPath).Length
                    sha256 = $sourceSha256
                }
                supportingSources = $supportingSourceReceipts.ToArray()
            }
            toolchain = $toolchainIdentity
            result = [pscustomobject]@{
                exitCode = $compileResult.exitCode
                warningCount = $warningCount
                errorCount = $errorCount
                compileLog = [pscustomobject]@{
                    relativePath = $compileLogRelativePath
                    size = (Get-Item -LiteralPath $compileLogPath).Length
                    sha256 = (Get-FileHash -LiteralPath $compileLogPath -Algorithm SHA256).Hash
                }
            }
            output = [pscustomobject]@{
                relativePath = $artifactRelativePath
                fileName = [System.IO.Path]::GetFileName($x64Path)
                size = $peInfo.size
                sha256 = $peInfo.sha256
                pe = [pscustomobject]@{
                    machine = $peInfo.machine
                    optionalHeaderMagic = $peInfo.optionalHeaderMagic
                    isDll = $peInfo.isDll
                    isExecutableImage = $peInfo.isExecutableImage
                    numberOfSections = $peInfo.numberOfSections
                    sizeOfOptionalHeader = $peInfo.sizeOfOptionalHeader
                    sizeOfImage = $peInfo.sizeOfImage
                    timeDateStamp = $peInfo.timeDateStamp
                }
                exportCount = $peInfo.exportCount
                windhawkExports = $peInfo.windhawkExports
            }
            activationPermitted = $false
            liveExplorer = 'not-run'
        }

        $moduleReceiptPath = Join-Path $moduleStageDirectory 'build-receipt.json'
        Write-AtomicUtf8Json -Path $moduleReceiptPath -Value $moduleReceipt
        $runModules.Add([pscustomobject]@{
            id = $spec.id
            architecture = $spec.architecture
            sourcePath = $moduleReceipt.module.sourcePath
            sourceSha256 = $sourceSha256
            sourceSnapshot = $moduleReceipt.module.sourceSnapshot
            supportingSources = $moduleReceipt.module.supportingSources
            artifact = $moduleReceipt.output
            compileLog = $moduleReceipt.result.compileLog
            moduleReceipt = [pscustomobject]@{
                relativePath = $moduleReceiptRelativePath
                size = (Get-Item -LiteralPath $moduleReceiptPath).Length
                sha256 = (Get-FileHash -LiteralPath $moduleReceiptPath -Algorithm SHA256).Hash
            }
            result = [pscustomobject]@{
                exitCode = 0
                warningCount = 0
                errorCount = 0
            }
        })
    }

    $compileInputAggregateAfter = Get-CompileInputAggregate -PortablePath $portablePath -Scopes $toolchainLock.compileInputTree.scopes
    Assert-CompileInputAggregate -Actual $compileInputAggregateAfter -Expected $toolchainLock.compileInputTree
    $null = Assert-FileHash -Path $compilerScriptPath -Expected $compilerScriptSha256
    $null = Assert-FileHash -Path $pyLauncherPath -Expected $pyLauncherSha256
    $null = Assert-FileHash -Path $pythonExecutablePath -Expected $pythonExecutableSha256
    foreach ($runtimeIdentity in $pythonRuntimePaths) {
        $null = Assert-FileHash -Path $runtimeIdentity.path -Expected $runtimeIdentity.sha256
    }
    $null = Assert-FileHash -Path $toolchainLockPath -Expected $toolchainLockSha256
    $null = Assert-FileHash -Path $buildScriptPath -Expected $buildScriptSha256
    $null = Assert-FileHash -Path $testScriptPath -Expected $testScriptSha256
    foreach ($moduleResult in $runModules) {
        $currentSourcePath = Join-Path $root ([string]$moduleResult.sourcePath).Replace('/', '\')
        $null = Assert-FileHash -Path $currentSourcePath -Expected $moduleResult.sourceSha256
        foreach ($supportingSource in @($moduleResult.supportingSources)) {
            $currentSupportingSourcePath = Join-Path $root ([string]$supportingSource.path).Replace('/', '\')
            $currentSupportingSourcePath = Assert-PathWithin -Path $currentSupportingSourcePath -Parent $root
            $null = Assert-NoReparsePointsInPath -Path $currentSupportingSourcePath
            if ((Get-Item -LiteralPath $currentSupportingSourcePath -Force).Length -ne [int64]$supportingSource.size) {
                throw "Supporting source size changed before publication: $currentSupportingSourcePath"
            }
            $null = Assert-FileHash -Path $currentSupportingSourcePath -Expected $supportingSource.sha256
        }
    }

    $selectedIds = [string[]]@($selectedSpecs | ForEach-Object id)
    [Array]::Sort($selectedIds, [StringComparer]::Ordinal)
    $allIds = [string[]]@($moduleSpecs.Keys)
    [Array]::Sort($allIds, [StringComparer]::Ordinal)
    $isCanonicalFullRun = ($selectedIds -join ',') -eq ($allIds -join ',')

    $runSummary = [pscustomobject]@{
        schemaVersion = 3
        runId = $runId
        status = 'complete'
        startedAtUtc = $runStartedAtUtc
        completedAtUtc = [DateTimeOffset]::UtcNow
        canonicalFullRun = $isCanonicalFullRun
        moduleIds = $selectedIds
        evidence = $evidenceIdentity
        toolchain = $toolchainIdentity
        modules = $runModules
        activationPermitted = $false
        liveExplorer = 'not-run'
        scope = 'Offline compile proof only. No module was installed, enabled, or loaded into Explorer.'
    }
    $runSummaryStagePath = Join-Path $runStageDirectory 'run-summary.json'
    Write-AtomicUtf8Json -Path $runSummaryStagePath -Value $runSummary
    $runSummarySha256 = (Get-FileHash -LiteralPath $runSummaryStagePath -Algorithm SHA256).Hash
    $runSummarySize = (Get-Item -LiteralPath $runSummaryStagePath).Length

    $null = Assert-NoReparsePointsInPath -Path $runStageDirectory
    [System.IO.Directory]::Move($runStageDirectory, $finalRunDirectory)
    $runStageDirectory = $null

    $finalRunSummaryPath = Join-Path $finalRunDirectory 'run-summary.json'
    $null = Assert-FileHash -Path $finalRunSummaryPath -Expected $runSummarySha256
    foreach ($moduleResult in $runModules) {
        $moduleEvidenceFiles = @($moduleResult.sourceSnapshot, $moduleResult.artifact, $moduleResult.compileLog, $moduleResult.moduleReceipt)
        $moduleEvidenceFiles += @($moduleResult.supportingSources | ForEach-Object { $_.snapshot })
        foreach ($evidenceFile in $moduleEvidenceFiles) {
            $evidencePath = Join-Path $root ([string]$evidenceFile.relativePath).Replace('/', '\')
            $null = Assert-FileHash -Path $evidencePath -Expected $evidenceFile.sha256
        }

        $currentSourcePath = Join-Path $root ([string]$moduleResult.sourcePath).Replace('/', '\')
        $null = Assert-FileHash -Path $currentSourcePath -Expected $moduleResult.sourceSha256
        foreach ($supportingSource in @($moduleResult.supportingSources)) {
            $currentSupportingSourcePath = Join-Path $root ([string]$supportingSource.path).Replace('/', '\')
            $currentSupportingSourcePath = Assert-PathWithin -Path $currentSupportingSourcePath -Parent $root
            $null = Assert-NoReparsePointsInPath -Path $currentSupportingSourcePath
            if ((Get-Item -LiteralPath $currentSupportingSourcePath -Force).Length -ne [int64]$supportingSource.size) {
                throw "Supporting source size changed after publication: $currentSupportingSourcePath"
            }
            $null = Assert-FileHash -Path $currentSupportingSourcePath -Expected $supportingSource.sha256
        }
    }

    $committedReceiptWritten = $false
    if ($isCanonicalFullRun -and (Test-PathEqual -Left $outputRoot -Right $canonicalOutputRoot)) {
        $committedReceipt = [pscustomobject]@{
            schemaVersion = 3
            generatedBy = 'scripts/Build-NativeMod.ps1'
            generatedAtUtc = [DateTimeOffset]::UtcNow
            runId = $runId
            evidence = $evidenceIdentity
            runSummary = [pscustomobject]@{
                relativePath = "artifacts/native/runs/$runId/run-summary.json"
                size = $runSummarySize
                sha256 = $runSummarySha256
            }
            toolchain = $toolchainIdentity
            modules = $runModules
            offlineEvidenceReady = $true
            releaseReady = $false
            activationPermitted = $false
            liveExplorer = 'not-run'
            scope = 'Canonical offline build evidence. Live activation remains forbidden until the separate safety gate and explicit user approval.'
        }
        Write-AtomicUtf8Json -Path $committedReceiptPath -Value $committedReceipt
        $committedReceiptWritten = $true
    }

    [pscustomobject]@{
        schemaVersion = 3
        runId = $runId
        status = 'complete'
        directory = $finalRunDirectory
        runSummary = $finalRunSummaryPath
        runSummarySha256 = $runSummarySha256
        modules = $runModules
        committedReceiptWritten = $committedReceiptWritten
        committedReceipt = if ($committedReceiptWritten) { $committedReceiptPath } else { $null }
        activationPermitted = $false
        liveExplorer = 'not-run'
    } | ConvertTo-Json -Depth 12
}
catch {
    if ($null -ne $runStageDirectory -and (Test-Path -LiteralPath $runStageDirectory)) {
        Remove-SafeTree -Path $runStageDirectory -AllowedParent $stagingRoot
    }
    throw
}
finally {
    if ($mutexAcquired) {
        $toolchainMutex.ReleaseMutex()
    }
    $toolchainMutex.Dispose()
}
