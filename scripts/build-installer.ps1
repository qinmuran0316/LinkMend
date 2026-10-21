param(
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$props = [xml](Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props"))
$version = [string]($props.Project.PropertyGroup | Select-Object -First 1).Version
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "The installer requires a stable numeric version in Directory.Build.props."
}

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot "publish.ps1") -Configuration Release -Runtime win-x64
}

$publishDir = Join-Path $repoRoot "artifacts\publish\LinkMend-win-x64"
$appPath = Join-Path $publishDir "LinkMend.exe"
if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) {
    throw "Published application missing. Run this script without -SkipPublish first."
}
if ((Get-Item -LiteralPath $appPath).VersionInfo.ProductVersion.Split('+')[0] -ne $version) {
    throw "The published application version does not match Directory.Build.props."
}

# Pin the official compiler and language file so local and CI builds use the same inputs.
$innoVersion = "6.7.3"
$toolsDir = Join-Path $repoRoot "artifacts\tools"
$compilerDir = Join-Path $toolsDir "InnoSetup-$innoVersion"
$compilerPath = Join-Path $compilerDir "ISCC.exe"
$compilerSetup = Join-Path $toolsDir "innosetup-$innoVersion.exe"
$languagePath = Join-Path $toolsDir "ChineseSimplified-$innoVersion.isl"
New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Get-VerifiedDownload {
    param([string]$Uri, [string]$Path, [string]$SHA256)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $Path -TimeoutSec 120
    }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $SHA256) {
        throw "Checksum mismatch: $Path. Remove this cached download before retrying."
    }
}

Get-VerifiedDownload `
    -Uri "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe" `
    -Path $compilerSetup `
    -SHA256 "9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732"
Get-VerifiedDownload `
    -Uri "https://raw.githubusercontent.com/jrsoftware/issrc/is-6_7_3/Files/Languages/Unofficial/ChineseSimplified.isl" `
    -Path $languagePath `
    -SHA256 "7d544b9bb1d142cfa11f2e5d3cc8abe2e55f8e066c5124e3772675aa236e1278"

if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    # Upstream's portable mode creates no Start Menu entries or uninstall registration.
    $setupArguments = @(
        "/PORTABLE=1", "/CURRENTUSER", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
        ('/DIR="{0}"' -f $compilerDir), ('/LOG="{0}"' -f (Join-Path $toolsDir "inno-bootstrap.log"))
    )
    $setup = Start-Process -FilePath $compilerSetup -ArgumentList $setupArguments -WindowStyle Hidden -Wait -PassThru
    try {
        if ($setup.ExitCode -ne 0) {
            throw "Inno Setup bootstrap failed with exit code $($setup.ExitCode)."
        }
    }
    finally { $setup.Dispose() }
}
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw "The portable Inno Setup compiler was not created."
}

$releaseDir = Join-Path $repoRoot "artifacts\release"
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
& $compilerPath "/Qp" "/DAppVersion=$version" "/DRepoRoot=$repoRoot" `
    "/DPublishDir=$publishDir" "/DOutputDir=$releaseDir" "/DChineseMessages=$languagePath" `
    (Join-Path $repoRoot "installer\LinkMend.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed."
}

$installerPath = Join-Path $releaseDir "LinkMend-v$version-win-x64-Setup.exe"
$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($installerPath + ".sha256",
    $hash + "  " + [IO.Path]::GetFileName($installerPath) + [Environment]::NewLine,
    [Text.Encoding]::ASCII)
Write-Host "Created $installerPath"
