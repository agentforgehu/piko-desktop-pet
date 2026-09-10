$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') {
    throw 'Installation lifecycle verification is restricted to a disposable GitHub Actions Windows user.'
}
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$version = (Get-Content (Join-Path $projectRoot 'release-version.txt') -Raw).Trim()
$setup = Join-Path $projectRoot "releases/Piko-$version-Setup.exe"
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs/PikoDesktopPet'
$userData = Join-Path $env:LOCALAPPDATA 'PikoDesktopPet'
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PikoDesktopPet'
if ((Test-Path $installRoot) -or (Test-Path $userData) -or (Test-Path $registryPath)) {
    throw 'Refusing to modify an existing Piko installation or data.'
}
Add-Type -Path (Join-Path $projectRoot 'src/Piko.Context/bin/Release/net8.0/Piko.Context.dll')
Add-Type -Path (Join-Path $projectRoot 'src/Piko.Runtime.Client/bin/Release/net8.0-windows/Piko.Runtime.Client.dll')
$credentials = [Piko.Runtime.Security.WindowsCredentialStore]::new()
$secretTargets = @([Piko.Runtime.Security.RuntimeSecretNames]::OpenAiApiKey, [Piko.Runtime.Security.RuntimeSecretNames]::MemoryEncryptionKey)
foreach ($target in $secretTargets) {
    if ($null -ne $credentials.Read($target)) { throw 'Refusing to modify existing Piko credentials.' }
}
$reportPath = Join-Path $projectRoot 'releases/installation-report.json'
$steps = [Collections.Generic.List[string]]::new()
$active = $null
function Invoke-Setup([string[]]$SetupArguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($setup)
    $info.UseShellExecute = $false
    foreach ($argument in $SetupArguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        if (-not $process.WaitForExit(60000)) { $process.Kill($true); throw 'Setup timed out' }
        if ($process.ExitCode -ne 0) { throw "Setup exited with $($process.ExitCode)" }
    } finally { $process.Dispose() }
}
try {
    Invoke-Setup @('--silent', '--no-launch')
    $desktop = Join-Path $installRoot 'app/Piko.exe'
    $runtime = Join-Path $installRoot 'app/Piko.Runtime.exe'
    if (-not (Test-Path $desktop) -or -not (Test-Path $runtime)) { throw 'Installed binaries are missing' }
    if ((Get-ItemProperty $registryPath).DisplayVersion -ne $version) { throw 'Installed registry version mismatch' }
    $steps.Add('fresh-install')
    New-Item -ItemType Directory -Force $userData | Out-Null
    $sentinel = Join-Path $userData 'retention-test.txt'
    Set-Content $sentinel 'retain-this-test-data'
    $active = Start-Process -FilePath $desktop -ArgumentList @('--stability-test', '--duration-seconds', '60') -PassThru
    Start-Sleep -Seconds 2
    Invoke-Setup @('--silent', '--no-launch')
    if (-not $active.WaitForExit(5000)) { throw 'Upgrade did not stop the managed Piko process' }
    if ((Get-Content $sentinel -Raw).Trim() -ne 'retain-this-test-data') { throw 'Upgrade changed user data' }
    $steps.Add('replace-running-installation-preserves-data')
    # Worker mode is synchronous and validates the same uninstall operation as the public launcher.
    Invoke-Setup @('--uninstall-worker', '--silent')
    if ((Test-Path $installRoot) -or (Test-Path $registryPath) -or -not (Test-Path $sentinel)) {
        throw 'Default uninstall must remove the application and retain user data'
    }
    $steps.Add('uninstall-preserves-data')
    Invoke-Setup @('--silent', '--no-launch')
    Invoke-Setup @('--uninstall-worker', '--silent', '--purge-data')
    if ((Test-Path $installRoot) -or (Test-Path $registryPath) -or (Test-Path $userData)) { throw 'Purge uninstall left managed data' }
    $steps.Add('reinstall-and-purge')
    # Regression: removing the data folder first must not leave Credential Manager entries behind.
    foreach ($target in $secretTargets) { $credentials.Save($target, 'piko-disposable-ci-sentinel') }
    Invoke-Setup @('--uninstall-worker', '--silent', '--purge-data')
    foreach ($target in $secretTargets) {
        if ($null -ne $credentials.Read($target)) { throw 'Purge left credentials when the data folder was absent' }
    }
    $steps.Add('purge-credentials-without-data-folder')
    [ordered]@{ passed = $true; version = $version; sourceCommit = $env:GITHUB_SHA; steps = $steps } |
        ConvertTo-Json -Depth 4 | Set-Content $reportPath -Encoding utf8
} catch {
    [ordered]@{ passed = $false; version = $version; steps = $steps; error = $_.Exception.Message } |
        ConvertTo-Json -Depth 4 | Set-Content $reportPath -Encoding utf8
    throw
} finally {
    foreach ($target in $secretTargets) { $credentials.Delete($target) }
    if ($null -ne $active) {
        try { if (-not $active.HasExited) { $active.Kill($true) } } finally { $active.Dispose() }
    }
}
