param(
    [string]$Version = '0.2.1',
    [ValidateSet('auto', 'preview', 'stable')]
    [string]$Channel = 'auto',
    [string]$SignToolPath = '',
    [string]$SigningCertificateThumbprint = '',
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$AllowUnsignedStable
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid semantic version: $Version"
}

$version = $Version
$versionCore = ($version -split '-', 2)[0]
$versionParts = $versionCore.Split('.')
$binaryVersion = '{0}.{1}.{2}.0' -f $versionParts[0], $versionParts[1], $versionParts[2]
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$workspaceRoot = (Resolve-Path (Join-Path $projectRoot '..\..')).Path
$releaseRoot = Join-Path $projectRoot 'releases'
$publishDirectory = Join-Path $releaseRoot "Piko-$version-win-x64"
$archivePath = Join-Path $releaseRoot "Piko-$version-win-x64.zip"
$checksumPath = "$archivePath.sha256.txt"
$extensionPath = Join-Path $releaseRoot "piko-context-bridge-$version.vsix"
$extensionChecksumPath = "$extensionPath.sha256.txt"
$setupPath = Join-Path $releaseRoot "Piko-$version-Setup.exe"
$setupChecksumPath = "$setupPath.sha256.txt"
$updateManifestPath = Join-Path $releaseRoot 'update-manifest.json'
$updateManifestChecksumPath = "$updateManifestPath.sha256.txt"
$setupPublishDirectory = Join-Path $releaseRoot '.setup-publish'
$localDotnet = Join-Path $workspaceRoot 'work\.dotnet\dotnet.exe'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue

if ($dotnetCommand) {
    $dotnet = $dotnetCommand.Source
} elseif (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
} else {
    throw '.NET 8 SDK was not found.'
}

