using System.Globalization;
using System.Text.Json;
using LinkMend.Core.Paths;

namespace LinkMend.Core.Backup;

public sealed class BackupRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly TimeProvider _timeProvider;

    public BackupRepository(string rootDirectory, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("备份目录不能为空。", nameof(rootDirectory));
        }

        RootDirectory = Path.GetFullPath(rootDirectory);
        _timeProvider = timeProvider ?? TimeProvider.System;
        HistoryDirectory = Path.Combine(RootDirectory, "History");
        SnapshotsDirectory = Path.Combine(RootDirectory, "Backups");
        Directory.CreateDirectory(HistoryDirectory);
        Directory.CreateDirectory(SnapshotsDirectory);
    }

    public string RootDirectory { get; }

    public string HistoryDirectory { get; }

    public string SnapshotsDirectory { get; }

    public static string GetDefaultRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinkMend");

    public BackupBatch CreateBatch(
        string operation,
        IEnumerable<BackupSeed> seeds,
        string? undoesBatchId = null,
        string? note = null)
    {
        var seedList = seeds.ToArray();
        if (seedList.Length == 0)
        {
            throw new InvalidOperationException("备份批次至少需要一个文件。");
        }

        if (operation is not (BackupOperations.Repair or BackupOperations.Undo)
            || (operation == BackupOperations.Undo && !IsValidBatchId(undoesBatchId)))
        {
            throw new InvalidOperationException("无效的备份操作或撤销批次编号。");
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in seedList)
        {
            if (!PathResolver.IsShortcutFilePath(seed.ShortcutPath)
                || !string.Equals(seed.FileName, Path.GetFileName(seed.ShortcutPath), StringComparison.OrdinalIgnoreCase)
                || !paths.Add(PathResolver.NormalizeKey(seed.ShortcutPath))
                || !string.Equals(FileHash.Compute(seed.FileBytes), seed.OriginalHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"备份路径重复、无效或内容校验失败：{seed.ShortcutPath}。");
            }
        }

        var now = _timeProvider.GetLocalNow();
        var id = CreateBatchId(now);
        var snapshotDirectory = Path.Combine(SnapshotsDirectory, id);
        Directory.CreateDirectory(snapshotDirectory);

        var batch = new BackupBatch
        {
            Id = id,
            Operation = operation,
            CreatedAt = now,
            UndoesBatchId = undoesBatchId,
            Note = note
        };

        for (var index = 0; index < seedList.Length; index++)
        {
            var seed = seedList[index];
            var snapshotFile = $"item-{index + 1:D3}.bak";
            AtomicFile.WriteAllBytes(Path.Combine(snapshotDirectory, snapshotFile), seed.FileBytes);

            batch.Items.Add(new BackupItem
            {
                ShortcutPath = seed.ShortcutPath,
                FileName = seed.FileName,
                SnapshotFile = snapshotFile,
                OriginalHash = seed.OriginalHash,
                BeforeTarget = seed.BeforeTarget,
                BeforeWorkingDirectory = seed.BeforeWorkingDirectory,
                BeforeIcon = seed.BeforeIcon,
                Status = BackupStatuses.Pending
            });
        }

        Save(batch);
        return batch;
    }

    public void Save(BackupBatch batch)
    {
        if (!IsValidBatch(batch))
        {
            throw new InvalidOperationException("备份记录包含无效的字段、路径或校验值。");
        }

        var path = GetHistoryPath(batch.Id);
        var json = JsonSerializer.SerializeToUtf8Bytes(batch, JsonOptions);
        AtomicFile.WriteAllBytes(path, json);
    }

    public IReadOnlyList<BackupBatch> LoadAll()
    {
        var results = new List<BackupBatch>();
        if (!Directory.Exists(HistoryDirectory))
        {
            return results;
        }

        foreach (var path in Directory.EnumerateFiles(HistoryDirectory, "*.json"))
        {
            try
            {
                var batch = JsonSerializer.Deserialize<BackupBatch>(
                    ReadBytesWithoutBom(path),
                    JsonOptions);
                if (IsValidBatch(batch)
                    && string.Equals(batch!.Id, Path.GetFileNameWithoutExtension(path), StringComparison.Ordinal))
                {
                    results.Add(batch!);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // A damaged historical record must not prevent the app from starting.
            }
        }

        return results
            .OrderByDescending(batch => batch.CreatedAt)
            .ToArray();
    }

    public BackupBatch? Load(string id)
    {
        if (!IsValidBatchId(id))
        {
            return null;
        }

        var path = GetHistoryPath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var batch = JsonSerializer.Deserialize<BackupBatch>(ReadBytesWithoutBom(path), JsonOptions);
            return IsValidBatch(batch) && string.Equals(batch!.Id, id, StringComparison.Ordinal) ? batch : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static byte[] ReadBytesWithoutBom(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;
    }

    public byte[] ReadSnapshot(BackupBatch batch, BackupItem item)
    {
        var path = GetSnapshotPath(batch, item);
        if ((File.GetAttributes(Path.GetDirectoryName(path)!) & FileAttributes.ReparsePoint) != 0
            || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("备份快照不能是重解析点。");
        }

        var bytes = File.ReadAllBytes(path);
        if (!string.Equals(FileHash.Compute(bytes), item.OriginalHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("备份快照校验失败，无法安全恢复。");
        }

        return bytes;
    }

    public string GetSnapshotPath(BackupBatch batch, BackupItem item)
    {
        if (!IsValidBatchId(batch.Id) || !IsValidSnapshotFile(item.SnapshotFile))
        {
            throw new InvalidOperationException("备份记录包含无效的文件路径。");
        }

        return Path.Combine(SnapshotsDirectory, batch.Id, item.SnapshotFile);
    }

    public IReadOnlyList<BackupItem> GetRemainingUndoItems(BackupBatch repairBatch, IReadOnlyList<BackupBatch> batches)
    {
        var restored = batches
            .Where(batch => batch.Operation == BackupOperations.Undo
                            && string.Equals(batch.UndoesBatchId, repairBatch.Id, StringComparison.Ordinal))
            .SelectMany(batch => batch.Items)
            .Where(item => item.Status == BackupStatuses.Restored)
            .ToArray();

        return repairBatch.Items
            .Where(item => item.CanUndo && !restored.Any(undoItem =>
                PathResolver.AreSame(undoItem.ShortcutPath, item.ShortcutPath)
                && string.Equals(undoItem.OriginalHash, item.ResultHash, StringComparison.OrdinalIgnoreCase)
                && string.Equals(undoItem.ResultHash, item.OriginalHash, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public bool HasUndoBatch(IReadOnlyList<BackupBatch> batches, string batchId)
    {
        var repairBatch = batches.FirstOrDefault(batch =>
            batch.Operation == BackupOperations.Repair && batch.Id == batchId);
        return repairBatch is { HasRepairableSnapshot: true }
               && GetRemainingUndoItems(repairBatch, batches).Count == 0;
    }

    public static bool IsValidBatch(BackupBatch? batch)
    {
        if (batch is null || !IsValidBatchId(batch.Id)
            || batch.Operation is not (BackupOperations.Repair or BackupOperations.Undo)
            || (batch.Operation == BackupOperations.Undo
                && (!IsValidBatchId(batch.UndoesBatchId) || batch.UndoesBatchId == batch.Id))
            || batch.Items is not { Count: > 0 })
        {
            return false;
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        var snapshots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in batch.Items)
        {
            if (item is null || !PathResolver.IsShortcutFilePath(item.ShortcutPath)
                || !string.Equals(item.FileName, Path.GetFileName(item.ShortcutPath), StringComparison.OrdinalIgnoreCase)
                || !IsValidSnapshotFile(item.SnapshotFile)
                || !FileHash.IsValid(item.OriginalHash)
                || !paths.Add(PathResolver.NormalizeKey(item.ShortcutPath))
                || !snapshots.Add(item.SnapshotFile)
                || item.Status is not (BackupStatuses.Pending or BackupStatuses.Failed or BackupStatuses.Repaired or BackupStatuses.Restored)
                || (item.Status == BackupStatuses.Repaired && batch.Operation != BackupOperations.Repair)
                || (item.Status == BackupStatuses.Restored && batch.Operation != BackupOperations.Undo)
                || (item.Status is BackupStatuses.Repaired or BackupStatuses.Restored && !FileHash.IsValid(item.ResultHash)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidSnapshotFile(string? name) =>
        name is { Length: >= 12 }
        && name.StartsWith("item-", StringComparison.Ordinal)
        && name.EndsWith(".bak", StringComparison.Ordinal)
        && name[5..^4].All(char.IsAsciiDigit);

    private string CreateBatchId(DateTimeOffset now)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var id = string.Create(
                CultureInfo.InvariantCulture,
                $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..12]}");
            if (!File.Exists(GetHistoryPath(id)) && !Directory.Exists(Path.Combine(SnapshotsDirectory, id)))
            {
                return id;
            }
        }

        throw new IOException("无法生成唯一的备份批次编号。");
    }

    private string GetHistoryPath(string id)
    {
        if (!IsValidBatchId(id))
        {
            throw new ArgumentException("无效的备份批次编号。", nameof(id));
        }

        return Path.Combine(HistoryDirectory, $"{id}.json");
    }

    private static bool IsValidBatchId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length <= 64
        && id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}
