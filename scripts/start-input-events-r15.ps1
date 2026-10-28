param([ValidateRange(1024,65535)][int]$Port=5450)
$ErrorActionPreference='Stop'
$inputQaWorkspace=Split-Path -Parent $PSScriptRoot
$inputQaSource=Join-Path $inputQaWorkspace 'artifacts/input-events-r15/fixture'
$inputQaRuntime=Join-Path $inputQaWorkspace 'artifacts/foliage-transform-r14/runtime/SceneWorkbench.Web.dll'
$inputQaDotnet=Join-Path $inputQaWorkspace '.tools/dotnet/dotnet.exe'
$inputQaPreviousRoot=$env:SCENE_WORKBENCH_ROOT
$inputQaPreviousSample=$env:SCENE_WORKBENCH_SAMPLE_PROJECT
$inputQaPreviousDotnet=$env:DOTNET_ROOT
$inputQaPreviousEnvironment=$env:ASPNETCORE_ENVIRONMENT
try {
 $env:SCENE_WORKBENCH_ROOT=$inputQaSource
 $env:SCENE_WORKBENCH_SAMPLE_PROJECT=''
 $env:DOTNET_ROOT=Join-Path $inputQaWorkspace '.tools/dotnet'
 $env:ASPNETCORE_ENVIRONMENT='Development'
 & $inputQaDotnet $inputQaRuntime --port $Port
 $inputQaExit=$LASTEXITCODE
} finally {
 $env:SCENE_WORKBENCH_ROOT=$inputQaPreviousRoot
 $env:SCENE_WORKBENCH_SAMPLE_PROJECT=$inputQaPreviousSample
 $env:DOTNET_ROOT=$inputQaPreviousDotnet
 $env:ASPNETCORE_ENVIRONMENT=$inputQaPreviousEnvironment
}
exit $inputQaExit
