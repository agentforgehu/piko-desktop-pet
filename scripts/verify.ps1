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
    & (Join-Path $PSScriptRoot 'check-version-sync.ps1')

    & $dotnet restore 'Piko.sln'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $dotnet build 'Piko.sln' -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $dotnet test 'Piko.sln' -c Release --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $npmCommand = Get-Command npm -ErrorAction SilentlyContinue
    if (-not $npmCommand) {
        throw 'Node.js/npm is required to verify the VS Code Context Bridge.'
    }

    Push-Location 'integrations\vscode'
    try {
        if (-not (Test-Path -LiteralPath 'node_modules')) {
            & $npmCommand.Source ci --ignore-scripts --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
        & $npmCommand.Source run check
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & $npmCommand.Source run compile
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    } finally {
        Pop-Location
    }

    $desktopSmokeRoot = Join-Path $workspaceRoot 'work\desktop-smoke'
    New-Item -ItemType Directory -Force -Path $desktopSmokeRoot | Out-Null
    & $dotnet 'src\Piko.Desktop\bin\Release\net8.0-windows\Piko.dll' --smoke-test --data-dir $desktopSmokeRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $runtimeSmokeRoot = Join-Path $workspaceRoot 'work\runtime-smoke'
    New-Item -ItemType Directory -Force -Path $runtimeSmokeRoot | Out-Null
    & $dotnet 'src\Piko.Runtime\bin\Release\net8.0-windows\Piko.Runtime.dll' --smoke-test --data-dir $runtimeSmokeRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $runtimeStatusFile = Join-Path $runtimeSmokeRoot 'runtime-status.json'
    if (-not (Test-Path -LiteralPath $runtimeStatusFile)) {
        throw 'Runtime smoke test did not create a status snapshot.'
    }

    $runtimeStatus = Get-Content -LiteralPath $runtimeStatusFile -Raw | ConvertFrom-Json
    if ($runtimeStatus.schemaVersion -ne 2 -or $runtimeStatus.health -ne 'healthy') {
        throw 'Runtime smoke status is not healthy or has an unsupported schema.'
    }

    exit 0
} finally {
    Pop-Location
}

