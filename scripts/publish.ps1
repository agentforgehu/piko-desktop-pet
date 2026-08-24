$ErrorActionPreference = 'Stop'

$version = '0.1.0'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$workspaceRoot = (Resolve-Path (Join-Path $projectRoot '..\..')).Path
$releaseRoot = Join-Path $projectRoot 'releases'
$publishDirectory = Join-Path $releaseRoot "Piko-$version-win-x64"
$archivePath = Join-Path $releaseRoot "Piko-$version-win-x64.zip"
$checksumPath = "$archivePath.sha256.txt"
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
if (!$resolvedPublishDirectory.StartsWith($resolvedProjectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    !$resolvedArchivePath.StartsWith($resolvedProjectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to publish outside the Piko project directory.'
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
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Copy-Item -LiteralPath 'docs\USER_GUIDE_ZH.md' -Destination (Join-Path $publishDirectory '使用说明.md')
    Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $publishDirectory 'LICENSE.txt')

    & (Join-Path $publishDirectory 'Piko.exe') --smoke-test
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archivePath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $checksumPath -Value "$hash  $(Split-Path -Leaf $archivePath)" -Encoding ascii

    Write-Output "Published: $publishDirectory"
    Write-Output "Archive:   $archivePath"
    Write-Output "SHA256:    $hash"
} finally {
    Pop-Location
}
