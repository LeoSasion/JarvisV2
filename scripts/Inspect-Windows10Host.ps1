[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$currentVersionPath =
    'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$observedAtUtc = [DateTimeOffset]::UtcNow

try {
    $currentVersion = Get-ItemProperty -LiteralPath $currentVersionPath
    $buildText = [string]$currentVersion.CurrentBuildNumber
    $build = 0
    $buildParsed = [int]::TryParse($buildText, [ref]$build)
    $ubr = if ($null -ne $currentVersion.UBR) {
        [int]$currentVersion.UBR
    }
    else {
        $null
    }
    $explorerPath = Join-Path $env:SystemRoot 'explorer.exe'
    $explorer = Get-Item -LiteralPath $explorerPath -Force
    $isClient = [string]$currentVersion.InstallationType -eq 'Client'
    $isWindows10Build = $buildParsed -and $build -ge 10240 -and $build -lt 22000

    $result = [ordered]@{
        schemaVersion = 1
        receiptType = 'jarvisv2-windows10-host-inventory'
        result = if ($isClient -and $isWindows10Build) {
            'passed-read-only-windows10-candidate'
        }
        else {
            'incompatible-host'
        }
        observedAtUtc = $observedAtUtc.ToString('o')
        mutationPerformed = $false
        activationPermitted = $false
        liveExplorer = 'not-run'
        host = [ordered]@{
            productName = [string]$currentVersion.ProductName
            displayVersion = [string]$currentVersion.DisplayVersion
            editionId = [string]$currentVersion.EditionID
            installationType = [string]$currentVersion.InstallationType
            build = if ($buildParsed) { $build } else { $null }
            ubr = $ubr
            architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            is64BitOperatingSystem = [Environment]::Is64BitOperatingSystem
        }
        explorer = [ordered]@{
            path = $explorer.FullName
            size = [int64]$explorer.Length
            productVersion = [string]$explorer.VersionInfo.ProductVersion
            fileVersion = [string]$explorer.VersionInfo.FileVersion
            sha256 = (Get-FileHash -LiteralPath $explorer.FullName -Algorithm SHA256).Hash
        }
        nextStep = if ($isClient -and $isWindows10Build) {
            'Review this inventory and create a new exact Win10 compatibility profile. Do not activate a module.'
        }
        else {
            'Do not use the Win10 backend on this host.'
        }
    }
}
catch {
    $result = [ordered]@{
        schemaVersion = 1
        receiptType = 'jarvisv2-windows10-host-inventory'
        result = 'failed'
        observedAtUtc = $observedAtUtc.ToString('o')
        mutationPerformed = $false
        activationPermitted = $false
        liveExplorer = 'not-run'
        error = "$($_.Exception.GetType().Name): $($_.Exception.Message)"
    }
}

$result | ConvertTo-Json -Depth 8
if ($result.result -eq 'failed') {
    exit 1
}
