param([switch]$Publish, [switch]$CI)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
if (-not $env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $root '.tools\home' }
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $root '.tools\packages' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $root
try {
    $restoreArgs = @()
    $testArgs = @()
    if ($CI) {
        foreach ($project in Get-ChildItem -Path 'src','tests' -Filter '*.csproj' -Recurse) {
            if (-not (Test-Path -LiteralPath (Join-Path $project.DirectoryName 'packages.lock.json'))) {
                throw "Missing dependency lock file for $($project.FullName). Generate and review it before running CI."
            }
        }
        & $dotnet restore 'NovelRAG.sln' --locked-mode --nologo -m:1 -nr:false
        if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed' }
        $restoreArgs = @('--no-restore')
        $testArgs = @('--logger', 'trx;LogFileName=tests.trx', '--results-directory', (Join-Path $root 'artifacts\test-results'))
    }
    & $dotnet test 'tests\NovelRAG.Tests\NovelRAG.Tests.csproj' -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false @restoreArgs @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    & $dotnet build 'src\NovelRAG.Desktop\NovelRAG.Desktop.csproj' -c Release --nologo -m:1 -nr:false -p:UseSharedCompilation=false @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    if ($CI) {
        & $dotnet format 'NovelRAG.sln' --verify-no-changes --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Format verification failed' }
    }
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
