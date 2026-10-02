param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $root '.tools\home'
$env:NUGET_PACKAGES = Join-Path $root '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $root
try {
    & $dotnet test 'tests\NovelRAG.Tests\NovelRAG.Tests.csproj' -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    & $dotnet build 'src\NovelRAG.Desktop\NovelRAG.Desktop.csproj' -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    if ($Publish) {
        & $dotnet publish 'src\NovelRAG.Desktop\NovelRAG.Desktop.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o 'artifacts\NovelRAG-win-x64'
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
        Copy-Item -LiteralPath 'README.md','PRODUCT.md','V1_SCOPE.md','ARCHITECTURE.md','DATA_MODEL.md','AI_DESIGN.md','FUTURE.md','THIRD_PARTY_NOTICES.md' -Destination 'artifacts\NovelRAG-win-x64'
        Copy-Item -LiteralPath 'third_party' -Destination 'artifacts\NovelRAG-win-x64' -Recurse -Force
        New-Item -ItemType Directory -Force -Path 'artifacts\NovelRAG-win-x64\docs' | Out-Null
        Copy-Item -LiteralPath 'docs\OPEN_SOURCE_RESEARCH.md','docs\VERIFICATION.md' -Destination 'artifacts\NovelRAG-win-x64\docs'
        Compress-Archive -Path 'artifacts\NovelRAG-win-x64\*' -DestinationPath 'artifacts\NovelRAG-win-x64.zip' -Force
    }
} finally { Pop-Location }
