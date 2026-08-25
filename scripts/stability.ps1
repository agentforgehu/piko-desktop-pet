param(
    [string]$Version = '0.2.2',
    [int]$DurationSeconds = 1800,
    [int]$SampleSeconds = 2,
    [int]$MaximumDesktopWorkingSetMb = 350,
    [int]$MaximumRuntimeWorkingSetMb = 250,
    [double]$MaximumDesktopCpuPercent = 20,
    [double]$MaximumRuntimeCpuPercent = 10
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid semantic version: $Version"
}
if ($DurationSeconds -lt 10 -or $DurationSeconds -gt 86400) {
    throw 'DurationSeconds must be between 10 and 86400.'
}
if ($SampleSeconds -lt 1 -or $SampleSeconds -gt 30) {
    throw 'SampleSeconds must be between 1 and 30.'
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$workspaceRoot = (Resolve-Path (Join-Path $projectRoot '..\..')).Path
$publishDirectory = Join-Path $projectRoot "releases\Piko-$Version-win-x64"
$desktopExecutable = Join-Path $publishDirectory 'Piko.exe'
$runtimeExecutable = Join-Path $publishDirectory 'Piko.Runtime.exe'
if (-not (Test-Path -LiteralPath $desktopExecutable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $runtimeExecutable -PathType Leaf)) {
    throw 'Published Piko executables were not found. Run scripts\publish.ps1 first.'
}

$testRoot = Join-Path $workspaceRoot "work\stability-$([Guid]::NewGuid().ToString('N'))"
$runtimeData = Join-Path $testRoot 'runtime'
$desktopData = Join-Path $testRoot 'desktop'
$pipeName = "PikoDesktopPet.Stability.$([Guid]::NewGuid().ToString('N'))"
$reportPath = Join-Path $projectRoot "releases\stability-report-$Version.json"
$reportChecksumPath = "$reportPath.sha256.txt"

function Start-HiddenProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $publishDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    return [Diagnostics.Process]::Start($startInfo)
}

function Get-NormalizedCpuPercent {
    param(
        [TimeSpan]$PreviousCpu,
        [TimeSpan]$CurrentCpu,
        [double]$ElapsedSeconds
    )

    if ($ElapsedSeconds -le 0) { return 0 }
    return (($CurrentCpu - $PreviousCpu).TotalSeconds / $ElapsedSeconds / [Environment]::ProcessorCount) * 100
}

