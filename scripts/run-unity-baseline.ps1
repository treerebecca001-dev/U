param(
    [Parameter(Mandatory)][ValidateSet('license','baseline','roundtrip')][string]$Mode,
    [Parameter(Mandatory)][string]$RunName,
    [string]$Editor = 'E:\2026場景編輯器\.tools\unity-2022.3.13f1\installed\Editor\Unity.exe',
    [string]$Project = 'E:\2026場景編輯器\samples\Client231229'
)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$Project=[IO.Path]::GetFullPath($Project).TrimEnd('\')
if(!$Project.StartsWith($workspace+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unity acceptance must use a workspace copy.' }
if($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'RunName must be a simple unique name.' }
$evidence=Join-Path $workspace 'artifacts/remediation/BL-001-010'
$log=Join-Path $evidence ($RunName+'.unity.log')
$report=Join-Path $evidence ($RunName+'.json')
$runFile=Join-Path $evidence ($RunName+'.invocation.json')
if((Test-Path -LiteralPath $runFile) -or (Test-Path -LiteralPath $log)) { throw 'Run evidence already exists; choose a new name.' }
if(!(Test-Path -LiteralPath $Editor -PathType Leaf)) { throw 'Validated Unity Editor executable is missing.' }
if($Mode -ne 'license') {
    $marker=Get-Content -LiteralPath (Join-Path $Project '.scene-workbench/workcopy.json') -Raw | ConvertFrom-Json
    if($marker.state -ne 'complete' -or [IO.Path]::GetFullPath($marker.workingRoot).TrimEnd('\') -ne $Project) { throw 'Working-copy identity mismatch.' }
    if((Get-FileHash -LiteralPath (Join-Path $Project '.scene-workbench/copy-manifest.jsonl') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $marker.manifestSha256) { throw 'Original copy manifest was changed.' }
    if(!(Test-Path -LiteralPath (Join-Path $Project 'Assets/SceneWorkbenchValidation/Editor/BaselineProbe.cs') -PathType Leaf)) { throw 'Optional Unity-side QA helper is not staged. Standalone editing does not require this script or Unity.' }
}
$arguments=@('-batchmode','-quit','-logFile',$log,'-job-worker-count','8')
if($Mode -eq 'license') {
    if(Test-Path -LiteralPath $Project) { throw 'License probe requires a fresh empty project location.' }
    $arguments+=@('-nographics','-createProject',$Project)
} else {
    $arguments+=@('-projectPath',$Project,'-buildTarget','Win64','-accept-apiupdate','-force-d3d11',
        '-executeMethod','SceneWorkbench.Validation.BaselineProbe.Run','-sceneWorkbenchMode',$Mode,
        '-sceneWorkbenchReport',$report,'-sceneWorkbenchCatalog',(Join-Path $workspace 'artifacts/reports/template-baseline-r5/baseline.json'))
}
$start=[Diagnostics.ProcessStartInfo]::new()
$start.FileName=$Editor
$start.WorkingDirectory=if($Mode -eq 'license'){$workspace}else{$Project}
$start.UseShellExecute=$false
$start.CreateNoWindow=$true
$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
$start.RedirectStandardOutput=$true
$start.RedirectStandardError=$true
$start.StandardOutputEncoding=[Text.Encoding]::UTF8
$start.StandardErrorEncoding=[Text.Encoding]::UTF8
$start.Environment['UPM_CACHE_ROOT']=Join-Path $workspace '.tools/unity-upm-cache'
foreach($argument in $arguments){$start.ArgumentList.Add($argument)}
$started=[DateTimeOffset]::UtcNow
$editorHash=(Get-FileHash -LiteralPath $Editor -Algorithm SHA256).Hash.ToLowerInvariant()
$process=[Diagnostics.Process]::Start($start)
$identity=[ordered]@{schemaVersion=1;mode=$Mode;executable=$Editor;editorSha256=$editorHash;arguments=$arguments;processId=$process.Id;startedUtc=$started.ToString('O');projectRoot=$Project;normalStandaloneRuntime=$false;originalSourceWritesAuthorized=$false;status='running';nativeExitCode=$null;unityLog=$log;report=$report}
$identity | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $runFile -Encoding utf8
$stdout=$process.StandardOutput.ReadToEndAsync()
$stderr=$process.StandardError.ReadToEndAsync()
$process.WaitForExit()
$code=$process.ExitCode
$completed=[DateTimeOffset]::UtcNow
[IO.File]::WriteAllText((Join-Path $evidence ($RunName+'.stdout.log')),$stdout.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $evidence ($RunName+'.stderr.log')),$stderr.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
$identity.status='completed'
$identity.nativeExitCode=$code
$identity.completedUtc=$completed.ToString('O')
$identity.elapsedSeconds=[Math]::Round(($completed-$started).TotalSeconds,3)
$identity | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $runFile -Encoding utf8
[IO.File]::WriteAllText((Join-Path $evidence ($RunName+'.exit.txt')),$code.ToString())
$process.Dispose()
Write-Output ('Unity acceptance '+$RunName+' exit: '+$code)
exit $code
