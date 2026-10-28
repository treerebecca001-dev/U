param([string]$Build='desktop-0.2.0-r1')
$ErrorActionPreference='Stop'
$sceneWorkspace=Split-Path -Parent $PSScriptRoot
if($Build -notmatch '^desktop-[0-9a-z.-]+$'){throw 'Build must be a simple desktop version name.'}
$sceneOutput=Join-Path $sceneWorkspace ('artifacts/app/'+$Build)
if(Test-Path -LiteralPath $sceneOutput){throw 'Publish requires a new output directory; fixed artifacts are never overwritten.'}
[void][IO.Directory]::CreateDirectory($sceneOutput)
$sceneLog=Join-Path $sceneWorkspace ('artifacts/logs/'+$Build+'-export.log')
Push-Location $sceneWorkspace
try{
 & (Join-Path $PSScriptRoot 'godot.ps1') --headless --path 'src/SceneWorkbench.Desktop' --export-release 'Windows Desktop' (Join-Path $sceneOutput 'SceneWorkbench.exe') --quit --log-file $sceneLog
 if($LASTEXITCODE -ne 0 -or (Select-String -LiteralPath $sceneLog -Pattern '^ERROR:' -Quiet)){throw 'Godot export failed; see raw export log.'}
 $sceneData=Join-Path $sceneOutput 'data_SceneWorkbench.Desktop_windows_x86_64'
 foreach($sceneRequired in @('SceneWorkbench.Desktop.dll','SceneWorkbench.Core.dll','SceneWorkbench.Unity.dll','coreclr.dll','hostfxr.dll','SceneWorkbench.Desktop.runtimeconfig.json')){
  if(!(Test-Path -LiteralPath (Join-Path $sceneData $sceneRequired) -PathType Leaf)){throw ('Incomplete self-contained export: '+$sceneRequired)}
 }
 Copy-Item -LiteralPath (Join-Path $sceneWorkspace 'DemoProject') -Destination (Join-Path $sceneOutput 'DemoProject') -Recurse
 Copy-Item -LiteralPath (Join-Path $sceneWorkspace 'THIRD-PARTY-NOTICES.md') -Destination $sceneOutput
 Copy-Item -LiteralPath (Join-Path $sceneWorkspace 'licenses') -Destination (Join-Path $sceneOutput 'licenses') -Recurse
 $sceneFiles=@(Get-ChildItem -LiteralPath $sceneOutput -Recurse -File | Sort-Object FullName | ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($sceneOutput,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
 [ordered]@{schemaVersion=1;product='SceneWorkbench independent desktop preview';build=$Build;generatedUtc=[DateTimeOffset]::UtcNow.ToString('O');godotVersion='4.7.2-stable';runtime='self-contained .NET 8 Windows x64';unityRequired=$false;scope='Known Unity 2021.3 standalone GameObject/Transform scalar editing; native Unity acceptance separate';files=$sceneFiles} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $sceneOutput 'build-manifest.json') -Encoding utf8
 Write-Output ('Published independent desktop: '+$sceneOutput)
}finally{Pop-Location}
