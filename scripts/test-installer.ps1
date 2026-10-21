param(
    [string]$InstallerPath
)

$ErrorActionPreference = "Stop"
Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
namespace LinkMend.InstallerSmoke {
    public static class NativeMethods {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetLongPathName(string path, StringBuilder output, uint capacity);

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity,
                System.IntPtr findData, uint flags);
        }

        public static string ReadShortcutTarget(string path) {
            object link = System.Activator.CreateInstance(System.Type.GetTypeFromCLSID(
                new System.Guid("00021401-0000-0000-C000-000000000046")));
            try {
                ((IPersistFile)link).Load(path, 0);
                var output = new StringBuilder(32768);
                ((IShellLinkW)link).GetPath(output, output.Capacity, System.IntPtr.Zero, 4);
                return output.ToString();
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }
    }
}
'@
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$props = [xml](Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props"))
$version = [string]($props.Project.PropertyGroup | Select-Object -First 1).Version
if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $repoRoot "artifacts\release\LinkMend-v$version-win-x64-Setup.exe"
}
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\qinmuran0316.LinkMend_is1"
if (Test-Path -LiteralPath $uninstallKey) {
    throw "An installed LinkMend already exists. Run this smoke test in a clean user account."
}

$testId = [Guid]::NewGuid().ToString("N")
$testRoot = Join-Path $repoRoot "artifacts\installer-smoke\$testId"
# Exercise spaces and non-ASCII paths without depending on PowerShell's source encoding.
$installDir = Join-Path $testRoot ("Install " + [char]0x5B89 + [char]0x88C5)
$group = "LinkMend-InstallerTest-$testId"
$programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$startMenuLink = Join-Path $programs "$group\LinkMend.lnk"
$desktopLink = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "LinkMend.lnk"
$testDesktop = -not (Test-Path -LiteralPath $desktopLink)
$taskArgument = if ($testDesktop) { "/TASKS=desktopicon" } else { '/TASKS=""' }
$dataRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "LinkMend"
$sentinel = Join-Path $dataRoot "installer-smoke-$testId.tmp"
New-Item -ItemType Directory -Force -Path $testRoot, $dataRoot | Out-Null
[IO.File]::WriteAllText($sentinel, $testId, [Text.Encoding]::ASCII)
$sentinelHash = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
$installedExe = Join-Path $installDir "LinkMend.exe"
$uninstaller = Join-Path $installDir "unins000.exe"
$expectedExeHash = (Get-FileHash -LiteralPath (Join-Path $repoRoot "artifacts\publish\LinkMend-win-x64\LinkMend.exe") -Algorithm SHA256).Hash
$appProcess = $null

function Invoke-SetupProcess {
    param([string]$Path, [string[]]$SetupArguments)

    $process = Start-Process -FilePath $Path -ArgumentList $SetupArguments -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(45000)) {
            $process.Kill()
            throw "Installer process timed out: $Path"
        }
        return $process.ExitCode
    }
    finally { $process.Dispose() }
}

