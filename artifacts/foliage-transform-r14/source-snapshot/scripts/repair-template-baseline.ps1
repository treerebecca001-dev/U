param(
    [string]$Project = 'E:\2026場景編輯器\samples\Client231229',
    [string]$Evidence = 'E:\2026場景編輯器\artifacts\remediation\BL-001-010'
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$Project = [IO.Path]::GetFullPath($Project).TrimEnd('\')
$Evidence = [IO.Path]::GetFullPath($Evidence).TrimEnd('\')
if (!$Project.StartsWith($workspace + '\',[StringComparison]::OrdinalIgnoreCase) -or
    !$Evidence.StartsWith($workspace + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Repair paths must be inside this workspace.' }
function Reject-Links([string]$Path) {
    for($current=$Path;$current;$current=[IO.Path]::GetDirectoryName($current)) {
        if(Test-Path -LiteralPath $current) {
            if(([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Repair must not follow reparse points.' }
        }
    }
}
Reject-Links $Project
Reject-Links $Evidence
$markerPath=Join-Path $Project '.scene-workbench/workcopy.json'
$marker=Get-Content -LiteralPath $markerPath -Raw -Encoding utf8 | ConvertFrom-Json
if($marker.state -ne 'complete' -or [IO.Path]::GetFullPath($marker.workingRoot).TrimEnd('\') -ne $Project) { throw 'A complete, correctly located working copy is required.' }
$source=[IO.Path]::GetFullPath($marker.sourceRoot).TrimEnd('\')
if($source.StartsWith($Project+'\',[StringComparison]::OrdinalIgnoreCase) -or $Project.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase) -or $source -eq $Project) { throw 'Source and working roots must be disjoint.' }
$manifestPath=Join-Path $Project '.scene-workbench/copy-manifest.jsonl'
$initialManifestHash=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
if($initialManifestHash -ne $marker.manifestSha256) { throw 'Original copy manifest integrity mismatch.' }
$resultPath=Join-Path $Evidence 'repairs-applied.json'
if(Test-Path -LiteralPath $resultPath) { throw 'Repair evidence already exists; do not repeat or overwrite this operation.' }
$relativeMetas=@(
    'Library/PackageCache/com.jetbrains.riderflow@2022.1.5-248/hash.txt.meta',
    'Library/PackageCache/com.unity.burst@1.8.11/Unity.Burst.Unsafe.xml.meta'
)
$watched=@('ProjectSettings/ProjectVersion.txt','Packages/manifest.json','Packages/packages-lock.json') + $relativeMetas + @($relativeMetas | ForEach-Object {$_.Substring(0,$_.Length-5)})
$sourceBefore=@($watched | ForEach-Object {
    $path=Join-Path $source $_
    [ordered]@{path=$_;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
})
New-Item -ItemType Directory -Path $Evidence -Force | Out-Null
$sourceBefore | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Evidence 'source-protection-before.json') -Encoding utf8
$changes=@()
foreach($relative in $relativeMetas) {
    $path=Join-Path $Project $relative
    Reject-Links $path
    $old=[IO.File]::ReadAllBytes($path)
    $text=[Text.Encoding]::UTF8.GetString($old)
    $guid=[regex]::Match($text,'(?m)^guid: ([0-9a-f]{32})\s*$').Groups[1].Value
    if(!$guid -or $text -notmatch '(?m)^folderAsset: yes\r?$' -or !(Test-Path -LiteralPath $path.Substring(0,$path.Length-5) -PathType Leaf)) { throw ('Unexpected meta baseline: '+$relative) }
    $backup=Join-Path $Evidence ('backups/'+$relative)
    if(Test-Path -LiteralPath $backup) { throw 'Backup already exists.' }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null
    [IO.File]::WriteAllBytes($backup,$old)
    $updated=[regex]::Replace($text,'(?m)^folderAsset: yes\r?\n','')
    [IO.File]::WriteAllText($path,$updated,[Text.UTF8Encoding]::new($false))
    $newGuid=[regex]::Match([IO.File]::ReadAllText($path),'(?m)^guid: ([0-9a-f]{32})\s*$').Groups[1].Value
    if($newGuid -ne $guid) { throw 'Repair unexpectedly changed a GUID.' }
    $changes += [ordered]@{blocker=if($relative -like '*riderflow*'){'BL-009'}else{'BL-010'};path=$relative;operation='Remove erroneous folderAsset line from existing file meta';guid=$guid;beforeSha256=(Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash.ToLowerInvariant();afterSha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();backup=[IO.Path]::GetRelativePath($workspace,$backup).Replace('\','/')}
}
$repo=Join-Path $workspace '.tools/package-recovery/yooasset'
$commit='160717b6411986cba4c223fa6f7604b32e1e669e'
$actual=(& git -C $repo rev-parse FETCH_HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $actual -ne $commit) { throw 'Recovered YooAsset commit differs from lock.' }
$archive=Join-Path $workspace '.tools/package-recovery/yooasset-160717b641.tar'
$archiveProof=Get-Content -LiteralPath (Join-Path $Evidence 'yooasset-archive.json') -Raw | ConvertFrom-Json
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archiveProof.archiveSha256) { throw 'Recovered archive integrity mismatch.' }
$package=Join-Path $Project 'Packages/com.tuyoogame.yooasset'
if(Test-Path -LiteralPath $package) { throw 'Embedded package destination already exists.' }
[IO.Directory]::CreateDirectory($package) | Out-Null
& tar.exe -xf $archive -C $package
if($LASTEXITCODE -ne 0) { throw 'YooAsset extraction failed.' }
$metadata=Get-Content -LiteralPath (Join-Path $package 'package.json') -Raw | ConvertFrom-Json
if($metadata.name -ne 'com.tuyoogame.yooasset' -or $metadata.version -ne '1.4.5-preview') { throw 'Recovered package identity mismatch.' }
$added=@(Get-ChildItem -LiteralPath $package -File -Recurse | Sort-Object FullName | ForEach-Object {
    if(($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Unexpected package link.' }
    [ordered]@{path=[IO.Path]::GetRelativePath($Project,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$added | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Evidence 'yooasset-added-files.json') -Encoding utf8
foreach($entry in $sourceBefore) {
    if((Get-FileHash -LiteralPath (Join-Path $source $entry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'Original source changed during repair.' }
}
if((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $initialManifestHash) { throw 'Original copy manifest changed.' }
[ordered]@{schemaVersion=1;appliedUtc=[DateTimeOffset]::UtcNow.ToString('O');projectRoot=$Project;sourceRoot=$source;sourceWritesPerformed=$false;copyManifestPreserved=$true;copyManifestSha256=$initialManifestHash;changes=$changes;recoveredPackage=[ordered]@{blocker='BL-008';name=$metadata.name;version=$metadata.version;source='Exact locked Git commit, embedded without changing declared dependency';commit=$commit;gitTree=$archiveProof.gitTree;archiveSha256=$archiveProof.archiveSha256;addedFiles=$added.Count;dependencies=$metadata.dependencies};remaining='Unity 2022.3 actual load and editor write profile require validation'} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $resultPath -Encoding utf8
Write-Output ('Recorded repairs: '+$resultPath)
