param([string]$Destination)
$ErrorActionPreference='Stop'
$sceneWorkspace=Split-Path -Parent $PSScriptRoot
if(!$Destination){$Destination=Join-Path $sceneWorkspace 'DemoProject'}
$Destination=[IO.Path]::GetFullPath($Destination)
if(!$Destination.StartsWith([IO.Path]::GetFullPath($sceneWorkspace).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Demo must be created inside this workspace.'}
if(Test-Path -LiteralPath $Destination){throw 'Do not overwrite an existing demo.'}
foreach($folder in @('Assets','Packages','ProjectSettings')){[void][IO.Directory]::CreateDirectory((Join-Path $Destination $folder))}
function Write-Demo([string]$Relative,[string]$Text){[IO.File]::WriteAllText((Join-Path $Destination $Relative),$Text,[Text.UTF8Encoding]::new($false))}
Write-Demo 'ProjectSettings/ProjectVersion.txt' "m_EditorVersion: 2021.3.15f1`n"
Write-Demo 'Packages/manifest.json' "{`"dependencies`":{}}`n"
Write-Demo 'Packages/packages-lock.json' "{`"dependencies`":{}}`n"
$yaml=[Text.StringBuilder]::new()
[void]$yaml.AppendLine('%YAML 1.1')
[void]$yaml.AppendLine('%TAG !u! tag:unity3d.com,2011:')
$objects=@(
 @{id=100;name='地板';x=0;y=-0.15;z=0;sx=14;sy=0.2;sz=14},
 @{id=200;name='中心方塊';x=0;y=0.5;z=0;sx=1;sy=1;sz=1},
 @{id=300;name='可移動方塊';x=2;y=0.5;z=1;sx=1;sy=1;sz=1},
 @{id=400;name='參考方塊';x=-2;y=0.75;z=-1;sx=1.5;sy=1.5;sz=1.5}
)
foreach($o in $objects){
 $go=$o.id;$tr=$go+1;$filter=$go+2;$renderer=$go+3;$collider=$go+4
 $name=ConvertTo-Json -InputObject $o.name -Compress
 [void]$yaml.AppendLine(@"
--- !u!1 &$go
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: $tr}
  - component: {fileID: $filter}
  - component: {fileID: $renderer}
  - component: {fileID: $collider}
  m_Layer: 0
  m_Name: $name
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &$tr
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: $go}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: $($o.x), y: $($o.y), z: $($o.z)}
  m_LocalScale: {x: $($o.sx), y: $($o.sy), z: $($o.sz)}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_RootOrder: 0
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!33 &$filter
MeshFilter:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: $go}
  m_Mesh: {fileID: 10202, guid: 0000000000000000e000000000000000, type: 0}
--- !u!23 &$renderer
MeshRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: $go}
  m_Enabled: 1
  m_CastShadows: 1
  m_ReceiveShadows: 1
  m_Materials: []
  m_SortingLayerID: 0
  m_SortingOrder: 0
--- !u!65 &$collider
BoxCollider:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: $go}
  m_Material: {fileID: 0}
  m_IsTrigger: 0
  m_Enabled: 1
  serializedVersion: 2
  m_Size: {x: 1, y: 1, z: 1}
  m_Center: {x: 0, y: 0, z: 0}
"@)
}
Write-Demo 'Assets/WorkbenchDemo.unity' $yaml.ToString()
Write-Demo 'Assets/WorkbenchDemo.unity.meta' "fileFormatVersion: 2`nguid: d38d638d2ad14bb78de31d56b77bca01`nDefaultImporter:`n  externalObjects: {}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
$layerLines=@('Default','TransparentFX','Ignore Raycast','','Water','UI')+@('')*26
$layerText="%YAML 1.1`n%TAG !u! tag:unity3d.com,2011:`n--- !u!78 &1`nTagManager:`n  serializedVersion: 2`n  tags: []`n  layers:`n"
foreach($name in $layerLines){$layerText+='  - '+$name+"`n"}
$layerText+="  m_SortingLayers:`n  - name: Default`n    uniqueID: 0`n    locked: 0`n"
Write-Demo 'ProjectSettings/TagManager.asset' $layerText
Write-Output ('Created native-format demo source: '+$Destination)
