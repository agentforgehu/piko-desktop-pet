param([string]$ExpectedVersion = '')

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$version = (Get-Content -LiteralPath (Join-Path $projectRoot 'release-version.txt') -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    $ExpectedVersion = $version
}
if ($version -ne $ExpectedVersion) {
    throw "release-version.txt is $version but the requested version is $ExpectedVersion."
}
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Piko release version must be a three-part numeric version: $version"
}

function Assert-ContainsLiteral {
    param([string]$RelativePath, [string]$ExpectedText)
    $path = Join-Path $projectRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required versioned file is missing: $RelativePath"
    }
    $content = Get-Content -LiteralPath $path -Raw
    if (-not $content.Contains($ExpectedText)) {
        throw "$RelativePath is not synchronized to Piko $version. Missing: $ExpectedText"
    }
}

$package = Get-Content -LiteralPath (Join-Path $projectRoot 'integrations\vscode\package.json') -Raw | ConvertFrom-Json
$packageLockText = Get-Content -LiteralPath (Join-Path $projectRoot 'integrations\vscode\package-lock.json') -Raw
$escapedVersion = [Regex]::Escape($version)
$lockVersions = [Regex]::Matches($packageLockText, '"version"\s*:\s*"' + $escapedVersion + '"')
if ($package.version -ne $version -or $lockVersions.Count -lt 2) {
    throw "VS Code extension versions are not synchronized to Piko $version."
}

Assert-ContainsLiteral 'README.md' $version
Assert-ContainsLiteral 'CHANGELOG.md' "## $version"
Assert-ContainsLiteral 'docs\USER_GUIDE_ZH.md' "Piko Desktop Pet $version"
Assert-ContainsLiteral 'docs\USER_GUIDE_ZH.md' "Piko-$version-Setup.exe"
Assert-ContainsLiteral 'docs\USER_GUIDE_ZH.md' "piko-context-bridge-$version.vsix"
Assert-ContainsLiteral 'docs\V1_PRODUCTION_GAP_MATRIX.md' $version

Write-Host "Version sync verified: Piko $version"

