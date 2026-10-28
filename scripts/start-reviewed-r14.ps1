param([ValidateRange(1024,65535)][int]$Port=5408)
$ErrorActionPreference='Stop'
$sceneWorkspace=Split-Path -Parent $PSScriptRoot
$sceneReviewRoot=Join-Path $sceneWorkspace 'artifacts/foliage-transform-r14'
$sceneSourceRoot=Join-Path $sceneReviewRoot 'source-snapshot'
$sceneRuntime=Join-Path $sceneReviewRoot 'runtime/SceneWorkbench.Web.dll'
$sceneDotnet=Join-Path $sceneWorkspace '.tools/dotnet/dotnet.exe'
foreach($sceneRequired in @($sceneDotnet,$sceneRuntime,(Join-Path $sceneSourceRoot 'SceneWorkbench.slnx'))){
 if(!(Test-Path -LiteralPath $sceneRequired)){throw "Missing local development input: $sceneRequired"}
}
$scenePreviousRoot=$env:SCENE_WORKBENCH_ROOT
$scenePreviousSample=$env:SCENE_WORKBENCH_SAMPLE_PROJECT
$scenePreviousDotnet=$env:DOTNET_ROOT
$scenePreviousEnvironment=$env:ASPNETCORE_ENVIRONMENT
try {
 $env:SCENE_WORKBENCH_ROOT=$sceneSourceRoot
 $env:SCENE_WORKBENCH_SAMPLE_PROJECT=''
 $env:DOTNET_ROOT=Join-Path $sceneWorkspace '.tools/dotnet'
 $env:ASPNETCORE_ENVIRONMENT='Development'
 Write-Output "R14 local development: http://127.0.0.1:$Port/ (Ctrl+C to stop; no Unity, build or packaging)"
 & $sceneDotnet $sceneRuntime --port $Port
 $sceneExitCode=$LASTEXITCODE
} finally {
 $env:SCENE_WORKBENCH_ROOT=$scenePreviousRoot
 $env:SCENE_WORKBENCH_SAMPLE_PROJECT=$scenePreviousSample
 $env:DOTNET_ROOT=$scenePreviousDotnet
 $env:ASPNETCORE_ENVIRONMENT=$scenePreviousEnvironment
}
exit $sceneExitCode
