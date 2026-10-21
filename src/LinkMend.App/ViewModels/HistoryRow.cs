using LinkMend.Core.Backup;
using LinkMend.Core.Paths;

namespace LinkMend.App.ViewModels;

public sealed class HistoryRow
{
    public HistoryRow(BackupBatch batch, bool isUndone, int? remainingUndoCount = null)
    {
        Batch = batch;
        IsUndone = isUndone;
        RemainingUndoCount = remainingUndoCount ?? (isUndone ? 0 : batch.RepairedCount);
        Items = batch.Items.Select(item => new HistoryItemRow(item)).ToArray();
    }

    public BackupBatch Batch { get; }

    public bool IsUndone { get; }

    public int RemainingUndoCount { get; }

    public IReadOnlyList<HistoryItemRow> Items { get; }

    public string TimeText => Batch.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string OperationLabel => Batch.Operation == BackupOperations.Undo ? "撤销" : "修复";

    public string StateLabel
    {
        get
        {
            if (Batch.Operation == BackupOperations.Undo)
            {
                if (Batch.PendingCount > 0)
                {
                    return "未完成";
                }

                var restored = Batch.Items.Count(item => item.Status == BackupStatuses.Restored);
                if (restored > 0 && Batch.FailedCount == 0)
                {
                    return "撤销完成";
                }

                return restored > 0 ? "部分恢复" : "撤销失败";
            }

            if (IsUndone)
            {
                return "已撤销";
            }

            if (Batch.PendingCount > 0)
            {
                return "未完成";
            }

            if (RemainingUndoCount > 0 && RemainingUndoCount < Batch.RepairedCount)
            {
                return "部分撤销";
            }

            if (Batch.RepairedCount == 0)
            {
                return "失败";
            }

            return Batch.FailedCount > 0 ? "部分成功" : "可撤销";
        }
    }

    public bool CanUndo =>
        Batch.Operation == BackupOperations.Repair
        && !IsUndone
        && Batch.PendingCount == 0
        && RemainingUndoCount > 0;

    public string Summary
    {
        get
        {
            if (Batch.Operation == BackupOperations.Undo)
            {
                var restored = Batch.Items.Count(item => item.Status == BackupStatuses.Restored);
                return $"{Batch.Items.Count} 个快捷方式 · 已恢复 {restored} · 失败 {Batch.FailedCount}";
            }

            return $"{Batch.Items.Count} 个快捷方式 · 成功 {Batch.RepairedCount} · 失败 {Batch.FailedCount} · 待撤销 {RemainingUndoCount}";
        }
    }

    public string BatchId => Batch.Id;

    public string DetailHeading => $"{(Batch.Operation == BackupOperations.Undo ? "撤销批次" : "修复批次")} · {TimeText}";

    public string UndoHint
    {
        get
        {
            if (Batch.Operation == BackupOperations.Undo)
            {
                return "这是一条撤销记录，用于审计和查看恢复详情，不能再次撤销。";
            }

            if (CanUndo)
            {
                return $"撤销会恢复剩余 {RemainingUndoCount} 个快捷方式到此批次修复前的状态。";
            }

            return StateLabel switch
            {
                "已撤销" => "此批次已经撤销或已被后续撤销记录覆盖。",
                "未完成" => "此批次仍有未完成的条目，请先检查详情。",
                "失败" => "此批次没有成功修复项，无法撤销。",
                "部分成功" => "部分条目已成功修复，可能仍可撤销成功项。",
                _ => "当前记录不可撤销。"
            };
        }
    }

    public string Note => string.IsNullOrWhiteSpace(Batch.Note)
        ? "无备注"
        : Batch.Note!;
}

public sealed class HistoryItemRow
{
    public HistoryItemRow(BackupItem item)
    {
        Item = item;
        Changes = BuildChanges(item);
    }

    public BackupItem Item { get; }

    public IReadOnlyList<string> Changes { get; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Item.FileName) ? Path.GetFileName(Item.ShortcutPath) : Item.FileName;

    public string ShortcutPath => Item.ShortcutPath;

    public string StatusLabel => Item.Status switch
    {
        BackupStatuses.Pending => "未完成",
        BackupStatuses.Repaired => "已修复",
        BackupStatuses.Restored => "已恢复",
        BackupStatuses.Failed => "失败",
        _ => Item.Status
    };

    public string Message => string.IsNullOrWhiteSpace(Item.Message) ? "无详细信息" : Item.Message!;

    public bool HasChanges => Changes.Count > 0;

    private static IReadOnlyList<string> BuildChanges(BackupItem item)
    {
        var changes = new List<string>();

        if (item.AfterTarget is not null
            && !PathResolver.AreSame(item.BeforeTarget, item.AfterTarget))
        {
            changes.Add($"目标：{Display(item.BeforeTarget)} → {item.AfterTarget}");
        }

        if (item.AfterWorkingDirectory is not null
            && !PathResolver.AreSame(item.BeforeWorkingDirectory, item.AfterWorkingDirectory))
        {
            changes.Add($"起始位置：{Display(item.BeforeWorkingDirectory)} → {Display(item.AfterWorkingDirectory)}");
        }

        if (item.AfterIcon is not null
            && !string.Equals(item.BeforeIcon, item.AfterIcon, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"图标：{Display(item.BeforeIcon)} → {Display(item.AfterIcon)}");
        }

        return changes;
    }

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "默认" : value;
}
