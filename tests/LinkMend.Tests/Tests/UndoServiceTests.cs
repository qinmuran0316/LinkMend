using LinkMend.Core.Backup;
using LinkMend.Core.Paths;
using LinkMend.Core.Repair;
using LinkMend.Tests.Support;

namespace LinkMend.Tests.Tests;

public sealed class UndoServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Undo_WhenFileChangesAfterPreflight_PreservesNewState(bool deleteFile)
    {
        using var temp = new TempDirectory();
        var time = new CallbackTimeProvider();
        var repository = new BackupRepository(temp.Combine("BackupRoot"), time);
        var batch = CreateRepairedBatch(temp, repository, "First", "Second");
        var first = batch.Items[0];
        var second = batch.Items[1];
        var firstBytes = File.ReadAllBytes(first.ShortcutPath);
        time.OnGetUtcNow = () =>
        {
            if (deleteFile)
            {
                File.Delete(second.ShortcutPath);
            }
            else
            {
                File.WriteAllText(second.ShortcutPath, "later-user-change");
            }
        };

        var outcome = new UndoService(repository).Undo(batch, repository.LoadAll());

        Assert.Equal(firstBytes, File.ReadAllBytes(first.ShortcutPath));
        if (deleteFile)
        {
            Assert.False(File.Exists(second.ShortcutPath));
        }
        else
        {
            Assert.Equal("later-user-change", File.ReadAllText(second.ShortcutPath));
        }

        Assert.False(outcome.Applied);
        Assert.NotEmpty(outcome.Conflicts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Undo_WhenLaterSnapshotIsDamaged_LeavesEntireBatchUntouched(bool deleteSnapshot)
    {
        using var temp = new TempDirectory();
        var repository = new BackupRepository(temp.Combine("BackupRoot"));
        var batch = CreateRepairedBatch(temp, repository, "First", "Second");
        var currentBytes = batch.Items.Select(item => File.ReadAllBytes(item.ShortcutPath)).ToArray();
        var snapshot = repository.GetSnapshotPath(batch, batch.Items[1]);
        if (deleteSnapshot)
        {
            File.Delete(snapshot);
        }
        else
        {
            File.WriteAllText(snapshot, "damaged-backup");
        }

        var service = new UndoService(repository);
        Assert.False(service.Preview(batch, repository.LoadAll()).CanUndo);
        var outcome = service.Undo(batch, repository.LoadAll());

        Assert.False(outcome.Applied);
        Assert.NotEmpty(outcome.Conflicts);
        for (var index = 0; index < batch.Items.Count; index++)
        {
            Assert.Equal(currentBytes[index], File.ReadAllBytes(batch.Items[index].ShortcutPath));
        }
    }

    [Fact]
    public void Undo_AfterPartialUndo_RetriesOnlyRemainingItems()
    {
        using var temp = new TempDirectory();
        var repository = new BackupRepository(temp.Combine("BackupRoot"));
        var batch = CreateRepairedBatch(temp, repository, "First", "Second");
        var partialUndo = repository.CreateBatch(
            BackupOperations.Undo,
            batch.Items.Select(item => new BackupSeed(
                item.ShortcutPath, item.FileName, File.ReadAllBytes(item.ShortcutPath),
                item.ResultHash!, "", "", "")),
            undoesBatchId: batch.Id);
        File.WriteAllBytes(batch.Items[0].ShortcutPath, repository.ReadSnapshot(batch, batch.Items[0]));
        partialUndo.Items[0].Status = BackupStatuses.Restored;
        partialUndo.Items[0].ResultHash = batch.Items[0].OriginalHash;
        partialUndo.Items[1].Status = BackupStatuses.Failed;
        repository.Save(partialUndo);

        var history = repository.LoadAll();
        Assert.False(repository.HasUndoBatch(history, batch.Id));
        var service = new UndoService(repository);
        var preview = service.Preview(batch, history);
        Assert.True(preview.CanUndo);
        Assert.Equal(batch.Items[1].ShortcutPath, Assert.Single(preview.UndoableItems).ShortcutPath);

        var outcome = service.Undo(batch, history);

        Assert.True(outcome.Applied);
        Assert.Equal(OperationItemOutcome.Restored, Assert.Single(outcome.Items).Outcome);
        Assert.True(repository.HasUndoBatch(repository.LoadAll(), batch.Id));
        foreach (var item in batch.Items)
        {
            Assert.Equal(item.OriginalHash, FileHash.Compute(item.ShortcutPath));
        }
    }

    private static BackupBatch CreateRepairedBatch(
        TempDirectory temp, BackupRepository repository, params string[] names)
    {
        var seeds = names.Select(name =>
        {
            var path = temp.CreateFile($"Desktop/{name}.lnk", $"original-{name}");
            var bytes = File.ReadAllBytes(path);
            return new BackupSeed(path, Path.GetFileName(path), bytes, FileHash.Compute(bytes), "", "", "");
        }).ToArray();
        var batch = repository.CreateBatch(BackupOperations.Repair, seeds);
        foreach (var item in batch.Items)
        {
            File.WriteAllText(item.ShortcutPath, $"repaired-{item.FileName}");
            item.Status = BackupStatuses.Repaired;
            item.ResultHash = FileHash.Compute(item.ShortcutPath);
        }

        repository.Save(batch);
        return batch;
    }
}
