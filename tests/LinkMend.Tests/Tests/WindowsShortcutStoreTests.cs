using System.Runtime.InteropServices;
using LinkMend.Core.Abstractions;
using LinkMend.Core.Backup;
using LinkMend.Core.Diagnosis;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;
using LinkMend.Core.Repair;
using LinkMend.Core.Scan;
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
    public async Task RepairAndUndo_RealShortcut_RestoresOriginalBytesAndPreservesArguments()
    {
        using var temp = new TempDirectory();
        var oldTarget = temp.Combine("OldApp/demo.exe");
        var oldDirectory = temp.CreateDirectory("OldApp");
        var newTarget = temp.CreateFile("NewApp/demo.exe");
        var linkPath = temp.Combine("Demo.lnk");
        await StaExecutor.RunAsync(() =>
        {
            var icon = $"{Path.Combine(Environment.SystemDirectory, "shell32.dll")},0";
            CreateWithWScript(linkPath, oldTarget, "--profile \"C:\\A B\"", oldDirectory, icon);
            var originalBytes = File.ReadAllBytes(linkPath);
            var store = new WindowsShortcutStore();
            var probe = new WindowsSystemProbe();
            var scanner = new ShortcutScanner(store, new ShortcutDiagnoser(probe));
            var scanned = Assert.Single(scanner.Scan(
                new ScanRequest(Array.Empty<ScanRoot>(), new[] { linkPath }), null, CancellationToken.None).Shortcuts);
            var candidate = new ResolutionCandidate(newTarget, 90, CandidateConfidence.High, Array.Empty<string>(), null);
            var plan = new RepairPlanBuilder(probe).Build(scanned, candidate);
            Assert.NotNull(plan);
            var repository = new BackupRepository(temp.Combine("BackupRoot"));

            var repaired = new RepairService(store, repository, probe).Apply(new[] { plan! });

            Assert.Equal(1, repaired.RepairedCount);
            var readBack = store.Read(linkPath);
            Assert.True(PathResolver.AreSame(newTarget, readBack.Link!.TargetPath));
            Assert.Equal("--profile \"C:\\A B\"", readBack.Link.Arguments);
            var history = repository.LoadAll();
            var undone = new UndoService(repository).Undo(Assert.Single(history), history);
            Assert.Equal(OperationItemOutcome.Restored, Assert.Single(undone.Items).Outcome);
            Assert.Equal(originalBytes, File.ReadAllBytes(linkPath));
            return true;
        });
    }

    [Fact]
    public async Task Apply_WhenHashNoLongerMatches_PreservesChangedShortcut()
    {
        using var temp = new TempDirectory();
        var target = temp.CreateFile("demo.exe");
        var linkPath = temp.Combine("Demo.lnk");
        await StaExecutor.RunAsync(() =>
        {
            var icon = $"{Path.Combine(Environment.SystemDirectory, "shell32.dll")},0";
            CreateWithWScript(linkPath, target, "--old", temp.Path, icon);
            var expectedHash = FileHash.Compute(linkPath);
            CreateWithWScript(linkPath, target, "--new-user-arguments", temp.Path, icon);
            var changedBytes = File.ReadAllBytes(linkPath);
            var store = new WindowsShortcutStore();

            Assert.Throws<FileChangedException>(() => store.Apply(linkPath,
                new ShortcutPatch(WorkingDirectory: temp.Path, ExpectedHash: expectedHash)));
            Assert.Equal(changedBytes, File.ReadAllBytes(linkPath));
            Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.tmp.lnk"));
            return true;
        });
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
