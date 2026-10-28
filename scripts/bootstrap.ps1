param([switch]$Restore)

$ErrorActionPreference = 'Stop'
$sceneWorkspace = Split-Path -Parent $PSScriptRoot
$sceneTools = Join-Path $sceneWorkspace '.tools'
$sceneSdkDirectory = Join-Path $sceneTools 'dotnet'
$sceneDotnet = Join-Path $sceneSdkDirectory 'dotnet.exe'
$scenePinnedVersion = (Get-Content -Raw -LiteralPath (Join-Path $sceneWorkspace 'global.json') | ConvertFrom-Json).sdk.version
New-Item -ItemType Directory -Path $sceneTools -Force | Out-Null

if (Test-Path -LiteralPath $sceneDotnet) {
    $sceneInstalledVersion = & $sceneDotnet --version
    if ($sceneInstalledVersion.Trim() -ne $scenePinnedVersion) {
        throw 'Workspace SDK version differs from global.json; existing tools were not overwritten.'
    }
} else {
    if (Test-Path -LiteralPath $sceneSdkDirectory) {
        throw 'An incomplete workspace SDK directory exists. Preserve it for diagnosis before retrying.'
    }
    $sceneChannel = ($scenePinnedVersion -split '\.')[0] + '.0'
    $sceneMetadataUrl = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/' + $sceneChannel + '/releases.json'
    $sceneMetadata = Invoke-RestMethod -Uri $sceneMetadataUrl -TimeoutSec 60
    $sceneMatchingSdks = foreach ($sceneRelease in $sceneMetadata.releases) {
        if ($sceneRelease.sdk.version -eq $scenePinnedVersion) { $sceneRelease.sdk }
        foreach ($sceneSdk in $sceneRelease.sdks) {
            if ($sceneSdk.version -eq $scenePinnedVersion) { $sceneSdk }
        }
    }
    $sceneArchive = $sceneMatchingSdks.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name.EndsWith('.zip') } | Select-Object -First 1
    if ($null -eq $sceneArchive -or -not $sceneArchive.url.StartsWith('https://builds.dotnet.microsoft.com/')) {
        throw 'Pinned SDK archive not found in official Microsoft metadata.'
    }
    $sceneArchivePath = Join-Path $sceneTools 'dotnet-sdk-win-x64.zip'
    if (-not (Test-Path -LiteralPath $sceneArchivePath) -or (Get-FileHash -LiteralPath $sceneArchivePath -Algorithm SHA512).Hash -ine $sceneArchive.hash) {
        Invoke-WebRequest -Uri $sceneArchive.url -OutFile $sceneArchivePath -TimeoutSec 600
    }
    $sceneActualHash = (Get-FileHash -LiteralPath $sceneArchivePath -Algorithm SHA512).Hash
    if ($sceneActualHash -ine $sceneArchive.hash) { throw 'SDK SHA-512 does not match official metadata.' }
    Expand-Archive -LiteralPath $sceneArchivePath -DestinationPath $sceneSdkDirectory
    [ordered]@{ version = $scenePinnedVersion; url = $sceneArchive.url; sha512 = $sceneActualHash.ToLowerInvariant(); verifiedUtc = [DateTimeOffset]::UtcNow.ToString('O') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sceneTools 'sdk-provenance.json') -Encoding utf8
}

if ($Restore) {
    Push-Location $sceneWorkspace
    try {
        & (Join-Path $PSScriptRoot 'dotnet.ps1') restore 'SceneWorkbench.slnx' --locked-mode --disable-parallel --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }
    } finally { Pop-Location }
}
Write-Output ('Workspace SDK ready: ' + $scenePinnedVersion)
