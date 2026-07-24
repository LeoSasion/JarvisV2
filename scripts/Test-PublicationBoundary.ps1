[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'config\publication-manifest.json'
$manifest =
    Get-Content -LiteralPath $manifestPath -Raw |
        ConvertFrom-Json -Depth 100
$errors = [System.Collections.Generic.List[string]]::new()

function Add-BoundaryError {
    param([Parameter(Mandatory)] [string]$Code)
    if (-not $errors.Contains($Code)) {
        $errors.Add($Code)
    }
}

if ($manifest.schemaVersion -ne 1 -or
    $manifest.repositoryName -ne 'JarvisV2' -or
    $manifest.internalRuntimeNamespace -ne 'JARVIS2' -or
    $manifest.license -ne 'GPL-3.0') {
    Add-BoundaryError 'publication-manifest-identity-invalid'
}

$gitOutput = & git -C $root ls-files --cached --others --exclude-standard
if ($LASTEXITCODE -ne 0) {
    throw 'git ls-files failed.'
}
$candidatePaths = @(
    $gitOutput |
        ForEach-Object { ([string]$_).Replace('\', '/') } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique
)
if ($candidatePaths.Count -eq 0) {
    Add-BoundaryError 'publication-candidate-set-empty'
}

$requiredFiles = @($manifest.requiredFiles)
foreach ($required in $requiredFiles) {
    if ($required -notin $candidatePaths -or
        -not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf)) {
        Add-BoundaryError "required-file-missing:$required"
    }
}

$gitIgnoreText =
    [System.IO.File]::ReadAllText((Join-Path $root '.gitignore'))
foreach ($requiredIgnore in @(
    'artifacts/',
    'tools/',
    '**/bin/',
    '**/obj/',
    '*.dll',
    '*.exe',
    '*.pdb',
    '.env'
)) {
    if (-not $gitIgnoreText.Contains($requiredIgnore)) {
        Add-BoundaryError "gitignore-contract-missing:$requiredIgnore"
    }
}

$forbiddenExtensions =
    @($manifest.forbiddenExtensions | ForEach-Object { $_.ToLowerInvariant() })
$maxBytes = [int64]$manifest.maxTrackedFileBytes
$candidateBytes = [int64]0
$textExtensions = @(
    '',
    '.cs',
    '.csproj',
    '.cpp',
    '.hpp',
    '.json',
    '.md',
    '.ps1',
    '.yml',
    '.yaml'
)
$localUserPathWindows =
    'C:' + [char]92 + 'Users' + [char]92 + 'Administrator'
$localUserPathSlash =
    'C:' + '/' + 'Users' + '/' + 'Administrator'
$secretPatterns = [ordered]@{
    privateKey = '-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'
    githubToken = 'gh[pousr]_[A-Za-z0-9_]{20,}'
    awsAccessKey = 'AKIA[0-9A-Z]{16}'
    bearerToken = '(?i)authorization\s*[:=]\s*bearer\s+[A-Za-z0-9._-]{16,}'
    localAdministratorPath =
        '(?i)' +
        [regex]::Escape($localUserPathWindows) +
        '|' +
        [regex]::Escape($localUserPathSlash)
}

foreach ($relativePath in $candidatePaths) {
    if ([IO.Path]::IsPathRooted($relativePath) -or
        $relativePath.Split('/') -contains '..') {
        Add-BoundaryError "unsafe-relative-path:$relativePath"
        continue
    }
    foreach ($excludedRoot in @($manifest.excludedRoots)) {
        if ($relativePath.StartsWith(
                [string]$excludedRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            Add-BoundaryError "excluded-root-candidate:$relativePath"
        }
    }
    $fullPath = [IO.Path]::GetFullPath(
        (Join-Path $root $relativePath.Replace('/', '\')))
    $fullRoot = [IO.Path]::GetFullPath($root).TrimEnd('\')
    if (-not $fullPath.StartsWith(
            $fullRoot + '\',
            [StringComparison]::OrdinalIgnoreCase)) {
        Add-BoundaryError "candidate-escaped-root:$relativePath"
        continue
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Add-BoundaryError "candidate-not-file:$relativePath"
        continue
    }
    $item = Get-Item -LiteralPath $fullPath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Add-BoundaryError "candidate-reparse-point:$relativePath"
    }
    if ($item.Length -gt $maxBytes) {
        Add-BoundaryError "candidate-too-large:$relativePath"
    }
    $candidateBytes += [int64]$item.Length
    $extension = [IO.Path]::GetExtension($relativePath).ToLowerInvariant()
    if ($extension -in $forbiddenExtensions) {
        Add-BoundaryError "forbidden-extension:$relativePath"
    }
    if ($extension -in $textExtensions) {
        try {
            $bytes = [IO.File]::ReadAllBytes($fullPath)
            if ($bytes -contains 0) {
                Add-BoundaryError "nul-byte-in-text:$relativePath"
                continue
            }
            $text = [Text.Encoding]::UTF8.GetString($bytes)
            foreach ($entry in $secretPatterns.GetEnumerator()) {
                if ([regex]::IsMatch($text, [string]$entry.Value)) {
                    Add-BoundaryError "sensitive-pattern:$($entry.Key):$relativePath"
                }
            }
        }
        catch {
            Add-BoundaryError "text-scan-failed:$relativePath"
        }
    }
}

$licenseText = [System.IO.File]::ReadAllText((Join-Path $root 'LICENSE'))
$noticeText =
    [System.IO.File]::ReadAllText((Join-Path $root 'third_party\NOTICE.md'))
$readmeText =
    [System.IO.File]::ReadAllText((Join-Path $root 'README.md'))
$compatibilityText =
    [System.IO.File]::ReadAllText((Join-Path $root 'config\compatibility.json'))
$workflowText =
    [System.IO.File]::ReadAllText((Join-Path $root '.github\workflows\ci.yml'))

if (-not $licenseText.Contains('GNU GENERAL PUBLIC LICENSE') -or
    -not $licenseText.Contains('Version 3, 29 June 2007')) {
    Add-BoundaryError 'gpl-v3-text-incomplete'
}
if (-not $noticeText.Contains('Windows 11 Taskbar Styler') -or
    -not $noticeText.Contains('Taskbar height and icon size') -or
    -not $noticeText.Contains('eDEX-UI')) {
    Add-BoundaryError 'third-party-notice-incomplete'
}
if (-not $readmeText.StartsWith('# JarvisV2') -or
    -not $readmeText.Contains('内部运行时安全标识仍为 `JARVIS2`')) {
    Add-BoundaryError 'public-name-runtime-boundary-missing'
}
if (-not $compatibilityText.Contains('"project": "JARVIS2"') -or
    -not $compatibilityText.Contains(
        '"supervisorActivationEligible": false')) {
    Add-BoundaryError 'runtime-compatibility-boundary-changed'
}
if (-not [regex]::IsMatch(
        $workflowText,
        '(?m)^\s*permissions:\s*\r?\n\s+contents:\s+read\s*$') -or
    $workflowText.Contains('pull_request_target') -or
    [regex]::Matches(
        $workflowText,
        '(?m)^\s*uses:\s+[^@\r\n]+@([A-Fa-f0-9]{40})\s*$').Count -ne 2 -or
    [regex]::IsMatch(
        $workflowText,
        '(?i)run:.*(?:clear-kill-switch|restart-explorer|windhawk\.exe)')) {
    Add-BoundaryError 'public-ci-safety-boundary-invalid'
}

$result = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-publication-boundary'
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    result = if ($errors.Count -eq 0) { 'passed' } else { 'failed' }
    repositoryName = [string]$manifest.repositoryName
    internalRuntimeNamespace = [string]$manifest.internalRuntimeNamespace
    candidateFileCount = $candidatePaths.Count
    candidateBytes = $candidateBytes
    maxTrackedFileBytes = $maxBytes
    secretValuesPrinted = $false
    errors = @($errors)
}
$result | ConvertTo-Json -Depth 8
if ($errors.Count -ne 0) {
    exit 1
}
