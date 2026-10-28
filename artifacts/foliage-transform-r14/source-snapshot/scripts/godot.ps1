$ErrorActionPreference='Stop'
$sceneWorkspace=Split-Path -Parent $PSScriptRoot
$sceneGodotRoot=Join-Path $sceneWorkspace '.tools/godot-4.7.2/engine/Godot_v4.7.2-stable_mono_win64'
$sceneEngine=Join-Path $sceneGodotRoot 'Godot_v4.7.2-stable_mono_win64.exe'
if(!(Test-Path -LiteralPath $sceneEngine -PathType Leaf)){throw 'Godot 4.7.2 .NET is missing from the workspace.'}
if(!(Test-Path -LiteralPath (Join-Path $sceneGodotRoot '_sc_'))){[IO.File]::WriteAllText((Join-Path $sceneGodotRoot '_sc_'),'')}
$env:DOTNET_ROOT=Join-Path $sceneWorkspace '.tools/dotnet'
$sceneAliasProof=Join-Path $sceneWorkspace '.tools/godot-4.7.2/sdk-alias.json'
if(Test-Path -LiteralPath $sceneAliasProof){
    $sceneAlias=Get-Content -LiteralPath $sceneAliasProof -Raw | ConvertFrom-Json
    $sceneAliasItem=Get-Item -LiteralPath $sceneAlias.sdkAlias -ErrorAction Stop
    if($sceneAliasItem.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath($sceneAliasItem.Target).TrimEnd('\') -ne [IO.Path]::GetFullPath((Join-Path $sceneWorkspace '.tools/dotnet')).TrimEnd('\')){throw 'SDK alias does not target the pinned workspace SDK.'}
    $env:DOTNET_ROOT=$sceneAlias.sdkAlias
}
$env:DOTNET_CLI_HOME=Join-Path $sceneWorkspace '.tools/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:PATH=$env:DOTNET_ROOT+';'+$env:PATH
$env:APPDATA=Join-Path $sceneWorkspace '.tools/godot-user/Roaming'
$env:LOCALAPPDATA=Join-Path $sceneWorkspace '.tools/godot-user/Local'
[void][IO.Directory]::CreateDirectory($env:APPDATA)
[void][IO.Directory]::CreateDirectory($env:LOCALAPPDATA)
$sceneArguments=[Collections.Generic.List[string]]::new()
$sceneHasSeparator=$false
foreach($sceneArgument in $args){
    if($sceneArgument -eq '--'){$sceneHasSeparator=$true}
    if(!$sceneHasSeparator -and $sceneArgument -in @('--project','--scene','--desktop-qa-output','--desktop-qa-previous')){$sceneArguments.Add('--');$sceneHasSeparator=$true}
    $sceneArguments.Add([string]$sceneArgument)
}
$sceneStart=[Diagnostics.ProcessStartInfo]::new()
$sceneStart.FileName=$sceneEngine
$sceneStart.WorkingDirectory=$sceneWorkspace
$sceneStart.UseShellExecute=$false
$sceneStart.CreateNoWindow=$true
foreach($sceneArgument in $sceneArguments){$sceneStart.ArgumentList.Add($sceneArgument)}
$sceneProcess=[Diagnostics.Process]::Start($sceneStart)
$sceneProcess.WaitForExit()
$sceneExit=$sceneProcess.ExitCode
$sceneProcess.Dispose()
exit $sceneExit
