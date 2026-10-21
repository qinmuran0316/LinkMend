using System.Text;
using LinkMend.Core.Backup;
using LinkMend.Core.Paths;
using LinkMend.Tests.Support;

namespace LinkMend.Tests.Tests;

public sealed class BackupRepositoryTests
{
    [Fact]
    public void CreateBatch_WritesSnapshotAndHistory()
    {
        using var temp = new TempDirectory();
        var repository = new BackupRepository(temp.Path);
        var bytes = Encoding.UTF8.GetBytes("original shortcut");
        var seed = new BackupSeed(
            @"C:\Links\Demo.lnk",
            "Demo.lnk",
            bytes,
            FileHash.Compute(bytes),
            @"C:\Old\demo.exe",
            @"C:\Old",
            "");

        var batch = repository.CreateBatch(BackupOperations.Repair, new[] { seed }, note: "测试批次");

        Assert.NotEmpty(batch.Id);
        var loaded = Assert.Single(repository.LoadAll());
        Assert.Equal(batch.Id, loaded.Id);
        Assert.Equal("测试批次", loaded.Note);
        Assert.Equal(BackupStatuses.Pending, loaded.Items[0].Status);
        Assert.True(bytes.SequenceEqual(repository.ReadSnapshot(loaded, loaded.Items[0])));
    }

    [Fact]
    public void CreateBatch_RejectsMismatchedHash()
    {
        using var temp = new TempDirectory();
        var repository = new BackupRepository(temp.Path);
        var seed = new BackupSeed(
            @"C:\Links\Demo.lnk",
            "Demo.lnk",
            Encoding.UTF8.GetBytes("original"),
            "not-the-real-hash",
            "",
            "",
            "");

        Assert.Throws<InvalidOperationException>(
            () => repository.CreateBatch(BackupOperations.Repair, new[] { seed }));
    }

    [Fact]
    public void GetSnapshotPath_RejectsPathTraversal()
    {
        var repository = new BackupRepository(Path.Combine(Path.GetTempPath(), "LinkMend.Tests", Guid.NewGuid().ToString("N")));
        var batch = new BackupBatch { Id = "20261022-120000-ABCD" };
        var item = new BackupItem { SnapshotFile = "../outside.bak" };

        Assert.Throws<InvalidOperationException>(() => repository.GetSnapshotPath(batch, item));
        Assert.Null(repository.Load("../outside"));
    }

    [Fact]
    public void LoadAll_AcceptsHistoryJsonWithUtf8Bom()
    {
        using var temp = new TempDirectory();
        var repository = new BackupRepository(temp.Path);
        var bytes = Encoding.UTF8.GetBytes("original");
        var seed = new BackupSeed(
            @"C:\Links\Demo.lnk",
            "Demo.lnk",
            bytes,
            FileHash.Compute(bytes),
            "",
            "",
            "");
        repository.CreateBatch(BackupOperations.Repair, new[] { seed });

        var historyFile = Directory.EnumerateFiles(repository.HistoryDirectory, "*.json").Single();
        var json = File.ReadAllBytes(historyFile);
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(json).ToArray();
        File.WriteAllBytes(historyFile, withBom);

        Assert.Single(repository.LoadAll());
    }

    [Fact]
    public void HasUndoBatch_OnlyCountsBatchesThatActuallyRestoredSomething()
    {
        using var temp = new TempDirectory();
        var repository = new BackupRepository(temp.Path);
        var failedUndo = new BackupBatch
        {
            Id = "20261022-120000-AAAA",
            Operation = BackupOperations.Undo,
            UndoesBatchId = "20261022-110000-BBBB",
            Items = new List<BackupItem>
            {
                new() { ShortcutPath = @"C:\Links\Demo.lnk", Status = BackupStatuses.Failed }
            }
        };
        repository.Save(failedUndo);

        Assert.False(repository.HasUndoBatch(repository.LoadAll(), "20261022-110000-BBBB"));

        failedUndo.Items[0].Status = BackupStatuses.Restored;
        repository.Save(failedUndo);

        Assert.True(repository.HasUndoBatch(repository.LoadAll(), "20261022-110000-BBBB"));
    }
}
