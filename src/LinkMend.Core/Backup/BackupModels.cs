using System.Text.Json.Serialization;

namespace LinkMend.Core.Backup;

public static class BackupOperations
{
    public const string Repair = "repair";
    public const string Undo = "undo";
}

public static class BackupStatuses
{
    public const string Pending = "pending";
    public const string Repaired = "repaired";
    public const string Restored = "restored";
    public const string Failed = "failed";
}

public sealed class BackupBatch
{
    public string Id { get; set; } = "";

    public string Operation { get; set; } = BackupOperations.Repair;

    public DateTimeOffset CreatedAt { get; set; }

    public string? UndoesBatchId { get; set; }

    public string? Note { get; set; }

    public List<BackupItem> Items { get; set; } = new();

    [JsonIgnore]
    public int RepairedCount => Items.Count(item => item.Status == BackupStatuses.Repaired);

    [JsonIgnore]
    public int FailedCount => Items.Count(item => item.Status == BackupStatuses.Failed);

    [JsonIgnore]
    public int PendingCount => Items.Count(item => item.Status == BackupStatuses.Pending);

    [JsonIgnore]
    public bool HasRepairableSnapshot =>
        Operation == BackupOperations.Repair
        && Items.Any(item => item.CanUndo);
}

public sealed class BackupItem
{
    public string ShortcutPath { get; set; } = "";

    public string FileName { get; set; } = "";

    public string SnapshotFile { get; set; } = "";

    public string OriginalHash { get; set; } = "";

    public string? ResultHash { get; set; }

    public string BeforeTarget { get; set; } = "";

    public string? AfterTarget { get; set; }

    public string BeforeWorkingDirectory { get; set; } = "";

    public string? AfterWorkingDirectory { get; set; }

    public string BeforeIcon { get; set; } = "";

    public string? AfterIcon { get; set; }

    public string Status { get; set; } = BackupStatuses.Pending;

    public string? Message { get; set; }

    [JsonIgnore]
    public bool CanUndo =>
        Status == BackupStatuses.Repaired
        && !string.IsNullOrWhiteSpace(ResultHash);
}

public sealed record BackupSeed(
    string ShortcutPath,
    string FileName,
    byte[] FileBytes,
    string OriginalHash,
    string BeforeTarget,
    string BeforeWorkingDirectory,
    string BeforeIcon);

public sealed record UndoConflict(string ShortcutPath, string FileName, string Reason);
