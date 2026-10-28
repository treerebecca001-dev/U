$ErrorActionPreference = 'Stop'
$sceneWorkspace = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $sceneWorkspace '.tools/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$sceneLocalDotnet = Join-Path $sceneWorkspace '.tools/dotnet/dotnet.exe'
if (Test-Path -LiteralPath $sceneLocalDotnet) {
    & $sceneLocalDotnet @args
} else {
    & dotnet @args
}
exit $LASTEXITCODE
