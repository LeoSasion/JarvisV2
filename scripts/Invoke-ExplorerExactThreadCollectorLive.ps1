[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory,

    [ValidateRange(10, 60)]
    [int]$SessionTimeoutSeconds = 20,

    [string]$ControllerReceiptPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This public entry point is intentionally blocked before it resolves the
# package, reads or changes JARVIS2 state, acquires a mutex, enumerates
# Explorer, or launches any process. Native unload/callback-drain proof is not
# closed, so no live admission path exists in this revision.
$blockReason = (
    'Native collector unload and callback-drain proof is not closed; ' +
    'Explorer activation remains hard-disabled and offline-only.')
$receipt = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-win10-explorer-exact-thread-live-controller'
    result = 'blocked'
    blockReason = $blockReason
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    packageDirectoryArgument = $PackageDirectory
    sessionTimeoutSecondsArgument = $SessionTimeoutSeconds
    offlineOnly = $true
    collectorExecutablePublished = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
}
$json = ($receipt | ConvertTo-Json -Depth 6) + [Environment]::NewLine

if (-not [string]::IsNullOrWhiteSpace($ControllerReceiptPath)) {
    $resolvedReceipt = [IO.Path]::GetFullPath($ControllerReceiptPath)
    $receiptParent = Split-Path -Parent $resolvedReceipt
    if (
        [IO.Path]::GetFileName($resolvedReceipt) -cne 'controller-receipt.json' -or
        -not (Test-Path -LiteralPath $receiptParent -PathType Container)
    ) {
        throw (
            'ControllerReceiptPath must explicitly name controller-receipt.json ' +
            'inside an existing directory.')
    }
    [IO.File]::WriteAllText(
        $resolvedReceipt,
        $json,
        [Text.UTF8Encoding]::new($false))
}

$json.TrimEnd()
exit 31
