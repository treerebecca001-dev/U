param([string]$Project,[string]$Scene='Assets/02.Arts/Scene/Room/Room_main.unity',[string]$Output,[ValidateRange(1024,65535)][int]$Port=5286)
$ErrorActionPreference='Stop'
$sceneWorkspace=Split-Path -Parent $PSScriptRoot
if(!$Project){$Project=Join-Path $sceneWorkspace 'samples/Client231229'}
if(!$Output){$Output=Join-Path $sceneWorkspace ('artifacts/web-qa/reference-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))}
$Project=[IO.Path]::GetFullPath($Project);$Output=[IO.Path]::GetFullPath($Output)
$sceneAllowedPrefix=[IO.Path]::GetFullPath($sceneWorkspace).TrimEnd('\')+'\'
foreach($sceneTarget in @($Project,$Output)){if(!$sceneTarget.StartsWith($sceneAllowedPrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Reference QA may only write inside the task workspace.'}}
if(Test-Path -LiteralPath $Output){throw 'Use a new evidence directory.'}
if($Scene -notmatch '^Assets/[^:]+\.unity$' -or $Scene.Split('/').Contains('..')){throw 'Unsafe scene path.'}
$scenePath=Join-Path $Project $Scene
$sceneInfo=Get-Content -LiteralPath (Join-Path $Project '.scene-workbench/workcopy.json') -Raw | ConvertFrom-Json
$sceneSourcePath=Join-Path $sceneInfo.sourceRoot $Scene
$sceneBefore=[IO.File]::ReadAllBytes($scenePath);$sceneBeforeHash=(Get-FileHash -LiteralPath $scenePath -Algorithm SHA256).Hash.ToLowerInvariant()
$sceneBeforeTime=[IO.File]::GetLastWriteTimeUtc($scenePath)
$sceneMetaHash=(Get-FileHash -LiteralPath ($scenePath+'.meta') -Algorithm SHA256).Hash.ToLowerInvariant()
$sceneSourceHash=(Get-FileHash -LiteralPath $sceneSourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
[void][IO.Directory]::CreateDirectory($Output);[IO.File]::WriteAllBytes((Join-Path $Output 'before.unity'),$sceneBefore)
function New-WebSession([int]$ServicePort){
 $base='http://127.0.0.1:'+ $ServicePort
 $html=(Invoke-WebRequest -Uri ($base+'/')).Content
 $token=[regex]::Match($html,'name="workbench-token" content="([a-f0-9]+)"').Groups[1].Value
 if($token.Length -ne 64){throw 'Missing local API token'}
 return @{base=$base;headers=@{'X-Workbench-Token'=$token}}
}
function Invoke-Api($Session,[string]$Path,$Body=$null){
 if($null -eq $Body){return Invoke-RestMethod -Uri ($Session.base+$Path) -Headers $Session.headers}
 return Invoke-RestMethod -Uri ($Session.base+$Path) -Headers $Session.headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($Body|ConvertTo-Json -Depth 8 -Compress))) -TimeoutSec 300
}
function Scene-Documents([byte[]]$Bytes){
 $text=[Text.Encoding]::UTF8.GetString($Bytes);$headers=[regex]::Matches($text,'(?m)^--- !u!\d+ &(-?\d+)[^\r\n]*\r?\n')
 $result=@{};for($i=0;$i -lt $headers.Count;$i++){$end=if($i+1 -lt $headers.Count){$headers[$i+1].Index}else{$text.Length};$result[$headers[$i].Groups[1].Value]=$text.Substring($headers[$i].Index,$end-$headers[$i].Index)}
 return $result
}
$sceneSession=New-WebSession $Port
$sceneState=Invoke-Api $sceneSession '/api/state'
if($sceneState.dirty){throw 'Current developer session has unsaved changes. QA does not discard them.'}
$sceneState=Invoke-Api $sceneSession '/api/project/open' @{revision=$sceneState.revision;path=$Project}
$sceneState=Invoke-Api $sceneSession '/api/scene/open' @{revision=$sceneState.revision;path=$Scene}
$sceneItem=@($sceneState.items | Where-Object {$_.editable -and $_.editableFields.Contains('position')}) | Select-Object -First 1
if(!$sceneItem){throw 'No verified standalone Transform in reference scene.'}
$sceneExpected=@(([double]$sceneItem.position[0]+0.125),([double]$sceneItem.position[1]),([double]$sceneItem.position[2]))
$sceneProcess=$null;$sceneOutTask=$null;$sceneErrTask=$null;$sceneChanged=$false
try{
 $sceneState=Invoke-Api $sceneSession '/api/scene/edit' @{revision=$sceneState.revision;id=[string]$sceneItem.id;field='position';value=$sceneExpected};$sceneChanged=$true
 $sceneState=Invoke-Api $sceneSession '/api/scene/save' @{revision=$sceneState.revision}
 $sceneSavedHash=(Get-FileHash -LiteralPath $scenePath -Algorithm SHA256).Hash.ToLowerInvariant()
 if($sceneSavedHash -eq $sceneBeforeHash -or $sceneState.dirty){throw 'Native save did not persist the edit.'}
 $sceneOldDocs=Scene-Documents $sceneBefore;$sceneNewDocs=Scene-Documents ([IO.File]::ReadAllBytes($scenePath))
 if($sceneOldDocs.Count -ne $sceneNewDocs.Count){throw 'Unity document count changed.'}
 foreach($sceneId in $sceneOldDocs.Keys){if($sceneId -ne [string]$sceneItem.id -and $sceneOldDocs[$sceneId] -cne $sceneNewDocs[$sceneId]){throw ('Unedited Unity document changed: '+$sceneId)}}
 $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$freshPort=$listener.LocalEndpoint.Port;$listener.Stop()
 $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $sceneWorkspace '.tools/dotnet/dotnet.exe'))
 $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.WorkingDirectory=$sceneWorkspace
 foreach($arg in @((Join-Path $sceneWorkspace 'src/SceneWorkbench.Web/bin/Release/net10.0/SceneWorkbench.Web.dll'),'--port',[string]$freshPort)){$start.ArgumentList.Add($arg)}
 $start.Environment['SCENE_WORKBENCH_ROOT']=$sceneWorkspace
 $sceneProcess=[Diagnostics.Process]::Start($start);$sceneOutTask=$sceneProcess.StandardOutput.ReadToEndAsync();$sceneErrTask=$sceneProcess.StandardError.ReadToEndAsync()
 $fresh=$null;for($attempt=0;$attempt -lt 100;$attempt++){if($sceneProcess.HasExited){throw 'Fresh QA service exited.'};try{$fresh=New-WebSession $freshPort;break}catch{Start-Sleep -Milliseconds 100}}
 if(!$fresh){throw 'Fresh QA service did not start.'}
 $reopened=Invoke-Api $fresh '/api/project/open' @{revision=0;path=$Project}
 $reopened=Invoke-Api $fresh '/api/scene/open' @{revision=$reopened.revision;path=$Scene}
 $reopenedItem=@($reopened.items | Where-Object {$_.id -eq $sceneItem.id})[0]
 for($i=0;$i -lt 3;$i++){if([double]$reopenedItem.position[$i] -ne $sceneExpected[$i]){throw 'Fresh process did not read the saved position.'}}
 try{$sceneModules=@($sceneProcess.Modules | ForEach-Object {$_.ModuleName} | Sort-Object -Unique)}catch{$sceneModules=@('UNAVAILABLE: module inventory access denied')}
 if(@($sceneModules | Where-Object {$_ -match 'UnityEditor|UnityEngine'}).Count){throw 'Unexpected Unity module loaded.'}
 $sceneState=Invoke-Api $sceneSession '/api/scene/undo' @{revision=$sceneState.revision}
 $sceneState=Invoke-Api $sceneSession '/api/scene/save' @{revision=$sceneState.revision}
 if((Get-FileHash -LiteralPath $scenePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sceneBeforeHash){throw 'Undo/save did not restore exact original scene bytes.'}
 $sceneChanged=$false
 [IO.File]::SetLastWriteTimeUtc($scenePath,$sceneBeforeTime)
 if((Get-FileHash -LiteralPath ($scenePath+'.meta') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sceneMetaHash){throw '.meta changed.'}
 if((Get-FileHash -LiteralPath $sceneSourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sceneSourceHash){throw 'Original reference scene changed.'}
 [ordered]@{status='PASS_API_ONLY';project=$Project;scene=$Scene;id=[string]$sceneItem.id;beforeSha256=$sceneBeforeHash;savedSha256=$sceneSavedHash;restoredSha256=$sceneBeforeHash;metaSha256=$sceneMetaHash;originalSourceSha256=$sceneSourceHash;expectedPosition=$sceneExpected;freshProcessPosition=$reopenedItem.position;untouchedDocuments=($sceneOldDocs.Count-1);runtimeDiagnostics=$sceneState.diagnostics;freshProcessModules=$sceneModules;sceneWriteTimeRestored=$true;browserVerified=$false;note='Actual reference native save/reopen through local HTTP API. No browser UI verification and no Unity native loading claim.'} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Output 'report.json') -Encoding utf8
 Write-Output ('Reference native save/reopen passed via API: '+$Output)
}finally{
 if($sceneProcess){if(!$sceneProcess.HasExited){$sceneProcess.Kill($true);$sceneProcess.WaitForExit()};[IO.File]::WriteAllText((Join-Path $Output 'fresh-service.log'),$sceneOutTask.GetAwaiter().GetResult()+$sceneErrTask.GetAwaiter().GetResult());$sceneProcess.Dispose()}
 if($sceneChanged){[ordered]@{status='INCOMPLETE';note='QA left a document edit pending or saved; before.unity retained. Restore through Undo/save or inspect transaction backup. Never write the D: source.';project=$Project;scene=$Scene} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'incomplete.json') -Encoding utf8}
}
