using LinkMend.Core.Abstractions;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;

namespace LinkMend.Core.Relocation;

public sealed class RelocationService
{
    private const int SearchDepth = 5;
    private readonly ISystemProbe _probe;
    private readonly IInstallationIndex _installationIndex;
    private readonly IExecutableSearch _executableSearch;
    private readonly ISearchRootProvider _searchRoots;
    private readonly TimeSpan _perRootBudget;

    public RelocationService(
        ISystemProbe probe,
        IInstallationIndex installationIndex,
        IExecutableSearch executableSearch,
        ISearchRootProvider searchRoots,
        TimeSpan? perRootBudget = null)
    {
        _probe = probe;
        _installationIndex = installationIndex;
        _executableSearch = executableSearch;
        _searchRoots = searchRoots;
        _perRootBudget = perRootBudget ?? TimeSpan.FromSeconds(3);
    }

    public RelocationSearchResult FindCandidates(
        ScannedShortcut shortcut,
        IReadOnlyList<string> customRoots,
        CancellationToken cancellationToken = default)
    {
        var oldTarget = shortcut.Paths.TargetPath;
        if (string.IsNullOrWhiteSpace(oldTarget) || PathResolver.IsShellNamespace(oldTarget))
        {
            return new RelocationSearchResult(
                Array.Empty<ResolutionCandidate>(),
                Array.Empty<string>(),
                "这个快捷方式没有记录可搜索的目标文件。");
        }

        var fileName = Path.GetFileName(oldTarget);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new RelocationSearchResult(
                Array.Empty<ResolutionCandidate>(),
                Array.Empty<string>(),
                "无法从原目标路径中提取程序文件名。");
        }

        var oldMetadata = _probe.ReadMetadata(oldTarget);
        var oldFolderName = GetDirectoryName(oldTarget);
        var candidates = new Dictionary<string, CandidateAccumulator>(StringComparer.OrdinalIgnoreCase);
        var searchedRoots = new List<string>();
        var timedOutRoots = 0;

        foreach (var hint in _installationIndex.FindByExecutableName(fileName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddCandidate(hint.ExecutablePath, hint.Source);
        }

        var roots = BuildRoots(shortcut, oldTarget, customRoots);
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            searchedRoots.Add(root);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_perRootBudget > TimeSpan.Zero)
            {
                linked.CancelAfter(_perRootBudget);
            }

            IReadOnlyList<string> found;
            try
            {
                found = _executableSearch.FindExactFileName(root, fileName, SearchDepth, linked.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timedOutRoots++;
                continue;
            }

            foreach (var path in found)
            {
                AddCandidate(path, null);
            }
        }

