param(
    [string]$Project='E:\2026場景編輯器\samples\Client231229',
    [string]$Evidence='E:\2026場景編輯器\artifacts\remediation\BL-001-010',
    [string]$ResultDirectory
)
$ErrorActionPreference='Stop'
$Project=[IO.Path]::GetFullPath($Project).TrimEnd('\')
$Evidence=[IO.Path]::GetFullPath($Evidence).TrimEnd('\')
$workspace=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
if(!$Project.StartsWith($workspace+'\',[StringComparison]::OrdinalIgnoreCase) -or !$Evidence.StartsWith($workspace+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence and project must be in this workspace.'}
if(!$ResultDirectory){$ResultDirectory=$Evidence}
$ResultDirectory=[IO.Path]::GetFullPath($ResultDirectory).TrimEnd('\')
if(!$ResultDirectory.StartsWith($workspace+'\',[StringComparison]::OrdinalIgnoreCase) -or $ResultDirectory -eq $Project -or $ResultDirectory.StartsWith($Project+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Results must be in the workspace, outside the project.'}
foreach($path in @($Project,$Evidence,$ResultDirectory)){
    for($current=$path;$current;$current=[IO.Path]::GetDirectoryName($current)){
        if((Test-Path -LiteralPath $current) -and (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0)){throw 'Evidence must not follow reparse points.'}
    }
}
$marker=Get-Content -LiteralPath (Join-Path $Project '.scene-workbench/workcopy.json') -Raw | ConvertFrom-Json
$originalManifest=Join-Path $Project '.scene-workbench/copy-manifest.jsonl'
if((Get-FileHash -LiteralPath $originalManifest -Algorithm SHA256).Hash.ToLowerInvariant() -ne $marker.manifestSha256){throw 'Original copy manifest changed.'}
$output=Join-Path $ResultDirectory 'derived-post-repair-baseline.jsonl'
if(Test-Path -LiteralPath $output){throw 'Derived evidence must not overwrite an earlier result.'}
[void][IO.Directory]::CreateDirectory($ResultDirectory)
$expected=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$repairs=Get-Content -LiteralPath (Join-Path $Evidence 'repairs-applied.json') -Raw | ConvertFrom-Json
foreach($entry in $repairs.changes){[void]$expected.Add($entry.path)}
foreach($entry in (Get-Content -LiteralPath (Join-Path $Evidence 'yooasset-added-files.json') -Raw | ConvertFrom-Json)){[void]$expected.Add($entry.path)}
foreach($relative in @('Assets/SceneWorkbenchValidation/Editor/BaselineProbe.cs','Assets/SceneWorkbenchValidation/Editor/SceneWorkbench.Validation.asmdef')){[void]$expected.Add($relative)}
$files=New-Object 'Collections.Generic.Dictionary[string,IO.FileInfo]' ([StringComparer]::OrdinalIgnoreCase)
$pending=New-Object 'Collections.Generic.Stack[string]'
foreach($relative in @('Assets','Packages','ProjectSettings','Library/PackageCache')){$path=Join-Path $Project $relative;if(Test-Path -LiteralPath $path){$pending.Push($path)}}
while($pending.Count){
    foreach($entry in [IO.Directory]::EnumerateFileSystemEntries($pending.Pop())){
        $attributes=[IO.File]::GetAttributes($entry)
        if(($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Do not follow links while collecting evidence.'}
        if(($attributes -band [IO.FileAttributes]::Directory) -ne 0){$pending.Push($entry)}else{$files.Add([IO.Path]::GetRelativePath($Project,$entry).Replace('\','/'),[IO.FileInfo]::new($entry))}
    }
}
$changes=New-Object 'Collections.Generic.List[object]'
$sourceProtected=New-Object 'Collections.Generic.List[object]'
$reused=0
$hashed=0
$totalBytes=0L
$count=0
$writer=[IO.StreamWriter]::new($output,$false,[Text.UTF8Encoding]::new($false))
try {
    foreach($line in [IO.File]::ReadLines($originalManifest)){
        $record=$line | ConvertFrom-Json
        if(!$files.ContainsKey($record.path)){$changes.Add([ordered]@{path=$record.path;operation='missing';expected=$false});continue}
        $file=$files[$record.path]
        [void]$files.Remove($record.path)
        if($expected.Contains($record.path) -or $file.Length -ne $record.length -or $file.LastWriteTimeUtc.Ticks -ne $record.lastWriteUtcTicks){
            $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $hashed++
            if($hash -ne $record.sha256){
                $sourcePath=Join-Path $marker.sourceRoot $record.path
                $sourceHash=(Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
                $sourceProtected.Add([ordered]@{path=$record.path;sourceSha256=$sourceHash;originalBaselineSha256=$record.sha256;matchesOriginalBaseline=($sourceHash -eq $record.sha256)})
                $changes.Add([ordered]@{path=$record.path;operation='modified';expected=$expected.Contains($record.path);beforeSha256=$record.sha256;afterSha256=$hash})
            }
            $record.sha256=$hash
            $record.length=$file.Length
            $record.lastWriteUtcTicks=$file.LastWriteTimeUtc.Ticks
        }else{$reused++}
        $writer.WriteLine(($record | ConvertTo-Json -Compress))
        $count++
        $totalBytes+=$record.length
    }
    foreach($pair in ($files.GetEnumerator() | Sort-Object Key)){
        $hash=(Get-FileHash -LiteralPath $pair.Value.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $record=[ordered]@{path=$pair.Key;length=$pair.Value.Length;lastWriteUtcTicks=$pair.Value.LastWriteTimeUtc.Ticks;sha256=$hash}
        $writer.WriteLine(($record | ConvertTo-Json -Compress))
        $changes.Add([ordered]@{path=$pair.Key;operation='added';expected=$expected.Contains($pair.Key);afterSha256=$hash})
        $hashed++
        $count++
        $totalBytes+=$pair.Value.Length
    }
} finally {$writer.Dispose()}
$before=Get-Content -LiteralPath (Join-Path $Evidence 'source-protection-before.json') -Raw | ConvertFrom-Json
foreach($entry in $before){
    $hash=(Get-FileHash -LiteralPath (Join-Path $marker.sourceRoot $entry.path) -Algorithm SHA256).Hash.ToLowerInvariant()
    $sourceProtected.Add([ordered]@{path=$entry.path;sourceSha256=$hash;beforeSha256=$entry.sha256;matchesBeforeRepair=($hash -eq $entry.sha256)})
}
$unexpected=@($changes | Where-Object {!$_.expected})
$badSource=@($sourceProtected | Where-Object {($_.Contains('matchesOriginalBaseline') -and !$_.matchesOriginalBaseline) -or ($_.Contains('matchesBeforeRepair') -and !$_.matchesBeforeRepair)})
$changes | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultDirectory 'intentional-and-unexpected-changes.json') -Encoding utf8
$sourceProtected | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultDirectory 'source-protection-after.json') -Encoding utf8
[ordered]@{schemaVersion=1;generatedUtc=[DateTimeOffset]::UtcNow.ToString('O');method='Derived from r6 full verified baseline; unchanged size/time records reuse earlier SHA256, modified and added files hashed now. Not a new complete rehash.';files=$count;bytes=$totalBytes;reusedUnchangedRecords=$reused;hashedFiles=$hashed;modified=@($changes | Where-Object operation -eq 'modified').Count;added=@($changes | Where-Object operation -eq 'added').Count;unexpectedChanges=$unexpected.Count;sourceProtectionMismatches=$badSource.Count;originalCopyManifestPreserved=((Get-FileHash -LiteralPath $originalManifest -Algorithm SHA256).Hash.ToLowerInvariant() -eq $marker.manifestSha256);derivedManifestSha256=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultDirectory 'delta-summary.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $ResultDirectory 'delta-summary.json')
if($unexpected.Count -or $badSource.Count){exit 1}