$resolvedProjectRoot = [IO.Path]::GetFullPath($projectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$resolvedPublishDirectory = [IO.Path]::GetFullPath($publishDirectory)
$resolvedArchivePath = [IO.Path]::GetFullPath($archivePath)
$resolvedExtensionPath = [IO.Path]::GetFullPath($extensionPath)
$resolvedSetupPath = [IO.Path]::GetFullPath($setupPath)
$resolvedSetupPublishDirectory = [IO.Path]::GetFullPath($setupPublishDirectory)
$resolvedUpdateManifestPath = [IO.Path]::GetFullPath($updateManifestPath)
if (!$resolvedPublishDirectory.StartsWith($resolvedProjectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    !$resolvedArchivePath.StartsWith($resolvedProjectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    !$resolvedExtensionPath.StartsWith($resolvedProjectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    !$resolvedSetupPath.StartsWith($resolvedProjectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    !$resolvedSetupPublishDirectory.StartsWith($resolvedProjectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    !$resolvedUpdateManifestPath.StartsWith($resolvedProjectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to publish outside the Piko project directory.'
}

$releaseChannel = if ($Channel -ne 'auto') {
    $Channel
} elseif ($version.Contains('-') -or [int]$versionParts[0] -eq 0) {
    'preview'
} else {
    'stable'
}
$shouldSign = -not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)
if ($releaseChannel -eq 'stable' -and -not $shouldSign -and -not $AllowUnsignedStable) {
    throw 'A stable release requires Authenticode signing. Supply -SigningCertificateThumbprint and -SignToolPath, or explicitly use -AllowUnsignedStable for non-production testing.'
}
if ($shouldSign) {
    if ([string]::IsNullOrWhiteSpace($SignToolPath)) {
        $signToolCommand = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if (-not $signToolCommand) {
            throw 'signtool.exe was not found. Supply -SignToolPath.'
        }
        $SignToolPath = $signToolCommand.Source
    }
    if (-not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) {
        throw 'The supplied signtool.exe path does not exist.'
    }
    if ($TimestampUrl -notmatch '^https?://') {
        throw 'TimestampUrl must be an HTTP(S) URL.'
    }
}

function Invoke-PikoCodeSign {
    param([Parameter(Mandatory = $true)][string[]]$Files)

    if (-not $shouldSign) {
        return
    }

    foreach ($file in $Files) {
        & $SignToolPath sign /fd SHA256 /sha1 $SigningCertificateThumbprint /tr $TimestampUrl /td SHA256 $file
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & $SignToolPath verify /pa $file
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}

$env:DOTNET_CLI_HOME = Join-Path $workspaceRoot 'work\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $workspaceRoot 'work\nuget-packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
if (Test-Path -LiteralPath $checksumPath) {
    Remove-Item -LiteralPath $checksumPath -Force
}
if (Test-Path -LiteralPath $extensionPath) {
    Remove-Item -LiteralPath $extensionPath -Force
}
if (Test-Path -LiteralPath $extensionChecksumPath) {
    Remove-Item -LiteralPath $extensionChecksumPath -Force
}
if (Test-Path -LiteralPath $setupPath) {
    Remove-Item -LiteralPath $setupPath -Force
}
if (Test-Path -LiteralPath $setupChecksumPath) {
    Remove-Item -LiteralPath $setupChecksumPath -Force
}
if (Test-Path -LiteralPath $setupPublishDirectory) {
    Remove-Item -LiteralPath $setupPublishDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $updateManifestPath) {
    Remove-Item -LiteralPath $updateManifestPath -Force
}
if (Test-Path -LiteralPath $updateManifestChecksumPath) {
    Remove-Item -LiteralPath $updateManifestChecksumPath -Force
}

Push-Location $projectRoot
try {
    & $dotnet publish 'src\Piko.Desktop\Piko.Desktop.csproj' `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $publishDirectory `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$version `
        -p:AssemblyVersion=$binaryVersion `
        -p:FileVersion=$binaryVersion `
        -p:InformationalVersion=$version `
        -p:IncludeSourceRevisionInInformationalVersion=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $dotnet publish 'src\Piko.Runtime\Piko.Runtime.csproj' `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $publishDirectory `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$version `
        -p:AssemblyVersion=$binaryVersion `
        -p:FileVersion=$binaryVersion `
        -p:InformationalVersion=$version `
        -p:IncludeSourceRevisionInInformationalVersion=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $extensionRoot = Join-Path $projectRoot 'integrations\vscode'
    $vsce = Join-Path $extensionRoot 'node_modules\.bin\vsce.cmd'
    if (-not (Test-Path -LiteralPath $vsce)) {
        throw 'VS Code extension dependencies are missing. Run npm ci in integrations/vscode.'
    }
    Push-Location $extensionRoot
    try {
        & $vsce package $version --no-dependencies --out $extensionPath
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    } finally {
        Pop-Location
    }
    Copy-Item -LiteralPath $extensionPath -Destination (Join-Path $publishDirectory (Split-Path -Leaf $extensionPath))

    Copy-Item -LiteralPath 'docs\USER_GUIDE_ZH.md' -Destination (Join-Path $publishDirectory '使用说明.md')
    Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $publishDirectory 'LICENSE.txt')

    $desktopExecutable = Join-Path $publishDirectory 'Piko.exe'
    $runtimeExecutable = Join-Path $publishDirectory 'Piko.Runtime.exe'
    $desktopFileVersion = (Get-Item -LiteralPath $desktopExecutable).VersionInfo.FileVersion
    $runtimeFileVersion = (Get-Item -LiteralPath $runtimeExecutable).VersionInfo.FileVersion
    if ($desktopFileVersion -ne $binaryVersion -or $runtimeFileVersion -ne $binaryVersion) {
        throw "Published file versions do not match $binaryVersion. Desktop=$desktopFileVersion Runtime=$runtimeFileVersion"
    }

    Invoke-PikoCodeSign -Files @($desktopExecutable, $runtimeExecutable)

    $desktopSmokeRoot = Join-Path $workspaceRoot 'work\release-desktop-smoke'
    $runtimeSmokeRoot = Join-Path $workspaceRoot 'work\release-runtime-smoke'
    New-Item -ItemType Directory -Force -Path $desktopSmokeRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $runtimeSmokeRoot | Out-Null

    & (Join-Path $publishDirectory 'Piko.exe') --smoke-test --data-dir $desktopSmokeRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & (Join-Path $publishDirectory 'Piko.Runtime.exe') --smoke-test --data-dir $runtimeSmokeRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archivePath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $checksumPath -Value "$hash  $(Split-Path -Leaf $archivePath)" -Encoding ascii
    $extensionHash = (Get-FileHash -LiteralPath $extensionPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $extensionChecksumPath -Value "$extensionHash  $(Split-Path -Leaf $extensionPath)" -Encoding ascii

    & $dotnet publish 'src\Piko.Setup\Piko.Setup.csproj' `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $setupPublishDirectory `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$version `
        -p:AssemblyVersion=$binaryVersion `
        -p:FileVersion=$binaryVersion `
        -p:InformationalVersion=$version `
        -p:IncludeSourceRevisionInInformationalVersion=false `
        "-p:PikoInstallerPayload=$archivePath"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Copy-Item -LiteralPath (Join-Path $setupPublishDirectory 'Piko.Setup.exe') -Destination $setupPath
    Invoke-PikoCodeSign -Files @($setupPath)
    & $setupPath --smoke-test --silent
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $setupFileVersion = (Get-Item -LiteralPath $setupPath).VersionInfo.FileVersion
    if ($setupFileVersion -ne $binaryVersion) {
        throw "Setup file version does not match $binaryVersion. Setup=$setupFileVersion"
    }
    $setupHash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $setupChecksumPath -Value "$setupHash  $(Split-Path -Leaf $setupPath)" -Encoding ascii

    $setupAssetName = Split-Path -Leaf $setupPath
    $manifest = [ordered]@{
        schemaVersion = 1
        version = $version
        channel = $releaseChannel
        publishedAt = [DateTimeOffset]::UtcNow.ToString('O')
        releasePage = "https://github.com/agentforgehu/piko-desktop-pet/releases/tag/v$version"
        installer = [ordered]@{
            url = "https://github.com/agentforgehu/piko-desktop-pet/releases/download/v$version/$setupAssetName"
            sha256 = $setupHash
            sizeBytes = (Get-Item -LiteralPath $setupPath).Length
            authenticodeRequired = $shouldSign
        }
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $updateManifestPath -Encoding utf8NoBOM
    $manifestHash = (Get-FileHash -LiteralPath $updateManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $updateManifestChecksumPath -Value "$manifestHash  $(Split-Path -Leaf $updateManifestPath)" -Encoding ascii

    Write-Output "Published: $publishDirectory"
    Write-Output "Archive:   $archivePath"
    Write-Output "SHA256:    $hash"
    Write-Output "VSIX:      $extensionPath"
    Write-Output "VSIX SHA:  $extensionHash"
    Write-Output "Setup:     $setupPath"
    Write-Output "Setup SHA: $setupHash"
    Write-Output "Manifest:  $updateManifestPath"
    Write-Output "Manifest SHA: $manifestHash"
} finally {
    Pop-Location
    if (Test-Path -LiteralPath $setupPublishDirectory) {
        Remove-Item -LiteralPath $setupPublishDirectory -Recurse -Force
    }
}
