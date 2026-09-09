$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$version = (Get-Content (Join-Path $root 'release-version.txt') -Raw).Trim()
$approval = Get-Content (Join-Path $root 'release-approval.json') -Raw | ConvertFrom-Json
if ($approval.schemaVersion -ne 1 -or $approval.version -ne $version) { throw 'Release approval version mismatch' }
foreach ($gate in @('publisherIdentityConfirmed', 'visualAndInteractionAccepted', 'realModelAcceptanceCompleted', 'referenceMachineSoakAccepted')) {
    if ($approval.$gate -isnot [bool] -or $approval.$gate -ne $true) { throw "Release acceptance is incomplete: $gate" }
}
if ($approval.reviewedCommit -notmatch '^[0-9a-f]{40}$' -or [string]::IsNullOrWhiteSpace($approval.evidence)) {
    throw 'Release approval requires a reviewed commit and acceptance evidence'
}
Push-Location $root
try {
    git cat-file -e "$($approval.reviewedCommit)^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'Reviewed commit does not exist' }
    git diff --quiet $approval.reviewedCommit HEAD -- src scripts integrations Directory.Build.props release-version.txt .github/workflows
    if ($LASTEXITCODE -ne 0) { throw 'Product or release code changed after acceptance' }
} finally { Pop-Location }
Write-Host "Release acceptance recorded for $version"
