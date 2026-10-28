param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$sceneWorkspace = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $sceneWorkspace ('artifacts/app/stage0-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
}
$sceneOutput = [IO.Path]::GetFullPath($OutputDirectory)
$sceneWorkspacePrefix = [IO.Path]::GetFullPath($sceneWorkspace).TrimEnd('\') + '\'
if (-not $sceneOutput.StartsWith($sceneWorkspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish output must stay inside this workspace.'
}
if (Test-Path -LiteralPath $sceneOutput) { throw 'Publish output already exists; choose a fresh directory.' }
Push-Location $sceneWorkspace
try {
    & (Join-Path $PSScriptRoot 'dotnet.ps1') publish 'src/SceneWorkbench.Cli/SceneWorkbench.Cli.csproj' --configuration Release --runtime win-x64 --self-contained true --output $sceneOutput --disable-build-servers --verbosity minimal '-m:1' '-nr:false' '-p:UseSharedCompilation=false'
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath (Join-Path $sceneWorkspace 'THIRD-PARTY-NOTICES.md') -Destination $sceneOutput
    Copy-Item -LiteralPath (Join-Path $sceneWorkspace 'licenses') -Destination $sceneOutput -Recurse
    $sceneNoticeDestination = Join-Path $sceneOutput 'licenses'
    foreach ($sceneNotice in @('LICENSE.txt','ThirdPartyNotices.txt')) {
        $sceneNoticeSource = Join-Path $sceneWorkspace ('.tools/dotnet/' + $sceneNotice)
        if (-not (Test-Path -LiteralPath $sceneNoticeSource)) { throw 'Run bootstrap to make the .NET dependency notices available.' }
        Copy-Item -LiteralPath $sceneNoticeSource -Destination (Join-Path $sceneNoticeDestination ('DotNet-' + $sceneNotice))
    }
    $sceneBuildEntries = Get-ChildItem -LiteralPath $sceneOutput -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($sceneOutput,$_.FullName).Replace('\','/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    [ordered]@{ schemaVersion = 1; product = 'SceneWorkbench stage 0/1 CLI'; generatedUtc = [DateTimeOffset]::UtcNow.ToString('O'); files = @($sceneBuildEntries) } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $sceneOutput 'build-manifest.json') -Encoding utf8
    Write-Output ('Published: ' + (Join-Path $sceneOutput 'SceneWorkbench.exe'))
} finally { Pop-Location }
