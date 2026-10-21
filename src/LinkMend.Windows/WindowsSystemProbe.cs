using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LinkMend.Core.Abstractions;
using LinkMend.Core.Paths;

namespace LinkMend.Windows;

public sealed class WindowsSystemProbe : ISystemProbe
{
    private const int InvalidFileAttributes = -1;

    public PathProbe ProbePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new PathProbe(ProbeOutcome.InvalidPath, "路径为空。");
        }

        var cleaned = PathResolver.Expand(path);
        if (PathResolver.IsShellNamespace(cleaned))
        {
            return new PathProbe(ProbeOutcome.ShellNamespace, "这是 Windows Shell 命名空间路径。");
        }

        if (TryCheckDriveReadiness(cleaned, out var driveProbe))
        {
            return driveProbe;
        }

        var attributes = GetFileAttributesW(ToExtendedPath(cleaned));
        if (attributes != InvalidFileAttributes)
        {
            return (attributes & (int)FileAttributes.Directory) != 0
                ? new PathProbe(ProbeOutcome.Directory)
                : new PathProbe(ProbeOutcome.File);
        }

        var error = Marshal.GetLastWin32Error();
        var outcome = MapWin32Error(error);
        if (outcome == ProbeOutcome.NotFound && TryCheckUncShare(cleaned, out var networkProbe))
        {
            return networkProbe;
        }

        return new PathProbe(outcome, DescribeWin32Error(error));
    }

    public FileMetadata? ReadMetadata(string path)
    {
        if (ProbePath(path).Outcome != ProbeOutcome.File)
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            long length = 0;
            try
            {
                length = new FileInfo(path).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Version information is still useful without a file size.
            }

            return new FileMetadata(
                Clean(info.ProductName),
                Clean(info.CompanyName),
                Clean(info.FileDescription),
                Clean(info.OriginalFilename),
                length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public IconProbeOutcome ProbeIcon(string iconPath, int iconIndex)
    {
        var pathProbe = ProbePath(iconPath);
        if (pathProbe.Outcome == ProbeOutcome.NotFound)
        {
            return IconProbeOutcome.Missing;
        }

        if (!pathProbe.Exists || !PathResolver.IsIconCapable(iconPath) || iconIndex < 0)
        {
            return IconProbeOutcome.Unknown;
        }

        IntPtr largeIcon = IntPtr.Zero;
        IntPtr smallIcon = IntPtr.Zero;
        try
        {
            var extracted = ExtractIconExW(iconPath, iconIndex, out largeIcon, out smallIcon, 1);
            if (extracted == 0 || (largeIcon == IntPtr.Zero && smallIcon == IntPtr.Zero))
            {
                return IconProbeOutcome.IndexUnavailable;
            }

            return IconProbeOutcome.Available;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return IconProbeOutcome.Unknown;
        }
        finally
        {
            if (largeIcon != IntPtr.Zero)
            {
                DestroyIcon(largeIcon);
            }

            if (smallIcon != IntPtr.Zero)
            {
                DestroyIcon(smallIcon);
            }
        }
    }

    public IReadOnlyList<string> GetReadyFixedDriveRoots()
    {
        var roots = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    {
                        roots.Add(drive.RootDirectory.FullName);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Ignore drives that disappear while enumerating.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A machine with no enumerable drives still gets an empty list.
        }

        return roots;
    }

    private static bool TryCheckDriveReadiness(string path, out PathProbe probe)
    {
        probe = default!;
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root)
                || root.Length != 3
                || root[1] != ':'
                || (root[2] != '\\' && root[2] != '/'))
            {
                return false;
            }

            var drive = new DriveInfo(root);
            if (drive.IsReady)
            {
                return false;
            }

            probe = drive.DriveType == DriveType.Network
                ? new PathProbe(ProbeOutcome.NetworkUnavailable, "映射的网络驱动器当前未连接。")
                : new PathProbe(ProbeOutcome.VolumeUnavailable, "磁盘当前未就绪。");
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryCheckUncShare(string path, out PathProbe probe)
    {
        probe = default!;
        if (!path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var shareRoot = $@"\\{parts[0]}\{parts[1]}";
        var attributes = GetFileAttributesW(shareRoot);
        if (attributes != InvalidFileAttributes)
        {
            return false;
        }

        var error = Marshal.GetLastWin32Error();
        if (MapWin32Error(error) is ProbeOutcome.NetworkUnavailable or ProbeOutcome.AccessDenied)
        {
            probe = new PathProbe(
                MapWin32Error(error),
                $"无法访问网络共享 {shareRoot}：{DescribeWin32Error(error)}");
            return true;
        }

        return false;
    }

    private static ProbeOutcome MapWin32Error(int error) => error switch
    {
        2 or 3 => ProbeOutcome.NotFound,
        5 => ProbeOutcome.AccessDenied,
        21 => ProbeOutcome.VolumeUnavailable,
        53 or 55 or 59 or 64 or 67 or 1203 or 1225 or 1231 or 1232 => ProbeOutcome.NetworkUnavailable,
        87 or 123 or 161 => ProbeOutcome.InvalidPath,
        _ => ProbeOutcome.Unknown
    };

    private static string DescribeWin32Error(int error)
    {
        var message = error switch
        {
            2 or 3 => "系统返回：找不到文件或目录。",
            5 => "系统返回：访问被拒绝。",
            21 => "系统返回：设备未就绪。",
            53 or 67 => "系统返回：找不到网络路径。",
            87 or 123 => "系统返回：路径格式无效。",
            _ => $"系统返回 Win32 错误 {error}。"
        };

        try
        {
            var native = new Win32Exception(error).Message;
            if (!string.IsNullOrWhiteSpace(native) && error is not (2 or 3 or 5 or 21 or 53 or 67 or 87 or 123))
            {
                message = $"{message}（{native}）";
            }
        }
        catch (Exception ex) when (ex is ArgumentException)
        {
            // Keep the readable generic message.
        }

        return message;
    }

    private static string ToExtendedPath(string path)
    {
        if (path.Length < 240 || path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + path[2..]
            : @"\\?\" + path;
    }

    private static string Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetFileAttributesW(string lpFileName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(
        string lpszFile,
        int nIconIndex,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
