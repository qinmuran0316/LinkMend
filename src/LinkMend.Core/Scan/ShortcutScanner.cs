using LinkMend.Core.Abstractions;
using LinkMend.Core.Diagnosis;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;

namespace LinkMend.Core.Scan;

public sealed class ShortcutScanner
{
    private readonly IShortcutStore _store;
    private readonly ShortcutDiagnoser _diagnoser;

    public ShortcutScanner(IShortcutStore store, ShortcutDiagnoser diagnoser)
    {
        _store = store;
        _diagnoser = diagnoser;
    }

    public ScanResult Scan(
        ScanRequest request,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = CollectShortcutFiles(request, cancellationToken, out var skippedItems);
        var total = files.Count;
        var processed = 0;
        var failedReads = 0;
        var pending = new List<PendingShortcut>();

        progress?.Report(new ScanProgress(0, total, "正在读取快捷方式…"));

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress(processed, total, file.FilePath));

            var hashBeforeReadOk = FileHash.TryCompute(file.FilePath, out var hashBeforeRead, out var hashBeforeReadError);
            var read = _store.Read(file.FilePath);
            if (!read.Success || read.Link is null)
            {
                failedReads++;
                var failure = read.Failure;
                var link = new ShortcutLink
                {
                    FilePath = file.FilePath,
                    DisplayName = Path.GetFileNameWithoutExtension(file.FilePath)
                };
                FileHash.TryCompute(file.FilePath, out var brokenHash, out _);
                var diagnosis = ShortcutDiagnoser.Unreadable(
                    failure is null ? "未知读取错误" : failure.Message);
                pending.Add(new PendingShortcut(
                    new ScannedShortcut(
                        link,
                        ResolvedShortcutPaths.Empty,
                        brokenHash,
                        file.ScopeLabel,
                        diagnosis),
                    HashFailed: false));
                processed++;
                continue;
            }

            var paths = PathResolver.Resolve(read.Link);
            var hashOk = FileHash.TryCompute(file.FilePath, out var hash, out var hashError);
            if (!hashBeforeReadOk)
            {
                hashOk = false;
                hashError = hashBeforeReadError;
            }
            else if (hashOk && !string.Equals(hashBeforeRead, hash, StringComparison.OrdinalIgnoreCase))
            {
                hashOk = false;
                hashError = "快捷方式在读取期间发生变化，请重新扫描后再修复。";
            }

            var readLink = read.Link;
            pending.Add(new PendingShortcut(
                new ScannedShortcut(
                    readLink,
                    paths,
                    hashOk ? hash : "",
                    file.ScopeLabel,
                    DiagnosisResult.Healthy),
                HashFailed: !hashOk,
                HashError: hashError));
            processed++;
        }

        var peers = pending
            .Where(item => item.Shortcut.Diagnosis.Health != ShortcutHealth.Unreadable)
            .Select(item => new ShortcutPeer(item.Shortcut.Link, item.Shortcut.Paths))
            .ToArray();

        var results = new List<ScannedShortcut>(pending.Count);
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scanned = item.Shortcut;

            if (scanned.Diagnosis.Health != ShortcutHealth.Unreadable)
            {
                var diagnosis = _diagnoser.Diagnose(scanned.Link, scanned.Paths, peers);
                if (item.HashFailed)
                {
                    diagnosis = diagnosis.AddIssue(new ShortcutIssue(
                        IssueCodes.HashUnavailable,
                        IssueSeverity.Warning,
                        "无法校验快捷方式内容",
                        "快捷方式可以读取，但无法计算文件校验值。为防止覆盖扫描后的变化，LinkMend 不会自动修复这一项。",
                        new[]
                        {
                            $"快捷方式：{scanned.Link.FilePath}",
                            item.HashError ?? "读取文件内容失败"
                        },
                        IssueHandling.Manual));
                }

                scanned = scanned with { Diagnosis = diagnosis };
            }

            results.Add(scanned);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ScanProgress(total, total, "扫描完成"));

        return new ScanResult(
            results
                .OrderBy(item => HealthRank(item.Diagnosis.Health))
                .ThenBy(item => item.Link.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Link.FilePath, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            failedReads,
            skippedItems);
    }

    private static List<ShortcutFile> CollectShortcutFiles(
        ScanRequest request,
        CancellationToken cancellationToken,
        out int skippedItems)
    {
        var files = new Dictionary<string, ShortcutFile>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;

        foreach (var root in request.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root.DirectoryPath) || !Directory.Exists(root.DirectoryPath))
            {
                skipped++;
                continue;
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MatchCasing = MatchCasing.CaseInsensitive,
                ReturnSpecialDirectories = false
            };

            try
            {
                if ((File.GetAttributes(root.DirectoryPath) & FileAttributes.ReparsePoint) != 0)
                {
                    skipped++;
                    continue;
                }

                foreach (var path in Directory.EnumerateFiles(root.DirectoryPath, "*.lnk", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Add(path, root.Label);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        foreach (var path in request.ExplicitShortcutFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path)
                || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path))
            {
                skipped++;
                continue;
            }

            Add(path, "单独添加");
        }

        skippedItems = skipped;
        return files.Values
            .OrderBy(file => file.FilePath, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        void Add(string path, string label)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
                if (!PathResolver.IsShortcutFilePath(fullPath)
                    || (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                {
                    skipped++;
                    return;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                skipped++;
                return;
            }

            files.TryAdd(PathResolver.NormalizeKey(fullPath), new ShortcutFile(fullPath, label));
        }
    }

    private static int HealthRank(ShortcutHealth health) => health switch
    {
        ShortcutHealth.Broken => 0,
        ShortcutHealth.NeedsAttention => 1,
        ShortcutHealth.Unreadable => 2,
        ShortcutHealth.Unsupported => 3,
        _ => 4
    };

    private sealed record ShortcutFile(string FilePath, string ScopeLabel);

    private sealed record PendingShortcut(
        ScannedShortcut Shortcut,
        bool HashFailed,
        string? HashError = null);
}
