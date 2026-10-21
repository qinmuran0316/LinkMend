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
    public void LoadAll_IgnoresNullItemsAndMismatchedRecordIds()
    {
        using var temp = new TempDirectory();
        var repository = new BackupRepository(temp.Path);
        File.WriteAllText(Path.Combine(repository.HistoryDirectory, "null-items.json"),
            "{\"id\":\"null-items\",\"items\":null}");
        File.WriteAllText(Path.Combine(repository.HistoryDirectory, "null-item.json"),
            "{\"id\":\"null-item\",\"items\":[null]}");
        File.WriteAllText(Path.Combine(repository.HistoryDirectory, "different-id.json"),
            "{\"id\":\"other-id\",\"items\":[]}");

        Assert.Empty(repository.LoadAll());
        Assert.Null(repository.Load("null-items"));
        Assert.Null(repository.Load("null-item"));
        Assert.Null(repository.Load("different-id"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("item-001.bak:stream")]
    [InlineData("item-001.bak ")]
    public void GetSnapshotPath_RejectsSpecialWindowsNames(string name)
    {
        using var temp = new TempDirectory();
        var repository = new BackupRepository(temp.Path);
        var batch = new BackupBatch { Id = "20261022-120000-ABCD" };

        Assert.Throws<InvalidOperationException>(() =>
            repository.GetSnapshotPath(batch, new BackupItem { SnapshotFile = name }));
    }

    [Fact]
    public void HasUndoBatch_OnlyCountsBatchesThatActuallyRestoredSomething()
    {
        using var temp = new TempDirectory();
        var repository = new BackupRepository(temp.Path);
        var bytes = Encoding.UTF8.GetBytes("original");
        var repaired = Encoding.UTF8.GetBytes("repaired");
        var seed = new BackupSeed(@"C:\Links\Demo.lnk", "Demo.lnk", bytes, FileHash.Compute(bytes), "", "", "");
        var repair = repository.CreateBatch(BackupOperations.Repair, new[] { seed });
        repair.Items[0].Status = BackupStatuses.Repaired;
        repair.Items[0].ResultHash = FileHash.Compute(repaired);
        repository.Save(repair);
        var failedUndo = repository.CreateBatch(BackupOperations.Undo,
            new[] { seed with { FileBytes = repaired, OriginalHash = FileHash.Compute(repaired) } },
            undoesBatchId: repair.Id);
        failedUndo.Items[0].Status = BackupStatuses.Failed;
        repository.Save(failedUndo);

        Assert.False(repository.HasUndoBatch(repository.LoadAll(), repair.Id));

        failedUndo.Items[0].Status = BackupStatuses.Restored;
        failedUndo.Items[0].ResultHash = repair.Items[0].OriginalHash;
        repository.Save(failedUndo);

        Assert.True(repository.HasUndoBatch(repository.LoadAll(), repair.Id));
    }
}
