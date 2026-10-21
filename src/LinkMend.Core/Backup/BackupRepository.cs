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
            var hash = FileHash.Compute(seed.FileBytes);
            if (!string.Equals(hash, seed.OriginalHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"备份内容校验失败：{seed.ShortcutPath}。原文件可能在读取后发生了变化。");
            }

            var snapshotFile = $"item-{index + 1:D3}.bak";
            File.WriteAllBytes(Path.Combine(snapshotDirectory, snapshotFile), seed.FileBytes);

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
                if (batch is not null && !string.IsNullOrWhiteSpace(batch.Id))
                {
                    results.Add(batch);
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
            return JsonSerializer.Deserialize<BackupBatch>(ReadBytesWithoutBom(path), JsonOptions);
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
        return File.ReadAllBytes(path);
    }

    public string GetSnapshotPath(BackupBatch batch, BackupItem item)
    {
        if (!IsValidBatchId(batch.Id) || string.IsNullOrWhiteSpace(item.SnapshotFile)
            || !string.Equals(item.SnapshotFile, Path.GetFileName(item.SnapshotFile), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("备份记录包含无效的文件路径。");
        }

        return Path.Combine(SnapshotsDirectory, batch.Id, item.SnapshotFile);
    }

    public bool HasUndoBatch(IReadOnlyList<BackupBatch> batches, string batchId) =>
        batches.Any(batch =>
            batch.Operation == BackupOperations.Undo
            && string.Equals(batch.UndoesBatchId, batchId, StringComparison.Ordinal)
            && batch.Items.Any(item => item.Status == BackupStatuses.Restored));

    private string CreateBatchId(DateTimeOffset now)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var id = string.Create(
                CultureInfo.InvariantCulture,
                $"{now:yyyyMMdd-HHmmss}-{Random.Shared.Next(0x1000, 0x10000):X4}");
            if (!File.Exists(GetHistoryPath(id)))
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
