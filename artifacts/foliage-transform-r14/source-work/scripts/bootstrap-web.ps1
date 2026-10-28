$ErrorActionPreference='Stop'
$sceneWorkspace=Split-Path -Parent $PSScriptRoot
$sceneProject=Join-Path $sceneWorkspace 'src/SceneWorkbench.Web'
$sceneManifest=Get-Content -LiteralPath (Join-Path $sceneProject 'vendor-manifest.json') -Raw | ConvertFrom-Json
$sceneVendor=Join-Path $sceneProject 'wwwroot/vendor'
$sceneOrigins=@{'three.module.js'='build/three.module.js';'three.core.js'='build/three.core.js';'controls/OrbitControls.js'='examples/jsm/controls/OrbitControls.js';'controls/TransformControls.js'='examples/jsm/controls/TransformControls.js';'loaders/FBXLoader.js'='examples/jsm/loaders/FBXLoader.js';'loaders/TGALoader.js'='examples/jsm/loaders/TGALoader.js';'curves/NURBSCurve.js'='examples/jsm/curves/NURBSCurve.js';'curves/NURBSUtils.js'='examples/jsm/curves/NURBSUtils.js';'libs/fflate.module.js'='examples/jsm/libs/fflate.module.js';'LICENSE'='LICENSE'}
foreach($sceneRecord in $sceneManifest.files){
 if($sceneRecord.path -eq 'fflate-LICENSE.txt'){$sceneOrigins['fflate-LICENSE.txt']='https://raw.githubusercontent.com/101arrowz/fflate/v0.8.2/LICENSE'}
 if(!$sceneOrigins.ContainsKey($sceneRecord.path)){throw 'Unexpected vendor manifest path.'}
 $sceneTarget=Join-Path $sceneVendor $sceneRecord.path
 if((Test-Path -LiteralPath $sceneTarget) -and (Get-FileHash -LiteralPath $sceneTarget -Algorithm SHA256).Hash.ToLowerInvariant() -eq $sceneRecord.servedSha256){continue}
 [void][IO.Directory]::CreateDirectory((Split-Path -Parent $sceneTarget))
 $sceneOrigin=$sceneOrigins[$sceneRecord.path]
 $sceneUri=if($sceneOrigin.StartsWith('https://')){$sceneOrigin}else{'https://raw.githubusercontent.com/mrdoob/three.js/'+$sceneManifest.commit+'/'+$sceneOrigin}
 Invoke-WebRequest -Uri $sceneUri -OutFile $sceneTarget
 if((Get-FileHash -LiteralPath $sceneTarget -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sceneRecord.originalSha256){throw 'Official vendor content does not match the pinned hash.'}
 if($sceneRecord.path.StartsWith('controls/') -or $sceneRecord.path.StartsWith('loaders/') -or $sceneRecord.path.StartsWith('curves/')){
  $sceneText=[IO.File]::ReadAllText($sceneTarget).Replace("from 'three'","from '../three.module.js'")
  if($sceneRecord.path -eq 'loaders/FBXLoader.js'){
   $sceneNeedle="if ( 'GlobalSettings' in fbxTree ) {"
   $sceneText=$sceneText.Replace($sceneNeedle,$sceneNeedle+"`n            sceneGraph.userData.sourceAxes = Object.fromEntries( ['UpAxis','UpAxisSign','FrontAxis','FrontAxisSign','CoordAxis','CoordAxisSign'].map( key => [key, fbxTree.GlobalSettings[key]?.value] ) );")
  }
  [IO.File]::WriteAllText($sceneTarget,$sceneText,[Text.UTF8Encoding]::new($false))
 }
 if((Get-FileHash -LiteralPath $sceneTarget -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sceneRecord.servedSha256){throw 'Local vendor adapter does not match the pinned hash.'}
}
& (Join-Path $PSScriptRoot 'sync-web-theme.ps1')
& (Join-Path $PSScriptRoot 'dotnet.ps1') restore (Join-Path $sceneProject 'SceneWorkbench.Web.csproj') --configfile (Join-Path $sceneProject 'NuGet.Config') --locked-mode --disable-parallel '-m:1' '-nr:false'
if($LASTEXITCODE -ne 0){throw 'Web dependency restore failed.'}
Write-Output 'Local web dependencies verified. No Godot download or application packaging required.'
