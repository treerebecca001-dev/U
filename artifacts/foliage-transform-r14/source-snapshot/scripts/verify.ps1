param([string]$CliExecutable)

$ErrorActionPreference = 'Stop'
$sceneWorkspace = Split-Path -Parent $PSScriptRoot
$sceneLogs = Join-Path $sceneWorkspace 'artifacts/logs'
$sceneResults = Join-Path $sceneWorkspace 'artifacts/test-results'
New-Item -ItemType Directory -Path $sceneLogs,$sceneResults -Force | Out-Null
$scenePreviousCli = $env:SCENE_WORKBENCH_CLI
if ($CliExecutable) { $env:SCENE_WORKBENCH_CLI = (Resolve-Path -LiteralPath $CliExecutable).Path }
Push-Location $sceneWorkspace
try {
    & (Join-Path $PSScriptRoot 'dotnet.ps1') build 'SceneWorkbench.slnx' --configuration Release --no-restore --disable-build-servers --verbosity minimal '-m:1' '-nr:false' '-p:UseSharedCompilation=false' 2>&1 |
        Tee-Object -FilePath (Join-Path $sceneLogs 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $sceneRunName = 'stage0-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '.trx'
    & (Join-Path $PSScriptRoot 'dotnet.ps1') test 'tests/SceneWorkbench.Tests/SceneWorkbench.Tests.csproj' --configuration Release --no-build --no-restore --verbosity minimal --logger ('trx;LogFileName=' + $sceneRunName) --results-directory $sceneResults 2>&1 |
        Tee-Object -FilePath (Join-Path $sceneLogs 'tests.log')
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed. See test results.' }
} finally {
    $env:SCENE_WORKBENCH_CLI = $scenePreviousCli
    Pop-Location
}
