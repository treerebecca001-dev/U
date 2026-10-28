param(
 [Parameter(Mandatory)][string]$Application,
 [Parameter(Mandatory)][string]$Output,
 [string]$Project,
 [string]$Scene='Assets/WorkbenchDemo.unity',
 [string]$Previous,
 [string]$Resolution,
 [switch]$ReopenOnly
)
$ErrorActionPreference='Stop'
$sceneWorkspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$Application=[IO.Path]::GetFullPath($Application)
$Output=[IO.Path]::GetFullPath($Output)
if(!$Application.StartsWith($sceneWorkspace+'\',[StringComparison]::OrdinalIgnoreCase) -or !$Output.StartsWith($sceneWorkspace+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'UI verification must use workspace artifacts.'}
if(Test-Path -LiteralPath $Output){throw 'QA output must be a new directory.'}
[void][IO.Directory]::CreateDirectory($Output)
$sceneAppRoot=Split-Path -Parent $Application
$sceneManifest=Join-Path $sceneAppRoot 'build-manifest.json'
if(!(Test-Path -LiteralPath $sceneManifest)){throw 'A fixed desktop build-manifest is required.'}
$sceneManifestHash=(Get-FileHash -LiteralPath $sceneManifest -Algorithm SHA256).Hash.ToLowerInvariant()
$sceneIdentity=Get-Content -LiteralPath $sceneManifest -Raw | ConvertFrom-Json
foreach($sceneEntry in $sceneIdentity.files | Where-Object {$_.path -eq 'SceneWorkbench.exe' -or $_.path -match 'SceneWorkbench\.(Desktop|Core|Unity)\.dll$'}){
 if((Get-FileHash -LiteralPath (Join-Path $sceneAppRoot $sceneEntry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sceneEntry.sha256){throw 'Fixed application identity mismatch.'}
}
function Run-DesktopPhase([string]$Phase,[string]$TargetProject,[string]$Prior){
 $phaseOutput=Join-Path $Output $Phase
 [void][IO.Directory]::CreateDirectory($phaseOutput)
 $start=[Diagnostics.ProcessStartInfo]::new()
 $start.FileName=$Application;$start.WorkingDirectory=$sceneAppRoot;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
 $start.Environment['PATH']=$env:SystemRoot+'\System32;'+$env:SystemRoot
 $start.Environment['DOTNET_ROOT']=Join-Path $phaseOutput 'no-installed-dotnet'
 $start.Environment['DOTNET_MULTILEVEL_LOOKUP']='0'
 [void]$start.Environment.Remove('UNITY_PATH');[void]$start.Environment.Remove('UNITY_HOME')
 $start.Environment['APPDATA']=Join-Path $phaseOutput 'Roaming';$start.Environment['LOCALAPPDATA']=Join-Path $phaseOutput 'Local'
 [void][IO.Directory]::CreateDirectory($start.Environment['APPDATA']);[void][IO.Directory]::CreateDirectory($start.Environment['LOCALAPPDATA'])
 $arguments=@('--log-file',(Join-Path $phaseOutput 'runtime.log'),'--','--desktop-qa-output',$phaseOutput,'--scene',$Scene)
 if($Resolution){if($Resolution -notmatch '^\d{3,5}x\d{3,5}$'){throw 'Resolution must be WIDTHxHEIGHT.'};$arguments=@('--resolution',$Resolution)+$arguments}
 if($TargetProject){$arguments+=@('--project',$TargetProject)}
 if($Prior){$arguments+=@('--desktop-qa-previous',$Prior)}
 foreach($argument in $arguments){$start.ArgumentList.Add($argument)}
 $started=[DateTimeOffset]::UtcNow
 $process=[Diagnostics.Process]::Start($start)
 $timedOut=!$process.WaitForExit(900000)
 if($timedOut){$process.Kill($true);$process.WaitForExit()}
 $code=$process.ExitCode
 [ordered]@{executable=$Application;arguments=$arguments;buildManifestSha256=$sceneManifestHash;startedUtc=$started.ToString('O');completedUtc=[DateTimeOffset]::UtcNow.ToString('O');nativeExitCode=$code;timedOut=$timedOut;pathRestrictedToWindows=$true;dotnetRootDeliberatelyUnavailable=$true;cleanMachineWithoutUnityTest=$false;interaction='Godot GUI input events in actual exported application; OS input automation not used'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $phaseOutput 'invocation.json') -Encoding utf8
 $process.Dispose()
 if($code -ne 0 -or $timedOut -or !(Test-Path -LiteralPath (Join-Path $phaseOutput 'report.json'))){throw ('Native UI phase failed: '+$Phase)}
 return (Join-Path $phaseOutput 'report.json')
}
$initialTime=$null
if($Project){
 $Project=[IO.Path]::GetFullPath($Project)
 if(!$Project.StartsWith($sceneWorkspace+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Never modify the original reference project during UI verification.'}
 $sceneTarget=[IO.Path]::GetFullPath((Join-Path $Project $Scene))
 if(!$sceneTarget.StartsWith($Project+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Scene escapes working project.'}
 $initialTime=(Get-Item -LiteralPath $sceneTarget).LastWriteTimeUtc
}
if($ReopenOnly){
 if(!$Previous -or !$Project){throw 'ReopenOnly requires explicit previous report and working project.'}
 $editReport=$Previous
}else{$editReport=Run-DesktopPhase 'edit' $Project ''}
$edit=Get-Content -LiteralPath $editReport -Raw | ConvertFrom-Json
$reopened=Run-DesktopPhase 'reopen' $edit.projectRoot $editReport
$readback=Get-Content -LiteralPath $reopened -Raw | ConvertFrom-Json
if(!$readback.restored -or $readback.phase -ne 'PASSED_REOPEN_AND_RESTORE'){throw 'Fresh process readback/restoration was not confirmed.'}
if($initialTime -and (Get-FileHash -LiteralPath $sceneTarget -Algorithm SHA256).Hash.ToLowerInvariant() -eq $readback.beforeSha256){[IO.File]::SetLastWriteTimeUtc($sceneTarget,$initialTime)}
[ordered]@{generatedUtc=[DateTimeOffset]::UtcNow.ToString('O');buildManifestSha256=$sceneManifestHash;editReport=$editReport;reopenReport=$reopened;sceneRestored=$readback.restored;sceneMetadataTimeRestored=($null -ne $initialTime);originalSourcePreserved=$true;unityRoundTripVerified=$false;cleanMachineWithoutUnityVerified=$false;scope='Actual exported desktop: select, type, save, close, new process reload, native GUI restore'} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'summary.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $Output 'summary.json')
