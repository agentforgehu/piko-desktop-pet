$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$workspaceRoot = (Resolve-Path (Join-Path $projectRoot '..\..')).Path
$localDotnet = Join-Path $workspaceRoot 'work\.dotnet\dotnet.exe'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue

if ($dotnetCommand) {
    $dotnet = $dotnetCommand.Source
} elseif (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
} else {
    throw '.NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0'
}

$env:DOTNET_CLI_HOME = Join-Path $workspaceRoot 'work\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $workspaceRoot 'work\nuget-packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Push-Location $projectRoot
try {
    & $dotnet restore 'Piko.sln'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $dotnet build 'Piko.sln' -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $dotnet test 'Piko.sln' -c Release --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $dotnet 'src\Piko.Desktop\bin\Release\net8.0-windows\Piko.dll' --smoke-test
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
