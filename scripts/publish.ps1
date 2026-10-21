param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64", "win-x86")]
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

$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\publish"))
$publishDir = [System.IO.Path]::GetFullPath((Join-Path $publishRoot "LinkMend-$Runtime"))
if (-not $publishDir.StartsWith($publishRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The publish directory must remain inside artifacts\publish."
}
$releaseDir = Join-Path $repoRoot "artifacts\release"
$zipPath = Join-Path $releaseDir "LinkMend-v$version-$Runtime.zip"

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
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

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zipPath + ".sha256",
    $hash + "  " + [IO.Path]::GetFileName($zipPath) + [Environment]::NewLine,
    [Text.Encoding]::ASCII)
Write-Host "Created $zipPath"