        var scored = candidates.Values
            .Select(accumulator => Score(accumulator, fileName, oldFolderName, oldMetadata))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ExecutablePath, StringComparer.CurrentCultureIgnoreCase)
            .Take(30)
            .ToArray();

        var notice = BuildNotice(scored.Length, timedOutRoots);
        return new RelocationSearchResult(scored, searchedRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), notice);

        void AddCandidate(string path, string? source)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string resolved;
            try
            {
                resolved = Path.GetFullPath(PathResolver.Expand(path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return;
            }

            if (_probe.ProbePath(resolved).Outcome != ProbeOutcome.File)
            {
                return;
            }

            var key = PathResolver.NormalizeKey(resolved);
            if (!candidates.TryGetValue(key, out var accumulator))
            {
                accumulator = new CandidateAccumulator(resolved);
                candidates.Add(key, accumulator);
            }

            if (!string.IsNullOrWhiteSpace(source))
            {
                accumulator.Sources.Add(source);
            }
        }

        ResolutionCandidate? Score(
            CandidateAccumulator accumulator,
            string expectedFileName,
            string expectedFolderName,
            FileMetadata? oldMetadata)
        {
            var candidateFileName = Path.GetFileName(accumulator.Path);
            if (!string.Equals(candidateFileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var score = 40;
            var reasons = new List<string> { "文件名与快捷方式记录一致" };
            var metadata = _probe.ReadMetadata(accumulator.Path);

            if (accumulator.Sources.Count > 0)
            {
                score += 25;
                reasons.Add($"系统登记来源：{string.Join("、", accumulator.Sources)}");
            }

            var candidateFolder = GetDirectoryName(accumulator.Path);
            if (!string.IsNullOrWhiteSpace(expectedFolderName)
                && string.Equals(candidateFolder, expectedFolderName, StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
                reasons.Add($"所在文件夹与原位置同名：{candidateFolder}");
            }

            if (metadata is not null && oldMetadata is not null)
            {
                var productMatches = FieldsMatch(oldMetadata.ProductName, metadata.ProductName);
                var companyMatches = FieldsMatch(oldMetadata.CompanyName, metadata.CompanyName);
                var descriptionMatches = FieldsMatch(oldMetadata.FileDescription, metadata.FileDescription);

                if (productMatches)
                {
                    score += 25;
                    reasons.Add($"产品名称一致：{metadata.ProductName}");
                }

                if (companyMatches)
                {
                    score += 15;
                    reasons.Add($"发布者一致：{metadata.CompanyName}");
                }

                if (descriptionMatches && !productMatches)
                {
                    score += 5;
                    reasons.Add($"文件说明一致：{metadata.FileDescription}");
                }

                if (oldMetadata.Length > 0 && metadata.Length > 0
                    && SimilarSize(oldMetadata.Length, metadata.Length))
                {
                    score += 5;
                    reasons.Add("文件大小与原程序接近");
                }
            }

            if (reasons.Count == 1)
            {
                reasons.Add("只确认了文件名；请核对发布者和所在目录后再使用");
            }

            score = Math.Min(score, 100);
            var confidence = score >= 65
                ? CandidateConfidence.High
                : score >= 45
                    ? CandidateConfidence.Medium
                    : CandidateConfidence.Low;

            return new ResolutionCandidate(
                accumulator.Path,
                score,
                confidence,
                reasons.Distinct(StringComparer.Ordinal).ToArray(),
                metadata);
        }

        string BuildNotice(int foundCount, int timedOut)
        {
            if (foundCount > 0)
            {
                return timedOut > 0
                    ? "部分目录搜索超时。结果按证据排序，仍需你确认后再修复。"
                    : "找到多个同名程序时不会自动选择，请核对路径、发布者和目录后确认。";
            }

            return timedOut > 0
                ? "部分目录搜索超时，且尚未找到同名程序。请添加软件所在目录后重新搜索。"
                : "没有找到同名程序。安装记录只覆盖标准安装信息，便携软件请添加其所在目录后重新搜索。";
        }
    }

    private List<string> BuildRoots(
        ScannedShortcut shortcut,
        string oldTarget,
        IReadOnlyList<string> customRoots)
    {
        var roots = new List<string>();

        foreach (var root in customRoots)
        {
            AddRoot(root);
        }

        try
        {
            var oldParent = Path.GetDirectoryName(oldTarget);
            if (!string.IsNullOrWhiteSpace(oldParent))
            {
                AddRoot(oldParent);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Ignore malformed legacy paths.
        }

        var shortcutDirectory = Path.GetDirectoryName(shortcut.Link.FilePath);
        if (!string.IsNullOrWhiteSpace(shortcutDirectory))
        {
            AddRoot(shortcutDirectory);
        }

        foreach (var root in _searchRoots.GetDefaultRoots())
        {
            AddRoot(root);
        }

        return roots;

        void AddRoot(string? root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            var resolved = PathResolver.ResolveOne(root, Environment.CurrentDirectory);
            if (_probe.ProbePath(resolved).Outcome != ProbeOutcome.Directory)
            {
                return;
            }

            if (!roots.Contains(resolved, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(resolved);
            }
        }
    }

    private static string GetDirectoryName(string path)
    {
        try
        {
            return Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }

    private static bool FieldsMatch(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SimilarSize(long left, long right)
    {
        if (left <= 0 || right <= 0)
        {
            return false;
        }

        var difference = Math.Abs(left - right);
        return difference <= Math.Max(left, right) * 0.1;
    }

    private sealed class CandidateAccumulator
    {
        public CandidateAccumulator(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public HashSet<string> Sources { get; } = new(StringComparer.Ordinal);
    }
}
