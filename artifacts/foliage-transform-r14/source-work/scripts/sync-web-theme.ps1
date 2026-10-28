$ErrorActionPreference='Stop'
$sceneWorkspace=Split-Path -Parent $PSScriptRoot
$sceneTheme=[IO.File]::ReadAllText((Join-Path $sceneWorkspace 'src/SceneWorkbench.Desktop/EditorTheme.cs'))
$sceneLines=[Collections.Generic.List[string]]::new()
$sceneLines.Add('/* Generated from EditorTheme.cs by scripts/sync-web-theme.ps1. */')
$sceneLines.Add(':root {')
foreach($sceneMatch in [regex]::Matches($sceneTheme,'(\w+)\s*=\s*new\("([0-9a-fA-F]{6})"\)')){
 $sceneLines.Add('  --color-'+$sceneMatch.Groups[1].Value.ToLowerInvariant()+': #'+$sceneMatch.Groups[2].Value.ToLowerInvariant()+';')
}
if($sceneLines.Count -ne 12){throw 'Palette declaration changed; review the web adapter.'}
$sceneLines.Add('}')
$sceneOutput=Join-Path $sceneWorkspace 'src/SceneWorkbench.Web/wwwroot/ui/tokens.css'
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $sceneOutput))
[IO.File]::WriteAllText($sceneOutput,($sceneLines -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
