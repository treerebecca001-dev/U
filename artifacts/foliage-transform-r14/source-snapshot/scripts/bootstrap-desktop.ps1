param([switch]$EngineOnly)
$ErrorActionPreference='Stop'
$sceneWorkspace=Split-Path -Parent $PSScriptRoot
$sceneTools=Join-Path $sceneWorkspace '.tools/godot-4.7.2'
[void][IO.Directory]::CreateDirectory($sceneTools)
$sceneAssets=@(
 @{name='Godot_v4.7.2-stable_mono_win64.zip';sha256='a2a48473a7414c5f19fab690518caebb738c09ef9601f6bd2388676a7f53b3c0'},
 @{name='Godot_v4.7.2-stable_mono_export_templates.tpz';sha256='92f8681e349ef1f90891b792da95e3b2b0bd1ed610b78018c58feb2d87e15a9d'}
)
if($EngineOnly){$sceneAssets=@($sceneAssets[0])}
foreach($sceneAsset in $sceneAssets){
 $sceneArchive=Join-Path $sceneTools $sceneAsset.name
 if(!(Test-Path -LiteralPath $sceneArchive)){
  $sceneUrl='https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable/'+$sceneAsset.name
  & curl.exe --fail --location --retry 2 --silent --show-error --output $sceneArchive $sceneUrl
  if($LASTEXITCODE -ne 0){throw 'Official Godot download failed; partial file retained.'}
 }
 if((Get-FileHash -LiteralPath $sceneArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sceneAsset.sha256){throw ('Archive differs from pinned official SHA256: '+$sceneAsset.name)}
 if($sceneAsset.name.EndsWith('.zip')){
  $sceneEngineDirectory=Join-Path $sceneTools 'engine'
  if(!(Test-Path -LiteralPath $sceneEngineDirectory)){Expand-Archive -LiteralPath $sceneArchive -DestinationPath $sceneEngineDirectory}
 }else{
  $sceneTemplateDirectory=Join-Path $sceneTools 'templates'
  [void][IO.Directory]::CreateDirectory($sceneTemplateDirectory)
  $sceneZip=[IO.Compression.ZipFile]::OpenRead($sceneArchive)
  try{
   foreach($sceneEntry in $sceneZip.Entries | Where-Object {$_.FullName -match 'windows_(debug|release)_x86_64\.exe$|templates/version.txt$'}){
    $sceneTarget=Join-Path $sceneTemplateDirectory $sceneEntry.Name
    if(!(Test-Path -LiteralPath $sceneTarget)){[IO.Compression.ZipFileExtensions]::ExtractToFile($sceneEntry,$sceneTarget,$false)}
   }
  }finally{$sceneZip.Dispose()}
 }
}
# Native dotnet SDK discovery can lose non-ASCII paths on Windows. This toolchain-only
# junction is outside Unity projects; authoring source/link guards are not relaxed.
$sceneAliasRecord=Join-Path $sceneTools 'sdk-alias.json'
if(!(Test-Path -LiteralPath $sceneAliasRecord) -and $sceneWorkspace -match '[^\x00-\x7F]'){
 $sceneSuffix=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sceneWorkspace))).Substring(0,8)
 $sceneSdk=Join-Path $sceneWorkspace '.tools/dotnet'
 $sceneAlias=Join-Path ([IO.Path]::GetTempPath()) ('SceneWorkbench-'+$sceneSuffix+'-sdk')
 if(!(Test-Path -LiteralPath $sceneAlias)){New-Item -ItemType Junction -Path $sceneAlias -Target $sceneSdk | Out-Null}
 $sceneItem=Get-Item -LiteralPath $sceneAlias
 if($sceneItem.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath($sceneItem.Target).TrimEnd('\') -ne [IO.Path]::GetFullPath($sceneSdk).TrimEnd('\')){throw 'Refuse an SDK alias owned by another path.'}
 [ordered]@{sdkAlias=$sceneAlias;target=$sceneSdk;reason='ASCII path for native dotnet SDK discovery'} | ConvertTo-Json | Set-Content -LiteralPath $sceneAliasRecord -Encoding utf8
}
Write-Output 'Godot 4.7.2 .NET workspace dependencies are ready. No Unity installation is used.'
