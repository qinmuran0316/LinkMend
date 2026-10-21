using LinkMend.Core.Abstractions;
using LinkMend.Core.Backup;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;
using LinkMend.Core.Repair;
using LinkMend.Tests.Support;

namespace LinkMend.Tests.Tests;

public sealed class RepairServiceTests
{
    [Fact]
    public void Apply_WritesChangeAndStoresVerifiedBackup()
    {
        using var fixture = CreateFixture();
        var originalBytes = File.ReadAllBytes(fixture.LinkPath);
        var plan = fixture.CreatePlan(@"C:\New\Demo\demo.exe");

        var outcome = fixture.Service.Apply(new[] { plan });

        Assert.Equal(1, outcome.RepairedCount);
        Assert.NotNull(outcome.BatchId);
        Assert.Equal(1, fixture.Store.ApplyCount);
        Assert.Equal(@"C:\New\Demo\demo.exe", fixture.Store.Links[fixture.LinkPath].TargetPath);
        Assert.False(originalBytes.SequenceEqual(File.ReadAllBytes(fixture.LinkPath)));

        var batch = Assert.Single(fixture.Backups.LoadAll());
        var item = Assert.Single(batch.Items);
        Assert.Equal(BackupStatuses.Repaired, item.Status);
        Assert.False(string.IsNullOrWhiteSpace(item.ResultHash));
        Assert.True(File.Exists(fixture.Backups.GetSnapshotPath(batch, item)));
        Assert.True(originalBytes.SequenceEqual(fixture.Backups.ReadSnapshot(batch, item)));
    }

    [Fact]
    public void Apply_ReturnsSkippedWhenFileChangedSinceScan()
    {
        using var fixture = CreateFixture();
        var plan = fixture.CreatePlan(@"C:\New\Demo\demo.exe");
        File.WriteAllText(fixture.LinkPath, "changed-after-scan");

        var outcome = fixture.Service.Apply(new[] { plan });

        Assert.Equal(1, outcome.SkippedCount);
        Assert.Null(outcome.BatchId);
        Assert.Equal(0, fixture.Store.ApplyCount);
        Assert.Empty(fixture.Backups.LoadAll());
        Assert.Equal("changed-after-scan", File.ReadAllText(fixture.LinkPath));
    }

    [Fact]
    public void Apply_SkipsWhenRelocationTargetDisappeared()
    {
        using var fixture = CreateFixture();
        var plan = fixture.CreatePlan(@"C:\New\Demo\demo.exe");
        fixture.Probe.SetOutcome(@"C:\New\Demo\demo.exe", ProbeOutcome.NotFound);

        var outcome = fixture.Service.Apply(new[] { plan });

        Assert.Equal(1, outcome.SkippedCount);
        Assert.Equal(0, fixture.Store.ApplyCount);
        Assert.Empty(fixture.Backups.LoadAll());
    }