New-Item -ItemType Directory -Force -Path $runtimeData,$desktopData | Out-Null
$runtime = $null
$desktop = $null
$samples = [Collections.Generic.List[object]]::new()
$startedAt = [DateTimeOffset]::UtcNow
try {
    $runtime = Start-HiddenProcess -FileName $runtimeExecutable -Arguments @(
        '--stability-test', '--duration-seconds', "$DurationSeconds",
        '--data-dir', $runtimeData,
        '--pipe-name', $pipeName)
    $desktop = Start-HiddenProcess -FileName $desktopExecutable -Arguments @(
        '--stability-test', '--duration-seconds', "$DurationSeconds",
        '--data-dir', $desktopData)

    $previousAt = [DateTimeOffset]::UtcNow
    $previousRuntimeCpu = [TimeSpan]::Zero
    $previousDesktopCpu = [TimeSpan]::Zero
    $deadline = $startedAt.AddSeconds($DurationSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds $SampleSeconds
        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            break
        }
        $runtime.Refresh()
        $desktop.Refresh()
        if ($runtime.HasExited -or $desktop.HasExited) {
            $runtimeState = if ($runtime.HasExited) { "exit $($runtime.ExitCode)" } else { 'running' }
            $desktopState = if ($desktop.HasExited) { "exit $($desktop.ExitCode)" } else { 'running' }
            throw "A Piko stability process exited early. Runtime=$runtimeState Desktop=$desktopState"
        }

        $now = [DateTimeOffset]::UtcNow
        $elapsed = ($now - $previousAt).TotalSeconds
        $runtimeCpu = $runtime.TotalProcessorTime
        $desktopCpu = $desktop.TotalProcessorTime
        $statusPath = Join-Path $runtimeData 'runtime-status.json'
        $heartbeatAge = $null
        $runtimeHealthy = $false
        if (Test-Path -LiteralPath $statusPath) {
            $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
            $heartbeatAge = ($now - [DateTimeOffset]::Parse($status.lastHeartbeatAt)).TotalSeconds
            $runtimeHealthy = $status.schemaVersion -eq 2 -and
                $status.health -eq 'healthy' -and
                $heartbeatAge -lt 4
        }

        $samples.Add([pscustomobject][ordered]@{
            at = $now.ToString('O')
            runtimeWorkingSetMb = [Math]::Round($runtime.WorkingSet64 / 1MB, 2)
            desktopWorkingSetMb = [Math]::Round($desktop.WorkingSet64 / 1MB, 2)
            runtimeCpuPercent = [Math]::Round((Get-NormalizedCpuPercent $previousRuntimeCpu $runtimeCpu $elapsed), 3)
            desktopCpuPercent = [Math]::Round((Get-NormalizedCpuPercent $previousDesktopCpu $desktopCpu $elapsed), 3)
            runtimeHandles = $runtime.HandleCount
            desktopHandles = $desktop.HandleCount
            runtimeHealthy = $runtimeHealthy
            heartbeatAgeSeconds = if ($null -eq $heartbeatAge) { $null } else { [Math]::Round($heartbeatAge, 3) }
        })

        $previousAt = $now
        $previousRuntimeCpu = $runtimeCpu
        $previousDesktopCpu = $desktopCpu
    }

    if (-not $runtime.WaitForExit(15000) -or -not $desktop.WaitForExit(15000)) {
        throw 'A Piko stability process did not exit cleanly after its deadline.'
    }
    if ($runtime.ExitCode -ne 0 -or $desktop.ExitCode -ne 0) {
        throw "A Piko stability process returned an error. Runtime=$($runtime.ExitCode) Desktop=$($desktop.ExitCode)"
    }

    $warmSamples = @($samples | Select-Object -Skip ([Math]::Min(2, $samples.Count)))
    if ($warmSamples.Count -eq 0) { $warmSamples = @($samples) }
    $summary = [ordered]@{
        schemaVersion = 1
        version = $Version
        startedAt = $startedAt.ToString('O')
        durationSeconds = $DurationSeconds
        sampleCount = $samples.Count
        maximumRuntimeWorkingSetMb = ($warmSamples | Measure-Object runtimeWorkingSetMb -Maximum).Maximum
        maximumDesktopWorkingSetMb = ($warmSamples | Measure-Object desktopWorkingSetMb -Maximum).Maximum
        averageRuntimeCpuPercent = [Math]::Round(($warmSamples | Measure-Object runtimeCpuPercent -Average).Average, 3)
        averageDesktopCpuPercent = [Math]::Round(($warmSamples | Measure-Object desktopCpuPercent -Average).Average, 3)
        maximumRuntimeHandles = ($warmSamples | Measure-Object runtimeHandles -Maximum).Maximum
        maximumDesktopHandles = ($warmSamples | Measure-Object desktopHandles -Maximum).Maximum
        allRuntimeHeartbeatsHealthy = @($warmSamples | Where-Object { -not $_.runtimeHealthy }).Count -eq 0
    }
    $passed = $summary.maximumRuntimeWorkingSetMb -le $MaximumRuntimeWorkingSetMb -and
        $summary.maximumDesktopWorkingSetMb -le $MaximumDesktopWorkingSetMb -and
        $summary.averageRuntimeCpuPercent -le $MaximumRuntimeCpuPercent -and
        $summary.averageDesktopCpuPercent -le $MaximumDesktopCpuPercent -and
        $summary.maximumRuntimeHandles -lt 2000 -and
        $summary.maximumDesktopHandles -lt 2000 -and
        $summary.allRuntimeHeartbeatsHealthy
    $report = [ordered]@{
        passed = $passed
        budgets = [ordered]@{
            maximumDesktopWorkingSetMb = $MaximumDesktopWorkingSetMb
            maximumRuntimeWorkingSetMb = $MaximumRuntimeWorkingSetMb
            maximumDesktopCpuPercent = $MaximumDesktopCpuPercent
            maximumRuntimeCpuPercent = $MaximumRuntimeCpuPercent
            maximumHandles = 2000
        }
        summary = $summary
        samples = $samples
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    $reportHash = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $reportChecksumPath -Value "$reportHash  $(Split-Path -Leaf $reportPath)" -Encoding ascii
    if (-not $passed) {
        throw "Piko stability budgets failed. See $reportPath"
    }

    Write-Output "Stability PASS: $reportPath"
    $summary | ConvertTo-Json -Depth 3
} finally {
    foreach ($process in @($runtime, $desktop)) {
        if ($null -ne $process) {
            try {
                if (-not $process.HasExited) {
                    $process.Kill($true)
                    $process.WaitForExit(5000) | Out-Null
                }
            } catch {
                Write-Warning "Could not stop isolated stability process $($process.Id)."
            }
            $process.Dispose()
        }
    }
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
        $resolvedWorkRoot = [IO.Path]::GetFullPath((Join-Path $workspaceRoot 'work')).TrimEnd([IO.Path]::DirectorySeparatorChar)
        if (-not $resolvedTestRoot.StartsWith($resolvedWorkRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to clean a stability directory outside workspace work.'
        }
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