function Assert-Installed {
    if ((Get-FileHash -LiteralPath $installedExe -Algorithm SHA256).Hash -ne $expectedExeHash) {
        throw "The installed executable differs from the published application."
    }
    $registration = Get-ItemProperty -LiteralPath $uninstallKey
    if ($registration.DisplayVersion -ne $version) { throw "Incorrect uninstall registration version." }
    if ($registration.InstallLocation.TrimEnd('\') -ne $installDir) { throw "Incorrect registered installation directory." }
    if (-not (Test-Path -LiteralPath $startMenuLink -PathType Leaf)) { throw "Start Menu shortcut missing." }
    Assert-ShortcutTarget $startMenuLink
    if ($testDesktop) { Assert-ShortcutTarget $desktopLink }
    if ((Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash -ne $sentinelHash) {
        throw "Installation modified application data."
    }
}

function Get-LongFilePath {
    param([string]$Path)

    $output = New-Object Text.StringBuilder 32768
    $length = [LinkMend.InstallerSmoke.NativeMethods]::GetLongPathName($Path, $output, $output.Capacity)
    if ($length -eq 0 -or $length -ge $output.Capacity) {
        throw "Cannot resolve shortcut path '$Path' (Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
    }
    return $output.ToString()
}

function Assert-ShortcutTarget {
    param([string]$ShortcutPath)

    # WScript.Shell can lose non-ANSI characters on English Windows; read IShellLinkW directly.
    $target = [LinkMend.InstallerSmoke.NativeMethods]::ReadShortcutTarget($ShortcutPath)
    $actualTarget = Get-LongFilePath $target
    $expectedTarget = Get-LongFilePath $installedExe
    if ($actualTarget -ne $expectedTarget) {
        throw "Shortcut '$ShortcutPath' targets '$target' instead of '$installedExe'."
    }
    if ((Get-FileHash -LiteralPath $actualTarget -Algorithm SHA256).Hash -ne $expectedExeHash) {
        throw "Shortcut target does not match the published executable."
    }
}

try {
    $commonArguments = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-",
        ('/DIR="{0}"' -f $installDir), ('/GROUP="{0}"' -f $group), $taskArgument)
    $installExit = Invoke-SetupProcess $InstallerPath ($commonArguments + ('/LOG="{0}"' -f (Join-Path $testRoot "install.log")))
    if ($installExit -ne 0) { throw "Installation failed: $installExit" }
    Assert-Installed

    $appProcess = Start-Process -FilePath $installedExe -WorkingDirectory $installDir -WindowStyle Hidden -PassThru
    $null = $appProcess.WaitForInputIdle(20000)
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $appProcess.Refresh()
        if ($appProcess.HasExited) { throw "The installed application exited during startup." }
        if ($appProcess.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($appProcess.MainWindowHandle -eq [IntPtr]::Zero -or $appProcess.MainWindowTitle -notlike 'LinkMend*') {
        throw "The installed application did not open its main window."
    }
    $runningMarker = $null
    if (-not [Threading.Mutex]::TryOpenExisting('Local\LinkMend.Running', [ref]$runningMarker)) {
        throw "The application did not expose its installer running marker."
    }
    $runningMarker.Dispose()

    $blockedInstallExit = Invoke-SetupProcess $InstallerPath ($commonArguments + ('/LOG="{0}"' -f (Join-Path $testRoot "blocked-upgrade.log")))
    if ($blockedInstallExit -eq 0) { throw "Upgrade unexpectedly proceeded while LinkMend was running." }
    $uninstallArguments = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART")
    $blockedUninstallExit = Invoke-SetupProcess $uninstaller ($uninstallArguments + ('/LOG="{0}"' -f (Join-Path $testRoot "blocked-uninstall.log")))
    if ($blockedUninstallExit -eq 0) { throw "Uninstall unexpectedly proceeded while LinkMend was running." }
    Assert-Installed
    $null = $appProcess.CloseMainWindow()
    if (-not $appProcess.WaitForExit(15000) -or $appProcess.ExitCode -ne 0) {
        throw "The installed application did not exit normally."
    }
    $appProcess.Dispose()
    $appProcess = $null

    $upgradeExit = Invoke-SetupProcess $InstallerPath ($commonArguments + ('/LOG="{0}"' -f (Join-Path $testRoot "upgrade.log")))
    if ($upgradeExit -ne 0) { throw "Reinstallation failed: $upgradeExit" }
    Assert-Installed
    $uninstallExit = Invoke-SetupProcess $uninstaller ($uninstallArguments + ('/LOG="{0}"' -f (Join-Path $testRoot "uninstall.log")))
    if ($uninstallExit -ne 0) { throw "Uninstallation failed: $uninstallExit" }
    if ((Test-Path -LiteralPath $installedExe) -or (Test-Path -LiteralPath $uninstallKey) -or
        (Test-Path -LiteralPath $startMenuLink) -or ($testDesktop -and (Test-Path -LiteralPath $desktopLink))) {
        throw "Uninstallation left application files, shortcuts, or its registration behind."
    }
    if ((Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash -ne $sentinelHash) {
        throw "Uninstallation removed or modified application data."
    }

    $report = [pscustomobject]@{
        Version = $version
        InstallerSHA256 = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash
        NonAsciiInstallPath = $true
        InstalledExecutableMatches = $true
        StartMenuShortcut = $true
        DesktopShortcutTested = $testDesktop
        ApplicationStartedAndExited = $true
        RunningUpgradeBlocked = $true
        RunningUninstallBlocked = $true
        ReinstallSucceeded = $true
        UninstallSucceeded = $true
        ApplicationDataPreserved = $true
        LogsDirectory = $testRoot
    } | ConvertTo-Json
    $reportDir = Join-Path $repoRoot "artifacts\test-results"
    New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
    $report | Set-Content -LiteralPath (Join-Path $reportDir "installer-smoke-v$version.json") -Encoding UTF8
    Write-Output $report
}
finally {
    if ($null -ne $appProcess) {
        if (-not $appProcess.HasExited) {
            $null = $appProcess.CloseMainWindow()
            if (-not $appProcess.WaitForExit(10000)) { $appProcess.Kill(); $appProcess.WaitForExit() }
        }
        $appProcess.Dispose()
    }
    if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
        $null = Invoke-SetupProcess $uninstaller @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART")
    }
    if (Test-Path -LiteralPath $sentinel) { Remove-Item -LiteralPath $sentinel -Force }
}
