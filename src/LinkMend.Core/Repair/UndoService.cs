using LinkMend.Core.Backup;
using LinkMend.Core.Paths;

namespace LinkMend.Core.Repair;

public sealed record UndoPreview(
    IReadOnlyList<BackupItem> UndoableItems,
    IReadOnlyList<UndoConflict> Conflicts,
    string Summary)
{
    public bool CanUndo => UndoableItems.Count > 0 && Conflicts.Count == 0;
}

public sealed class UndoService
{
    private readonly BackupRepository _backups;

    public UndoService(BackupRepository backups)
    {
        _backups = backups;
    }

    public UndoPreview Preview(BackupBatch batch, IReadOnlyList<BackupBatch> allBatches)
    {
        var undoable = new List<BackupItem>();
        var conflicts = new List<UndoConflict>();

        if (!BackupRepository.IsValidBatch(batch) || batch.Operation != BackupOperations.Repair)
        {
            return new UndoPreview(
                Array.Empty<BackupItem>(),
                new[] { new UndoConflict("", "", "修复记录无效，不能安全撤销。") },
                "这个批次不是有效的修复记录，不能撤销。");
        }

        if (_backups.HasUndoBatch(allBatches, batch.Id))
        {
            return new UndoPreview(
                Array.Empty<BackupItem>(),
                new[] { new UndoConflict("", "", "此批次已经执行过撤销。") },
                "此批次已经执行过撤销。");
        }

        foreach (var item in _backups.GetRemainingUndoItems(batch, allBatches))
        {
            if (!File.Exists(item.ShortcutPath))
            {
                conflicts.Add(new UndoConflict(item.ShortcutPath, item.FileName, "快捷方式已被删除或移动。"));
                continue;
            }

            string currentHash;
            try
            {
                currentHash = FileHash.Compute(item.ShortcutPath);
                _backups.ReadSnapshot(batch, item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
            {
                conflicts.Add(new UndoConflict(item.ShortcutPath, item.FileName, $"无法校验当前文件或备份快照：{ex.Message}"));
                continue;
            }

            if (!string.Equals(currentHash, item.ResultHash, StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(new UndoConflict(
                    item.ShortcutPath,
                    item.FileName,
                    "快捷方式在修复后又被其他程序修改。为保留这些新变化，撤销已停止。"));
                continue;
            }

            undoable.Add(item);
        }

        var summary = conflicts.Count > 0
            ? $"检测到 {conflicts.Count} 个冲突，未修改任何文件。请先核对下面列出的快捷方式。"
            : undoable.Count == 0
                ? "没有可撤销的成功修复项。"
                : $"将恢复 {undoable.Count} 个快捷方式到此批次修复前的状态。";

        return new UndoPreview(undoable, conflicts, summary);
    }

    public UndoOutcome Undo(BackupBatch batch, IReadOnlyList<BackupBatch> allBatches)
    {
        var preview = Preview(batch, allBatches);
        if (preview.Conflicts.Count > 0)
        {
            return new UndoOutcome(
                null,
                false,
                Array.Empty<RepairItemResult>(),
                preview.Conflicts,
                preview.Summary);
        }

        if (preview.UndoableItems.Count == 0)
        {
            return new UndoOutcome(
                null,
                false,
                Array.Empty<RepairItemResult>(),
                Array.Empty<UndoConflict>(),
                preview.Summary);
        }

        var seeds = new List<BackupSeed>();
        var seedItems = new List<BackupItem>();
        var originalSnapshots = new List<byte[]>();
        var preflightConflicts = new List<UndoConflict>();

        foreach (var item in preview.UndoableItems)
        {
            try
            {
                var bytes = FileHash.ReadAllBytesShared(item.ShortcutPath);
                var hash = FileHash.Compute(bytes);
                if (!string.Equals(hash, item.ResultHash, StringComparison.OrdinalIgnoreCase))
                {
                    preflightConflicts.Add(new UndoConflict(
                        item.ShortcutPath,
                        item.FileName,
                        "快捷方式在撤销开始前发生了变化。"));
                    continue;
                }

                var originalBytes = _backups.ReadSnapshot(batch, item);

                seeds.Add(new BackupSeed(
                    item.ShortcutPath,
                    item.FileName,
                    bytes,
                    hash,
                    item.AfterTarget ?? "",
                    item.AfterWorkingDirectory ?? "",
                    item.AfterIcon ?? ""));
                seedItems.Add(item);
                originalSnapshots.Add(originalBytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
            {
                preflightConflicts.Add(new UndoConflict(
                    item.ShortcutPath,
                    item.FileName,
                    $"无法校验当前文件或备份快照：{ex.Message}"));
            }
        }

        if (preflightConflicts.Count > 0)
        {
            return new UndoOutcome(
                null,
                false,
                Array.Empty<RepairItemResult>(),
                preflightConflicts,
                "准备撤销时检测到变化，未修改任何文件。请重新打开修复历史后再试。");
        }

        var undoBatch = _backups.CreateBatch(
            BackupOperations.Undo,
            seeds,
            undoesBatchId: batch.Id,
            note: $"撤销修复批次 {batch.Id}");

        var results = new List<RepairItemResult>();

        // Backing up a large batch takes time. Recheck every destination before
        // the first restore, so a late preflight conflict leaves all files alone.
        foreach (var item in seedItems)
        {
            try
            {
                FileHash.EnsureMatches(item.ShortcutPath, item.ResultHash!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                preflightConflicts.Add(new UndoConflict(item.ShortcutPath, item.FileName, ex.Message));
            }
        }

        if (preflightConflicts.Count > 0)
        {
            foreach (var item in undoBatch.Items)
            {
                item.Status = BackupStatuses.Failed;
                item.Message = "撤销前发现文件变化，整批未执行修改。";
            }

            TrySave(undoBatch);
            return new UndoOutcome(undoBatch.Id, false, results, preflightConflicts,
                "撤销前检测到文件变化，未修改任何文件。");
        }

        for (var index = 0; index < seedItems.Count; index++)
        {
            var originalItem = seedItems[index];
            var undoItem = undoBatch.Items[index];
            string? writtenHash = null;

            try
            {
                var originalBytes = originalSnapshots[index];
                AtomicFile.WriteAllBytes(originalItem.ShortcutPath, originalBytes, originalItem.ResultHash);
                writtenHash = originalItem.OriginalHash;
                var restoredHash = FileHash.Compute(originalItem.ShortcutPath);
                if (!string.Equals(restoredHash, originalItem.OriginalHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("恢复后的文件校验不一致。");
                }

                undoItem.Status = BackupStatuses.Restored;
                undoItem.ResultHash = restoredHash;
                undoItem.AfterTarget = originalItem.BeforeTarget;
                undoItem.AfterWorkingDirectory = originalItem.BeforeWorkingDirectory;
                undoItem.AfterIcon = originalItem.BeforeIcon;
                undoItem.Message = "已从备份恢复并重新校验。";
                _backups.Save(undoBatch);

                results.Add(new RepairItemResult(
                    originalItem.ShortcutPath,
                    originalItem.FileName,
                    OperationItemOutcome.Restored,
                    "已从备份恢复并重新校验。"));
            }
            catch (Exception ex)
            {
                var rollbackMessage = writtenHash is null
                    ? "未写入此快捷方式。"
                    : TryRollback(undoBatch, undoItem, writtenHash);
                undoItem.Status = BackupStatuses.Failed;
                undoItem.ResultHash = null;
                undoItem.Message = $"撤销失败：{ex.Message} {rollbackMessage}";
                TrySave(undoBatch);

                results.Add(new RepairItemResult(
                    originalItem.ShortcutPath,
                    originalItem.FileName,
                    OperationItemOutcome.Failed,
                    undoItem.Message));
            }
        }

        var failed = results.Count(result => result.Outcome == OperationItemOutcome.Failed);
        var message = failed == 0
            ? $"已撤销 {results.Count} 项快捷方式修改。"
            : $"撤销完成，其中 {failed} 项失败。未恢复的文件保留在备份中。";

        return new UndoOutcome(
            undoBatch.Id,
            true,
            results,
            Array.Empty<UndoConflict>(),
            message);
    }

    private string TryRollback(BackupBatch undoBatch, BackupItem undoItem, string writtenHash)
    {
        try
        {
            var beforeUndo = _backups.ReadSnapshot(undoBatch, undoItem);
            var expectedHash = undoItem.OriginalHash;
            if (!string.Equals(FileHash.Compute(beforeUndo), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return "撤销失败，且无法校验撤销前快照。";
            }

            AtomicFile.WriteAllBytes(undoItem.ShortcutPath, beforeUndo, writtenHash);
            var hash = FileHash.Compute(undoItem.ShortcutPath);
            return string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase)
                ? "已恢复撤销前的修复状态。"
                : "已尝试恢复撤销前状态，但校验失败，请从备份目录手动处理。";
        }
        catch (Exception ex)
        {
            return $"恢复撤销前状态失败：{ex.Message}。";
        }
    }

    private void TrySave(BackupBatch batch)
    {
        try
        {
            _backups.Save(batch);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The item result still explains the failure to the user.
        }
    }
}
