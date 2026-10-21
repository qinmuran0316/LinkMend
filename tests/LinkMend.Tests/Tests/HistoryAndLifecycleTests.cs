using LinkMend.App.Services;
using LinkMend.App.ViewModels;
using LinkMend.Core.Backup;
using LinkMend.Core.Diagnosis;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;
using LinkMend.Core.Relocation;
using LinkMend.Core.Repair;
using LinkMend.Core.Scan;
using LinkMend.Tests.Support;

namespace LinkMend.Tests.Tests;

public sealed class HistoryAndLifecycleTests
{
    [Fact]
    public void History_PartialUndo_ShowsRemainingItemsAndAllowsRetry()
    {
        var batch = new BackupBatch
        {
            Items = new List<BackupItem>
            {
                new() { Status = BackupStatuses.Repaired },
                new() { Status = BackupStatuses.Repaired }
            }
        };
        var row = new HistoryRow(batch, isUndone: false, remainingUndoCount: 1);

        Assert.Equal("部分撤销", row.StateLabel);
        Assert.True(row.CanUndo);
        Assert.Contains("剩余 1", row.UndoHint);
        Assert.Contains("待撤销 1", row.Summary);
    }

    [Fact]
    public void History_UndoWithPendingItems_IsNotShownAsComplete()
    {
        var batch = new BackupBatch
        {
            Operation = BackupOperations.Undo,
            Items = new List<BackupItem>
            {
                new() { Status = BackupStatuses.Restored },
                new() { Status = BackupStatuses.Pending }
            }
        };

        Assert.Equal("未完成", new HistoryRow(batch, false).StateLabel);
    }

    [Fact]
    public async Task Close_DuringRepair_WaitsForWriteToFinish()
    {
        using var temp = new TempDirectory();
        using var writing = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var path = temp.CreateFile("Demo.lnk", "original");
        var store = new FakeShortcutStore();
        var link = new ShortcutLink { FilePath = path, TargetPath = @"C:\Old\demo.exe" };
        store.Links[path] = link;
        store.AfterApply = _ =>
        {
            writing.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("测试未释放写入线程。");
            }
        };
        var probe = new FakeSystemProbe();
        probe.SetFile(@"C:\New\demo.exe");
        var backups = new BackupRepository(temp.Combine("BackupRoot"));
        var viewModel = CreateViewModel(store, probe, backups);
        var item = new ScannedShortcut(link, PathResolver.Resolve(link), FileHash.Compute(path), "测试", DiagnosisResult.Healthy);
        var operation = viewModel.RepairAsync(new[]
        {
            new RepairPlan(item, item.ContentHash, @"C:\New\demo.exe", null, null)
        });
        try
        {
            Assert.True(writing.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(viewModel.TryPrepareToClose());
        }
        finally
        {
            release.Set();
            await operation;
        }

        Assert.True(viewModel.TryPrepareToClose());
        Assert.Equal(BackupStatuses.Repaired, Assert.Single(backups.LoadAll()).Items[0].Status);
    }

    [Fact]
    public async Task Undo_WhenHistoryRecordWasDeleted_RejectsCachedRow()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateFile("Demo.lnk", "original");
        var bytes = File.ReadAllBytes(path);
        var backups = new BackupRepository(temp.Combine("BackupRoot"));
        var batch = backups.CreateBatch(BackupOperations.Repair, new[]
        {
            new BackupSeed(path, "Demo.lnk", bytes, FileHash.Compute(bytes), "", "", "")
        });
        batch.Items[0].Status = BackupStatuses.Repaired;
        batch.Items[0].ResultHash = FileHash.Compute(bytes);
        backups.Save(batch);
        var row = new HistoryRow(batch, false);
        var viewModel = CreateViewModel(new FakeShortcutStore(), new FakeSystemProbe(), backups);
        File.Delete(Path.Combine(backups.HistoryDirectory, $"{batch.Id}.json"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.PreviewUndoAsync(row));
        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.UndoAsync(row));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.True(viewModel.TryPrepareToClose());
    }

    private static MainViewModel CreateViewModel(
        FakeShortcutStore store, FakeSystemProbe probe, BackupRepository backups) =>
        new(new ShortcutScanner(store, new ShortcutDiagnoser(probe)),
            new RelocationService(probe, new FakeInstallationIndex(), new FakeExecutableSearch(), new FakeSearchRootProvider()),
            new RepairPlanBuilder(probe), new RepairService(store, backups, probe), new UndoService(backups),
            backups, new AppSettingsStore(backups.RootDirectory));
}
