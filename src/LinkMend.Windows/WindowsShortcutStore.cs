using System.Runtime.InteropServices;
using System.Text;
using LinkMend.Core.Abstractions;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;
using LinkMend.Windows.Interop;

namespace LinkMend.Windows;

/// <summary>
/// Reads and writes classic .lnk files through the Windows ShellLink COM interface.
/// All methods must be called on an STA thread.
/// </summary>
public sealed class WindowsShortcutStore : IShortcutStore
{
    private const int ErrorAccessDenied = unchecked((int)0x80070005);
    private const int ErrorFileNotFound = unchecked((int)0x80070002);
    private const int ErrorPathNotFound = unchecked((int)0x80070003);
    private const int ErrorInvalidArgument = unchecked((int)0x80070057);
    private const int ErrorGenericFailure = unchecked((int)0x80004005);

    public ShortcutReadResult Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ShortcutReadResult.Fail(ShortcutReadFailureKind.NotFound, "快捷方式路径为空。");
        }

        object? comObject = null;
        try
        {
            if (!File.Exists(path))
            {
                return ShortcutReadResult.Fail(ShortcutReadFailureKind.NotFound, $"文件不存在：{path}");
            }

            comObject = new ShellLinkInterop.ShellLink();
            var persistFile = (ShellLinkInterop.IPersistFile)comObject;
            persistFile.Load(path, ShellLinkInterop.StgmRead);
            var link = (ShellLinkInterop.IShellLinkW)comObject;

            var target = ReadPath(link);
            var description = ReadString(link.GetDescription);
            var workingDirectory = ReadString(link.GetWorkingDirectory);
            var arguments = ReadString(link.GetArguments);

            var iconBuffer = new StringBuilder(ShellLinkInterop.MaxPathBuffer);
            link.GetIconLocation(iconBuffer, iconBuffer.Capacity, out var iconIndex);

            var shortcut = new ShortcutLink
            {
                FilePath = path,
                DisplayName = Path.GetFileNameWithoutExtension(path),
                Description = description,
                TargetPath = target,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                IconPath = Clean(iconBuffer.ToString()),
                IconIndex = iconIndex
            };

            return ShortcutReadResult.Ok(shortcut);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ShortcutReadResult.Fail(ShortcutReadFailureKind.AccessDenied, ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return ShortcutReadResult.Fail(ShortcutReadFailureKind.NotFound, ex.Message);
        }
        catch (COMException ex)
        {
            return ShortcutReadResult.Fail(MapComFailure(ex), $"读取快捷方式失败（HRESULT 0x{ex.HResult:X8}）：{ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            return ShortcutReadResult.Fail(ShortcutReadFailureKind.Unknown, ex.Message);
        }
        finally
        {
            Release(comObject);
        }
    }

    public void Apply(string path, ShortcutPatch patch)
    {
        if (!patch.HasChanges)
        {
            return;
        }

        if (!File.Exists(path))
        {
            throw new ShortcutWriteException($"快捷方式不存在：{path}");
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ShortcutWriteException("无法确定快捷方式所在目录。");
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(path)}.linkmend-{Guid.NewGuid():N}.tmp.lnk");

        object? comObject = null;
        try
        {
            comObject = new ShellLinkInterop.ShellLink();
            var persistFile = (ShellLinkInterop.IPersistFile)comObject;
            persistFile.Load(path, ShellLinkInterop.StgmRead);
            var link = (ShellLinkInterop.IShellLinkW)comObject;

            if (patch.TargetPath is not null)
            {
                link.SetPath(patch.TargetPath);
            }

            if (patch.WorkingDirectory is not null)
            {
                link.SetWorkingDirectory(patch.WorkingDirectory);
            }

            if (patch.Icon is { } icon)
            {
                link.SetIconLocation(icon.Clear ? "" : icon.Path, icon.Clear ? 0 : icon.Index);
            }

            persistFile.Save(temporaryPath, true);
            Release(comObject);
            comObject = null;

            VerifyTemporaryLink(temporaryPath, patch);
            ReplaceFile(temporaryPath, path);
        }
        catch (ShortcutWriteException)
        {
            throw;
        }
        catch (COMException ex)
        {
            throw new ShortcutWriteException(
                $"写入快捷方式失败（HRESULT 0x{ex.HResult:X8}）：{ex.Message}",
                ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new ShortcutWriteException($"写入快捷方式失败：{ex.Message}", ex);
        }
        finally
        {
            Release(comObject);
            TryDelete(temporaryPath);
        }
    }

    private void VerifyTemporaryLink(string temporaryPath, ShortcutPatch patch)
    {
        var readBack = Read(temporaryPath);
        if (!readBack.Success || readBack.Link is null)
        {
            throw new ShortcutWriteException(
                $"新快捷方式内容无法重新读取：{readBack.Failure?.Message ?? "未知错误"}");
        }

        var actual = readBack.Link;
        if (patch.TargetPath is not null && !PathResolver.AreSame(actual.TargetPath, patch.TargetPath))
        {
            throw new ShortcutWriteException("新快捷方式的目标路径与写入值不一致。");
        }

        if (patch.WorkingDirectory is not null
            && !PathResolver.AreSame(actual.WorkingDirectory, patch.WorkingDirectory))
        {
            throw new ShortcutWriteException("新快捷方式的起始位置与写入值不一致。");
        }

        if (patch.Icon is { } icon)
        {
            if (icon.Clear)
            {
                if (!string.IsNullOrWhiteSpace(actual.IconPath))
                {
                    throw new ShortcutWriteException("新快捷方式仍保留自定义图标，无法清除。");
                }
            }
            else if (!PathResolver.AreSame(actual.IconPath, icon.Path) || actual.IconIndex != icon.Index)
            {
                throw new ShortcutWriteException("新快捷方式的图标位置与写入值不一致。");
            }
        }
    }

    private static void ReplaceFile(string temporaryPath, string destinationPath)
    {
        try
        {
            File.Replace(
                temporaryPath,
                destinationPath,
                destinationBackupFileName: null,
                ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
        {
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
    }

    private static string ReadPath(ShellLinkInterop.IShellLinkW link)
    {
        var buffer = new StringBuilder(ShellLinkInterop.MaxPathBuffer);
        link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, ShellLinkInterop.SlgpRawPath);
        var value = Clean(buffer.ToString());
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        buffer.Clear();
        link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
        return Clean(buffer.ToString());
    }

    private static string ReadString(Action<StringBuilder, int> getter)
    {
        var buffer = new StringBuilder(ShellLinkInterop.MaxPathBuffer);
        getter(buffer, buffer.Capacity);
        return Clean(buffer.ToString());
    }

    private static string Clean(string value)
    {
        var nullIndex = value.IndexOf('\0');
        return (nullIndex >= 0 ? value[..nullIndex] : value).Trim();
    }

    private static ShortcutReadFailureKind MapComFailure(COMException exception) =>
        exception.HResult switch
        {
            ErrorAccessDenied => ShortcutReadFailureKind.AccessDenied,
            ErrorFileNotFound or ErrorPathNotFound => ShortcutReadFailureKind.NotFound,
            ErrorInvalidArgument or ErrorGenericFailure => ShortcutReadFailureKind.Corrupt,
            _ => ShortcutReadFailureKind.Unknown
        };

    private static void Release(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }
        catch (InvalidOperationException)
        {
            // The runtime may already have released the wrapper.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The original file was never replaced, so a stale temp file is harmless.
        }
    }
}
