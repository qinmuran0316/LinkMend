using LinkMend.Core.Abstractions;
using LinkMend.Core.Backup;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;

namespace LinkMend.Core.Repair;

public sealed class RepairService
{
    private readonly IShortcutStore _store;
    private readonly BackupRepository _backups;
    private readonly ISystemProbe _probe;

    public RepairService(IShortcutStore store, BackupRepository backups, ISystemProbe probe)
    {
        _store = store;
        _backups = backups;
        _probe = probe;
    }

    public RepairOutcome Apply(IReadOnlyList<RepairPlan> plans)
    {
        var results = new List<RepairItemResult>();
        var preflight = new List<PreflightPlan>();

        foreach (var plan in plans)
        {
            var shortcutPath = plan.Item.Link.FilePath;
            if (!plan.HasChanges)
            {
                results.Add(Skipped(plan, "没有可应用的修改。"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(plan.ExpectedHash))
            {
                results.Add(Skipped(plan, "缺少扫描时的文件校验值，请重新扫描。"));
                continue;
            }

            byte[] originalBytes;
            string currentHash;
            try
            {
                originalBytes = FileHash.ReadAllBytesShared(shortcutPath);
                currentHash = FileHash.Compute(originalBytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                results.Add(Skipped(plan, $"无法读取快捷方式当前内容：{ex.Message}"));
                continue;
            }

            if (!string.Equals(currentHash, plan.ExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(Skipped(plan, "快捷方式在扫描后被修改。为避免覆盖新变化，请重新扫描后再修复。"));
                continue;
            }

            if (plan.TargetPath is not null
                && _probe.ProbePath(plan.TargetPath).Outcome is not (ProbeOutcome.File or ProbeOutcome.Directory))
            {
                results.Add(Skipped(plan, "新的目标路径已经不存在，请重新查找位置。"));
                continue;
            }

            if (plan.WorkingDirectory is not null
                && _probe.ProbePath(plan.WorkingDirectory).Outcome != ProbeOutcome.Directory)
            {
                results.Add(Skipped(plan, "新的起始位置不是可访问的文件夹，请重新扫描。"));
                continue;
            }

            preflight.Add(new PreflightPlan(plan, originalBytes, currentHash));
        }

        if (preflight.Count == 0)
        {
            return new RepairOutcome(null, results, Array.Empty<UndoConflict>());
        }

        var seeds = preflight.Select(entry => new BackupSeed(
            entry.Plan.Item.Link.FilePath,
            entry.Plan.Item.Link.FileName,
            entry.Bytes,
            entry.Hash,
            entry.Plan.Item.Link.TargetPath,
            entry.Plan.Item.Link.WorkingDirectory,
            FormatIcon(entry.Plan.Item.Link.IconPath, entry.Plan.Item.Link.IconIndex))).ToArray();

        var batch = _backups.CreateBatch(
            BackupOperations.Repair,
            seeds,
            note: $"LinkMend 自动修复 {seeds.Length} 项快捷方式");

        for (var index = 0; index < preflight.Count; index++)
        {
            var entry = preflight[index];
            var plan = entry.Plan;
            var backupItem = batch.Items[index];
            var shortcutPath = plan.Item.Link.FilePath;

            try
            {
                var hashBeforeWrite = FileHash.Compute(shortcutPath);
                if (!string.Equals(hashBeforeWrite, backupItem.OriginalHash, StringComparison.OrdinalIgnoreCase))
                {
                    MarkFailedAndSave(
                        batch,
                        backupItem,
                        "写入前发现快捷方式内容发生变化，未执行修改。");
                    results.Add(new RepairItemResult(
                        shortcutPath,
                        plan.FileName,
                        OperationItemOutcome.Skipped,
                        "写入前发现内容发生变化，未执行修改。"));
                    continue;
                }

                _store.Apply(shortcutPath, plan.ToPatch());

                var readBack = _store.Read(shortcutPath);
                var verificationError = Verify(plan, readBack);
                if (verificationError is not null)
                {
                    throw new ShortcutWriteException(verificationError);
                }

                var actual = readBack.Link!;
                var resultHash = FileHash.Compute(shortcutPath);

                backupItem.Status = BackupStatuses.Repaired;
                backupItem.ResultHash = resultHash;
                backupItem.AfterTarget = actual.TargetPath;
                backupItem.AfterWorkingDirectory = actual.WorkingDirectory;
                backupItem.AfterIcon = FormatIcon(actual.IconPath, actual.IconIndex);
                backupItem.Message = "已写入并重新读取验证。";
                _backups.Save(batch);

                results.Add(new RepairItemResult(
                    shortcutPath,
                    plan.FileName,
                    OperationItemOutcome.Repaired,
                    "已写入并重新读取验证。"));
            }
            catch (Exception ex)
            {
                var restoreMessage = RestoreOriginal(batch, backupItem);
                backupItem.Status = BackupStatuses.Failed;
                backupItem.Message = $"{ex.Message} {restoreMessage}";
                TrySave(batch);

                results.Add(new RepairItemResult(
                    shortcutPath,
                    plan.FileName,
                    OperationItemOutcome.Failed,
                    backupItem.Message));
            }
        }

        return new RepairOutcome(batch.Id, results, Array.Empty<UndoConflict>());
    }

    private string RestoreOriginal(BackupBatch batch, BackupItem item)
    {
        try
        {
            var original = _backups.ReadSnapshot(batch, item);
            var hash = FileHash.Compute(original);
            if (!string.Equals(hash, item.OriginalHash, StringComparison.OrdinalIgnoreCase))
            {
                return "自动恢复失败：备份文件校验不一致，请从备份目录手动恢复。";
            }

            AtomicFile.WriteAllBytes(item.ShortcutPath, original);
            var restoredHash = FileHash.Compute(item.ShortcutPath);
            if (!string.Equals(restoredHash, item.OriginalHash, StringComparison.OrdinalIgnoreCase))
            {
                return "自动恢复后校验失败，请从备份目录手动恢复。";
            }

            return "原快捷方式已自动恢复。";
        }
        catch (Exception ex)
        {
            return $"自动恢复失败：{ex.Message}。原始文件保存在 LinkMend 备份目录中。";
        }
    }

    private string? Verify(RepairPlan plan, ShortcutReadResult readBack)
    {
        if (!readBack.Success || readBack.Link is null)
        {
            return $"修复后无法重新读取快捷方式：{readBack.Failure?.Message ?? "未知错误"}";
        }

        var actual = readBack.Link;
        if (plan.TargetPath is not null
            && !PathResolver.AreSame(actual.TargetPath, plan.TargetPath))
        {
            return "重新读取后发现目标路径与预期不一致。";
        }

        if (plan.WorkingDirectory is not null
            && !PathResolver.AreSame(actual.WorkingDirectory, plan.WorkingDirectory))
        {
            return "重新读取后发现起始位置与预期不一致。";
        }

        if (plan.Icon is { } icon)
        {
            if (icon.Clear)
            {
                if (!string.IsNullOrWhiteSpace(actual.IconPath))
                {
                    return "尝试恢复默认图标后，快捷方式仍记录了自定义图标。";
                }
            }
            else if (!PathResolver.AreSame(actual.IconPath, icon.Path)
                     || actual.IconIndex != icon.Index)
            {
                return "重新读取后发现图标位置与预期不一致。";
            }
        }

        return null;
    }

    private void MarkFailedAndSave(BackupBatch batch, BackupItem item, string message)
    {
        item.Status = BackupStatuses.Failed;
        item.Message = message;
        TrySave(batch);
    }

    private void TrySave(BackupBatch batch)
    {
        try
        {
            _backups.Save(batch);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The caller still receives the operation result; disk errors are surfaced there.
        }
    }

    private static RepairItemResult Skipped(RepairPlan plan, string message) =>
        new(plan.Item.Link.FilePath, plan.FileName, OperationItemOutcome.Skipped, message);

    public static string FormatIcon(string path, int index) =>
        string.IsNullOrWhiteSpace(path) ? "" : $"{path}, {index}";

    private sealed record PreflightPlan(RepairPlan Plan, byte[] Bytes, string Hash);
}