    [Fact]
    public void Apply_WhenVerificationFails_RestoresOriginalAndMarksFailed()
    {
        using var fixture = CreateFixture();
        var originalBytes = File.ReadAllBytes(fixture.LinkPath);
        fixture.Store.CorruptFileAfterApply = true;
        var plan = fixture.CreatePlan(@"C:\New\Demo\demo.exe");

        var outcome = fixture.Service.Apply(new[] { plan });

        Assert.Equal(1, outcome.FailedCount);
        Assert.Equal(0, outcome.RepairedCount);
        Assert.True(originalBytes.SequenceEqual(File.ReadAllBytes(fixture.LinkPath)));

        var batch = Assert.Single(fixture.Backups.LoadAll());
        Assert.Equal(BackupStatuses.Failed, batch.Items[0].Status);
        Assert.Contains("自动恢复", batch.Items[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_WhenAnotherWriterChangesResult_PreservesTheirBytes()
    {
        using var fixture = CreateFixture();
        var plan = fixture.CreatePlan(@"C:\New\Demo\demo.exe");
        fixture.Store.AfterApply = path => File.WriteAllText(path, "later-user-change");

        var outcome = fixture.Service.Apply(new[] { plan });

        Assert.Equal(1, outcome.FailedCount);
        Assert.Equal("later-user-change", File.ReadAllText(fixture.LinkPath));
        Assert.Equal(BackupStatuses.Failed, Assert.Single(fixture.Backups.LoadAll()).Items[0].Status);
    }

    [Fact]
    public void Apply_DuplicatePlans_ModifiesFileOnlyOnce()
    {
        using var fixture = CreateFixture();
        var plan = fixture.CreatePlan(@"C:\New\Demo\demo.exe");

        var outcome = fixture.Service.Apply(new[] { plan, plan });

        Assert.Equal(1, outcome.RepairedCount);
        Assert.Equal(1, outcome.SkippedCount);
        Assert.Equal(1, fixture.Store.ApplyCount);
        Assert.Single(Assert.Single(fixture.Backups.LoadAll()).Items);
    }

    [Fact]
    public void Apply_WhenStoreFailsBeforeWriting_LeavesOriginalUntouched()
    {
        using var fixture = CreateFixture();
        var original = File.ReadAllBytes(fixture.LinkPath);
        var plan = fixture.CreatePlan(@"C:\New\Demo\demo.exe");
        fixture.Store.ApplyFailure = "模拟写入前失败";

        var outcome = fixture.Service.Apply(new[] { plan });

        Assert.Equal(1, outcome.FailedCount);
        Assert.Equal(original, File.ReadAllBytes(fixture.LinkPath));
        Assert.Contains("未覆盖", Assert.Single(outcome.Items).Message);
    }

    [Fact]
    public void Undo_RestoresOriginalBytesAndRecordsUndoBatch()
    {
        using var fixture = CreateFixture();
        var originalBytes = File.ReadAllBytes(fixture.LinkPath);
        var plan = fixture.CreatePlan(@"C:\New\Demo\demo.exe");
        fixture.Service.Apply(new[] { plan });

        var all = fixture.Backups.LoadAll();
        var repairBatch = Assert.Single(all);
        var undoService = new UndoService(fixture.Backups);
        var preview = undoService.Preview(repairBatch, all);

        Assert.True(preview.CanUndo);
        var outcome = undoService.Undo(repairBatch, all);

        Assert.True(outcome.Applied);
        Assert.Single(outcome.Items);
        Assert.Equal(OperationItemOutcome.Restored, outcome.Items[0].Outcome);
        Assert.True(originalBytes.SequenceEqual(File.ReadAllBytes(fixture.LinkPath)));

        var updatedHistory = fixture.Backups.LoadAll();
        Assert.Equal(2, updatedHistory.Count);
        var undoBatch = Assert.Single(updatedHistory, batch => batch.Operation == BackupOperations.Undo);
        Assert.Equal(repairBatch.Id, undoBatch.UndoesBatchId);
        Assert.Equal(BackupStatuses.Restored, undoBatch.Items[0].Status);
    }

    [Fact]
    public void Undo_RefusesWhenFileWasModifiedAfterRepair()
    {
        using var fixture = CreateFixture();
        var plan = fixture.CreatePlan(@"C:\New\Demo\demo.exe");
        fixture.Service.Apply(new[] { plan });
        File.WriteAllText(fixture.LinkPath, "later-user-change");

        var all = fixture.Backups.LoadAll();
        var repairBatch = Assert.Single(all);
        var undoService = new UndoService(fixture.Backups);
        var preview = undoService.Preview(repairBatch, all);

        Assert.False(preview.CanUndo);
        Assert.Single(preview.Conflicts);

        var outcome = undoService.Undo(repairBatch, all);

        Assert.False(outcome.Applied);
        Assert.Single(outcome.Conflicts);
        Assert.Equal("later-user-change", File.ReadAllText(fixture.LinkPath));
        Assert.Single(fixture.Backups.LoadAll());
    }

    [Fact]
    public void Undo_AlreadyUndoneBatchIsRefused()
    {
        using var fixture = CreateFixture();
        var plan = fixture.CreatePlan(@"C:\New\Demo\demo.exe");
        fixture.Service.Apply(new[] { plan });
        var all = fixture.Backups.LoadAll();
        var repairBatch = Assert.Single(all);
        var undoService = new UndoService(fixture.Backups);
        undoService.Undo(repairBatch, all);

        var afterUndo = fixture.Backups.LoadAll();
        var preview = undoService.Preview(repairBatch, afterUndo);
        var outcome = undoService.Undo(repairBatch, afterUndo);

        Assert.False(preview.CanUndo);
        Assert.False(outcome.Applied);
    }

    private static Fixture CreateFixture()
    {
        var temp = new TempDirectory();
        var linkPath = temp.CreateFile("Desktop/Demo.lnk", "original-shortcut-bytes");
        var store = new FakeShortcutStore();
        store.Links[linkPath] = new ShortcutLink
        {
            FilePath = linkPath,
            DisplayName = "Demo",
            TargetPath = @"C:\Old\Demo\demo.exe"
        };

        var probe = new FakeSystemProbe();
        probe.SetFile(@"C:\New\Demo\demo.exe");
        probe.SetDirectory(@"C:\New\Demo");

        var backups = new BackupRepository(Path.Combine(temp.Path, "BackupRoot"));
        var service = new RepairService(store, backups, probe);
        return new Fixture(temp, linkPath, store, probe, backups, service);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(
            TempDirectory temp,
            string linkPath,
            FakeShortcutStore store,
            FakeSystemProbe probe,
            BackupRepository backups,
            RepairService service)
        {
            Temp = temp;
            LinkPath = linkPath;
            Store = store;
            Probe = probe;
            Backups = backups;
            Service = service;
        }

        public TempDirectory Temp { get; }

        public string LinkPath { get; }

        public FakeShortcutStore Store { get; }

        public FakeSystemProbe Probe { get; }

        public BackupRepository Backups { get; }

        public RepairService Service { get; }

        public RepairPlan CreatePlan(string newTarget)
        {
            var link = Store.Links[LinkPath];
            var paths = PathResolver.Resolve(link);
            var diagnosis = DiagnosisResult.FromIssues(new[]
            {
                new ShortcutIssue(
                    IssueCodes.TargetMissing,
                    IssueSeverity.Error,
                    "目标不存在",
                    "测试",
                    Array.Empty<string>(),
                    IssueHandling.NeedsRelocation)
            });
            var item = new ScannedShortcut(
                link,
                paths,
                FileHash.Compute(LinkPath),
                "测试",
                diagnosis);

            return new RepairPlan(item, item.ContentHash, newTarget, null, null);
        }

        public void Dispose() => Temp.Dispose();
    }
}
