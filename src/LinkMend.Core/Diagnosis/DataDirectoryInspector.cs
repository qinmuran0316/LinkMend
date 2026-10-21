using LinkMend.Core.Abstractions;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;

namespace LinkMend.Core.Diagnosis;

public sealed record DataDirectorySetting(string Flag, string Value, string ResolvedPath);

/// <summary>
/// Inspects explicit data-directory switches only. LinkMend does not guess an
/// application's private configuration format from its name.
/// </summary>
public sealed class DataDirectoryInspector
{
    private static readonly string[] SupportedFlags =
    {
        "--user-data-dir",
        "--data-dir",
        "--datadir",
        "--user-data-path"
    };

    private readonly ISystemProbe _probe;

    public DataDirectoryInspector(ISystemProbe probe)
    {
        _probe = probe;
    }

    public IReadOnlyList<DataDirectorySetting> Parse(ShortcutLink link, ResolvedShortcutPaths paths)
    {
        var arguments = CommandLineSplitter.Split(link.Arguments);
        var baseDirectory = ResolveBaseDirectory(link, paths);
        var results = new List<DataDirectorySetting>();

        for (var index = 0; index < arguments.Count; index++)
        {
            var token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var equalsIndex = token.IndexOf('=');
            var flag = equalsIndex >= 0 ? token[..equalsIndex] : token;
            var supported = SupportedFlags.FirstOrDefault(
                item => string.Equals(item, flag, StringComparison.OrdinalIgnoreCase));

            if (supported is null)
            {
                continue;
            }

            string? value = null;
            if (equalsIndex >= 0)
            {
                value = token[(equalsIndex + 1)..];
            }
            else if (index + 1 < arguments.Count && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = arguments[++index];
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            value = PathResolver.Expand(value);
            var resolved = PathResolver.ResolveOne(value, baseDirectory);
            results.Add(new DataDirectorySetting(supported, value, resolved));
        }

        return results;
    }

    public IReadOnlyList<ShortcutIssue> GetIssues(
        ShortcutLink link,
        ResolvedShortcutPaths paths,
        IReadOnlyList<ShortcutPeer>? peers)
    {
        var issues = new List<ShortcutIssue>();
        var ownSettings = Parse(link, paths);

        foreach (var setting in ownSettings)
        {
            if (string.IsNullOrWhiteSpace(setting.ResolvedPath))
            {
                continue;
            }

            var probe = _probe.ProbePath(setting.ResolvedPath);
            if (probe.Outcome is ProbeOutcome.VolumeUnavailable or ProbeOutcome.NetworkUnavailable)
            {
                issues.Add(new ShortcutIssue(
                    IssueCodes.DataPathUnavailable,
                    IssueSeverity.Warning,
                    "数据目录当前不可用",
                    "启动参数指定的数据目录所在磁盘或网络位置当前不可访问。应用可能无法启动，或退回默认配置。LinkMend 不会自动改写启动参数。",
                    new[]
                    {
                        $"参数：{setting.Flag}",
                        $"数据目录：{setting.ResolvedPath}",
                        probe.Detail ?? "磁盘未就绪或网络位置离线"
                    },
                    IssueHandling.Manual));
            }
            else if (probe.Outcome == ProbeOutcome.AccessDenied)
            {
                issues.Add(new ShortcutIssue(
                    IssueCodes.DataPathUnavailable,
                    IssueSeverity.Warning,
                    "数据目录没有访问权限",
                    "启动参数指向的数据目录无法读取。请检查目录权限，或确认是否应由管理员运行该程序。",
                    new[]
                    {
                        $"参数：{setting.Flag}",
                        $"数据目录：{setting.ResolvedPath}",
                        probe.Detail ?? "访问被拒绝"
                    },
                    IssueHandling.Manual));
            }
            else if (probe.Outcome == ProbeOutcome.InvalidPath)
            {
                issues.Add(new ShortcutIssue(
                    IssueCodes.DataPathUnavailable,
                    IssueSeverity.Warning,
                    "数据目录参数不是有效路径",
                    "启动参数包含无效的数据目录路径。LinkMend 不会擅自删除参数，请在确认应用要求后手动调整。",
                    new[]
                    {
                        $"参数：{setting.Flag}",
                        $"参数值：{setting.Value}"
                    },
                    IssueHandling.Manual));
            }
        }

        if (peers is null
            || peers.Count < 2
            || ownSettings.Count == 0
            || string.IsNullOrWhiteSpace(paths.TargetPath))
        {
            return issues;
        }

        var targetKey = PathResolver.NormalizeKey(paths.TargetPath);
        var related = peers
            .Where(peer => string.Equals(
                PathResolver.NormalizeKey(peer.Paths.TargetPath),
                targetKey,
                StringComparison.Ordinal))
            .ToArray();

        if (related.Length < 2)
        {
            return issues;
        }

        var ownFlags = ownSettings
            .Select(setting => setting.Flag)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var flag in SupportedFlags.Where(ownFlags.Contains))
        {
            var rows = new List<(ShortcutPeer Peer, DataDirectorySetting Setting)>();
            foreach (var peer in related)
            {
                foreach (var setting in Parse(peer.Link, peer.Paths))
                {
                    if (string.Equals(setting.Flag, flag, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(setting.ResolvedPath))
                    {
                        rows.Add((peer, setting));
                    }
                }
            }

            var distinct = rows
                .Select(row => PathResolver.NormalizeKey(row.Setting.ResolvedPath))
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (distinct < 2)
            {
                continue;
            }

            var evidence = rows
                .GroupBy(row => PathResolver.NormalizeKey(row.Setting.ResolvedPath), StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    var names = string.Join(
                        "、",
                        group.Select(row => $"「{row.Peer.Link.DisplayName}」").Distinct(StringComparer.Ordinal));
                    return $"{names}：{first.Setting.ResolvedPath}";
                })
                .Take(6)
                .ToArray();

            issues.Add(new ShortcutIssue(
                IssueCodes.DataPathConflict,
                IssueSeverity.Warning,
                "多个数据目录配置",
                $"同一程序的快捷方式使用了不同的 {flag} 值。这可能是有意隔离配置，也可能是搬移或复制快捷方式时遗留的冲突；LinkMend 不会自动修改。",
                evidence,
                IssueHandling.Manual));
        }

        return issues;
    }

    private string ResolveBaseDirectory(ShortcutLink link, ResolvedShortcutPaths paths)
    {
        if (!string.IsNullOrWhiteSpace(paths.WorkingDirectory)
            && _probe.ProbePath(paths.WorkingDirectory).Outcome == ProbeOutcome.Directory)
        {
            return paths.WorkingDirectory;
        }

        var shortcutDirectory = System.IO.Path.GetDirectoryName(link.FilePath);
        return string.IsNullOrWhiteSpace(shortcutDirectory)
            ? Environment.CurrentDirectory
            : shortcutDirectory;
    }
}
