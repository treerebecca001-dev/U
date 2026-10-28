param([ValidateRange(1024,65535)][int]$Port=5286,[switch]$NoBuild,[string]$SampleProject='')
$ErrorActionPreference='Stop'
$sceneWorkspace=Split-Path -Parent $PSScriptRoot
$env:SCENE_WORKBENCH_ROOT=$sceneWorkspace
$env:ASPNETCORE_ENVIRONMENT='Development'
$previousSampleProject=$env:SCENE_WORKBENCH_SAMPLE_PROJECT
if($SampleProject){$env:SCENE_WORKBENCH_SAMPLE_PROJECT=[IO.Path]::GetFullPath($SampleProject)}
Push-Location $sceneWorkspace
try {
 & (Join-Path $PSScriptRoot 'sync-web-theme.ps1')
 if(!(Test-Path -LiteralPath (Join-Path $sceneWorkspace 'src/SceneWorkbench.Web/wwwroot/vendor/three.module.js'))){throw 'Run scripts/bootstrap-web.ps1 once to fetch pinned local preview dependencies.'}
 Write-Output "Local web development: http://127.0.0.1:$Port/  (Ctrl+C to stop; no Unity and no packaging)"
 if($NoBuild){& (Join-Path $PSScriptRoot 'dotnet.ps1') run --project 'src/SceneWorkbench.Web' --no-build --no-restore --configuration Release --no-launch-profile -- --port $Port}
 else {
  if(!(Test-Path -LiteralPath (Join-Path $sceneWorkspace 'src/SceneWorkbench.Web/obj/project.assets.json'))){throw 'Run scripts/bootstrap-web.ps1 once before development. Normal startup does not access a package server.'}
  & (Join-Path $PSScriptRoot 'dotnet.ps1') build 'src/SceneWorkbench.Web/SceneWorkbench.Web.csproj' --no-restore --configuration Release --disable-build-servers '-m:1' '-nr:false' '-p:UseSharedCompilation=false'
  if($LASTEXITCODE -ne 0){throw 'Web development build failed.'}
  & (Join-Path $PSScriptRoot 'dotnet.ps1') run --project 'src/SceneWorkbench.Web' --no-build --no-restore --configuration Release --no-launch-profile -- --port $Port
 }
 exit $LASTEXITCODE
} finally {$env:SCENE_WORKBENCH_SAMPLE_PROJECT=$previousSampleProject;Pop-Location}
