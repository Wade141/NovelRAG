param([switch]$Publish, [switch]$CI, [string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
if (-not $env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $root '.tools\home' }
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $root '.tools\packages' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $root
try {
    if ($PublishDirectory -and -not $Publish) { throw 'PublishDirectory requires -Publish' }
    if ($Publish) {
        $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
        $output = if ($PublishDirectory) { [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($PublishDirectory)) { $PublishDirectory } else { Join-Path $root $PublishDirectory })) } else { Join-Path $artifactsRoot ('NovelRAG-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
        if (-not $output.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'PublishDirectory must be a new child directory of artifacts' }
        if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath ($output + '.zip')) -or (Test-Path -LiteralPath ($output + '.release.json'))) { throw 'Publish output already exists; choose a fresh directory' }
    }
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
        & $dotnet publish 'src\NovelRAG.Desktop\NovelRAG.Desktop.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:RestoreLockedMode=true -o $output
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
        Copy-Item -LiteralPath 'README.md','PRODUCT.md','V1_SCOPE.md','ARCHITECTURE.md','DATA_MODEL.md','AI_DESIGN.md','FUTURE.md','THIRD_PARTY_NOTICES.md','AGENTS.md' -Destination $output
        Copy-Item -LiteralPath 'third_party' -Destination $output -Recurse
        New-Item -ItemType Directory -Force -Path (Join-Path $output 'docs\superpowers\plans') | Out-Null
        Copy-Item -LiteralPath 'docs\OPEN_SOURCE_RESEARCH.md','docs\VERIFICATION.md','docs\V1_ACCEPTANCE.md','docs\PERFORMANCE.md' -Destination (Join-Path $output 'docs')
        Copy-Item -LiteralPath 'docs\superpowers\plans\2026-10-07-v1-remaining-work.md' -Destination (Join-Path $output 'docs\superpowers\plans')
        $revision = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source revision' }
        $sourceFiles = & git ls-files --cached --others --exclude-standard
        if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source snapshot' }
        $sourceManifest = foreach ($file in ($sourceFiles | Sort-Object -Unique)) {
            if (Test-Path -LiteralPath $file -PathType Leaf) { [pscustomobject]@{ Path=$file; Sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash } }
        }
        [pscustomobject]@{ Revision=$revision; WorkingTreeDirty=[bool](& git status --porcelain); Files=$sourceManifest } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'source-manifest.json') -Encoding UTF8
        Compress-Archive -Path (Join-Path $output '*') -DestinationPath ($output + '.zip')
        [pscustomobject]@{ Directory=$output; Zip=($output+'.zip'); Sha256=(Get-FileHash -LiteralPath ($output+'.zip') -Algorithm SHA256).Hash; Revision=$revision; Signed=$false; Published=$false } | ConvertTo-Json | Set-Content -LiteralPath ($output+'.release.json') -Encoding UTF8
    }
} finally { Pop-Location }
