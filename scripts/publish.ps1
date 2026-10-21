param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET 10 SDK is required and 'dotnet' was not found on PATH."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$props = [xml](Get-Content (Join-Path $repoRoot "Directory.Build.props"))
$version = ($props.Project.PropertyGroup | Select-Object -First 1).Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version was not found in Directory.Build.props."
}

$publishDir = Join-Path $repoRoot "artifacts\publish\LinkMend-$Runtime"
$releaseDir = Join-Path $repoRoot "artifacts\release"
$zipPath = Join-Path $releaseDir "LinkMend-v$version-$Runtime.zip"

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

dotnet publish (Join-Path $repoRoot "src\LinkMend.App\LinkMend.App.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Copy-Item (Join-Path $repoRoot "README.md") $publishDir -Force
Copy-Item (Join-Path $repoRoot "CHANGELOG.md") $publishDir -Force
Copy-Item (Join-Path $repoRoot "LICENSE") $publishDir -Force

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Created $zipPath"
