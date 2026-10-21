using System.Runtime.InteropServices;
using LinkMend.Core.Abstractions;
using LinkMend.Core.Paths;
using LinkMend.Windows;
using LinkMend.Windows.Threading;
using LinkMend.Tests.Support;

namespace LinkMend.Tests.Tests;

public sealed class WindowsShortcutStoreTests
{
    [Fact]
    public async Task ReadAndApply_RoundTripsFieldsAndPreservesUnrelatedValues()
    {
        using var temp = new TempDirectory();
        var target = temp.CreateFile("Apps/Demo/demo.exe");
        var newTarget = temp.CreateFile("Apps/Demo/demo-new.exe");
        var linkPath = temp.Combine("Desktop/Demo.lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        var iconPath = Path.Combine(Environment.SystemDirectory, "shell32.dll");

        var result = await StaExecutor.RunAsync(() =>
        {
            CreateWithWScript(
                linkPath,
                target,
                "--profile \"C:\\A B\"",
                temp.Path,
                $"{iconPath},0",
                "Demo description");

            var store = new WindowsShortcutStore();
            var initial = store.Read(linkPath);
            var copyPath = temp.Combine("Desktop/Demo-copy.lnk");
            File.Copy(linkPath, copyPath);

            store.Apply(copyPath, new ShortcutPatch(
                TargetPath: newTarget,
                WorkingDirectory: temp.Path,
                Icon: new IconUpdate(iconPath, 1)));

            var updated = store.Read(copyPath);
            return (Initial: initial, Updated: updated, CopyPath: copyPath);
        });

        Assert.True(result.Initial.Success, result.Initial.Failure?.Message);
        Assert.True(PathResolver.AreSame(target, result.Initial.Link!.TargetPath));
        Assert.Equal("--profile \"C:\\A B\"", result.Initial.Link.Arguments);
        Assert.True(PathResolver.AreSame(temp.Path, result.Initial.Link.WorkingDirectory));
        Assert.Equal("Demo description", result.Initial.Link.Description);
        Assert.False(string.IsNullOrWhiteSpace(result.Initial.Link.IconPath));

        Assert.True(result.Updated.Success, result.Updated.Failure?.Message);
        Assert.True(PathResolver.AreSame(newTarget, result.Updated.Link!.TargetPath));
        Assert.True(PathResolver.AreSame(temp.Path, result.Updated.Link.WorkingDirectory));
        Assert.Equal("--profile \"C:\\A B\"", result.Updated.Link.Arguments);
        Assert.Equal("Demo description", result.Updated.Link.Description);
        Assert.True(PathResolver.AreSame(iconPath, result.Updated.Link.IconPath));
        Assert.Equal(1, result.Updated.Link.IconIndex);
    }

    [Fact]
    public async Task Apply_ClearIcon_RemovesCustomIcon()
    {
        using var temp = new TempDirectory();
        var target = temp.CreateFile("Apps/Demo/demo.exe");
        var linkPath = temp.Combine("Desktop/IconDemo.lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        var iconPath = Path.Combine(Environment.SystemDirectory, "shell32.dll");

        var result = await StaExecutor.RunAsync(() =>
        {
            CreateWithWScript(linkPath, target, "", temp.Path, $"{iconPath},0");
            var store = new WindowsShortcutStore();
            store.Apply(linkPath, new ShortcutPatch(Icon: new IconUpdate("", 0, Clear: true)));
            return store.Read(linkPath);
        });

        Assert.True(result.Success, result.Failure?.Message);
        Assert.True(string.IsNullOrWhiteSpace(result.Link!.IconPath));
    }

    [Fact]
    public void Read_NonexistentOrCorruptFile_ReturnsFailure()
    {
        using var temp = new TempDirectory();
        var corrupt = temp.CreateFile("bad.lnk", "this is not a shortcut");
        var store = new WindowsShortcutStore();

        var missing = store.Read(temp.Combine("missing.lnk"));
        var broken = store.Read(corrupt);

        Assert.False(missing.Success);
        Assert.Equal(ShortcutReadFailureKind.NotFound, missing.Failure!.Kind);
        Assert.False(broken.Success);
        Assert.NotNull(broken.Failure);
    }

    private static void CreateWithWScript(
        string linkPath,
        string targetPath,
        string arguments,
        string workingDirectory,
        string iconLocation,
        string? description = null)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("WScript.Shell 不可用。");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)!;
            dynamic dynamicShell = shell;
            shortcut = dynamicShell.CreateShortcut(linkPath);
            dynamic dynamicShortcut = shortcut;
            dynamicShortcut.TargetPath = targetPath;
            dynamicShortcut.Arguments = arguments;
            dynamicShortcut.WorkingDirectory = workingDirectory;
            dynamicShortcut.IconLocation = iconLocation;
            if (description is not null)
            {
                dynamicShortcut.Description = description;
            }

            dynamicShortcut.Save();
        }
        finally
        {
            Release(shortcut);
            Release(shell);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}
