using LinkMend.Core.Abstractions;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;

namespace LinkMend.Core.Diagnosis;

public sealed class ShortcutDiagnoser
{
    private readonly ISystemProbe _probe;
    private readonly DataDirectoryInspector _dataDirectories;

    public ShortcutDiagnoser(ISystemProbe probe)
    {
        _probe = probe;
        _dataDirectories = new DataDirectoryInspector(probe);
    }

    public DiagnosisResult Diagnose(
        ShortcutLink link,
        ResolvedShortcutPaths paths,
        IReadOnlyList<ShortcutPeer>? peers = null)
    {
        if (string.IsNullOrWhiteSpace(paths.TargetPath))
        {
            return DiagnoseEmptyTarget(link, paths);
        }

        if (PathResolver.IsShellNamespace(paths.TargetPath))
        {
            return DiagnosisResult.FromIssues(
                new[]
                {
                    new ShortcutIssue(
                        IssueCodes.TargetShellNamespace,
                        IssueSeverity.Info,
                        "系统命名空间快捷方式",
                        "该快捷方式由 Windows Shell 管理，不使用普通文件路径。LinkMend 不会修改这类快捷方式。",
                        new[]
                        {
                            $"记录的目标：{paths.TargetPath}",
                            "这类目标无法用普通文件系统检查或安全改写。"
                        },
                        IssueHandling.None)
                },
                ShortcutHealth.Unsupported);
        }

        var issues = new List<ShortcutIssue>();
        var targetProbe = _probe.ProbePath(paths.TargetPath);
        var targetAvailable = targetProbe.Exists;

        switch (targetProbe.Outcome)
        {
            case ProbeOutcome.File:
            case ProbeOutcome.Directory:
                break;
            case ProbeOutcome.NotFound:
                issues.Add(CreateTargetMissingIssue(link, paths, targetProbe));
                break;
            case ProbeOutcome.VolumeUnavailable:
                issues.Add(new ShortcutIssue(
                    IssueCodes.TargetUnavailable,
                    IssueSeverity.Warning,
                    "目标所在磁盘当前不可用",
                    "磁盘未连接、移动硬盘已拔出或光驱中没有介质。现在无法判断快捷方式是否永久失效，请恢复磁盘后重新检查。",
                    new[]
                    {
                        $"记录的目标：{DescribeRawAndResolved(link.TargetPath, paths.TargetPath)}",
                        targetProbe.Detail ?? "磁盘未就绪"
                    },
                    IssueHandling.Manual));
                break;
            case ProbeOutcome.NetworkUnavailable:
                issues.Add(new ShortcutIssue(
                    IssueCodes.TargetNetworkUnavailable,
                    IssueSeverity.Warning,
                    "目标网络位置当前不可用",
                    "网络共享或映射驱动器当前无法访问。请确认网络连接和共享状态后重新检查，LinkMend 不会把这种情况判定为路径永久失效。",
                    new[]
                    {
                        $"记录的目标：{DescribeRawAndResolved(link.TargetPath, paths.TargetPath)}",
                        targetProbe.Detail ?? "网络位置不可访问"
                    },
                    IssueHandling.Manual));
                break;
            case ProbeOutcome.AccessDenied:
                issues.Add(new ShortcutIssue(
                    IssueCodes.TargetAccessDenied,
                    IssueSeverity.Warning,
                    "无法读取目标位置",
                    "系统拒绝访问目标路径，因此不能判断文件是否真的不存在。请检查权限，必要时以管理员身份重新扫描。",
                    new[]
                    {
                        $"记录的目标：{DescribeRawAndResolved(link.TargetPath, paths.TargetPath)}",
                        targetProbe.Detail ?? "访问被拒绝"
                    },
                    IssueHandling.Manual));
                break;
            case ProbeOutcome.InvalidPath:
                issues.Add(new ShortcutIssue(
                    IssueCodes.TargetInvalid,
                    IssueSeverity.Error,
                    "目标路径无效",
                    "快捷方式记录的目标路径无法被解析为有效的文件路径。可尝试重新创建快捷方式，或手动指定正确程序。",
                    new[]
                    {
                        $"记录的目标：{link.TargetPath}",
                        targetProbe.Detail ?? "路径格式无效"
                    },
                    IssueHandling.Manual));
                break;
            case ProbeOutcome.Unknown:
                issues.Add(new ShortcutIssue(
                    IssueCodes.TargetUnknown,
                    IssueSeverity.Warning,
                    "无法确认目标状态",
                    "读取目标位置时遇到未知错误，因此不会把它直接判定为失效。请稍后重新检查或查看详细错误。",
                    new[]
                    {
                        $"记录的目标：{DescribeRawAndResolved(link.TargetPath, paths.TargetPath)}",
                        targetProbe.Detail ?? "未知错误"
                    },
                    IssueHandling.Manual));
                break;
        }

        if (targetAvailable)
        {
            AddWorkingDirectoryIssues(link, paths, issues);
        }

        AddIconIssues(link, paths, targetAvailable, issues);

        foreach (var issue in _dataDirectories.GetIssues(link, paths, peers))
        {
            issues.Add(issue);
        }

        return issues.Count == 0
            ? DiagnosisResult.Healthy
            : DiagnosisResult.FromIssues(issues);
    }

    public static DiagnosisResult Unreadable(string message)
    {
        return DiagnosisResult.FromIssues(
            new[]
            {
                new ShortcutIssue(
                    IssueCodes.UnreadableShortcut,
                    IssueSeverity.Error,
                    "无法读取快捷方式",
                    "文件无法作为 Windows 快捷方式打开。它可能已损坏、被其他程序占用，或当前账户没有读取权限。LinkMend 不会尝试改写此文件。",
                    new[] { message },
                    IssueHandling.None)
            },
            ShortcutHealth.Unreadable);
    }

    private DiagnosisResult DiagnoseEmptyTarget(ShortcutLink link, ResolvedShortcutPaths paths)
    {
        if (!string.IsNullOrWhiteSpace(paths.IconPath))
        {
            var iconProbe = _probe.ProbePath(paths.IconPath);
            if (iconProbe.Exists)
            {
                return DiagnosisResult.FromIssues(
                    new[]
                    {
                        new ShortcutIssue(
                            IssueCodes.SpecialShortcut,
                            IssueSeverity.Info,
                            "特殊或系统托管快捷方式",
                            "该快捷方式没有记录普通文件目标，可能由 Windows、应用商店或安装程序托管。LinkMend 保留其现状，不进行自动修复。",
                            new[]
                            {
                                $"快捷方式：{link.FilePath}",
                                $"图标来源：{paths.IconPath}",
                                "没有可安全改写的文件目标。"
                            },
                            IssueHandling.None)
                    },
                    ShortcutHealth.Unsupported);
            }
        }

        return DiagnosisResult.FromIssues(
            new[]
            {
                new ShortcutIssue(
                    IssueCodes.TargetMissing,
                    IssueSeverity.Error,
                    "快捷方式没有记录目标",
                    "文件里没有可执行目标或文档路径。它可能已损坏，也可能是 LinkMend 暂不支持的特殊快捷方式；无法根据现有信息自动寻找新位置。",
                    new[]
                    {
                        $"快捷方式：{link.FilePath}",
                        "建议删除后重新创建快捷方式，或从应用安装目录重新生成。"
                    },
                    IssueHandling.Manual)
            });
    }

    private void AddWorkingDirectoryIssues(
        ShortcutLink link,
        ResolvedShortcutPaths paths,
        List<ShortcutIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(paths.WorkingDirectory)
            || PathResolver.IsShellNamespace(paths.WorkingDirectory))
        {
            return;
        }

        var probe = _probe.ProbePath(paths.WorkingDirectory);
        if (probe.Outcome is ProbeOutcome.Directory or ProbeOutcome.Unknown)
        {
            return;
        }

        var handling = probe.Outcome switch
        {
            ProbeOutcome.NotFound or ProbeOutcome.InvalidPath or ProbeOutcome.File => IssueHandling.AutoFix,
            _ => IssueHandling.Manual
        };

        var unavailable = probe.Outcome is ProbeOutcome.VolumeUnavailable or ProbeOutcome.NetworkUnavailable;

        issues.Add(new ShortcutIssue(
            IssueCodes.WorkingDirectoryMissing,
            IssueSeverity.Warning,
            probe.Outcome == ProbeOutcome.File ? "起始位置不是文件夹" : "起始位置不可用",
            unavailable
                ? "快捷方式的“起始位置”位于当前不可用的磁盘或网络位置。应用可能仍能启动；LinkMend 不会自动改变这个可能是有意配置的目录。"
                : "部分程序依赖“起始位置”查找配置或资源。目标程序仍存在，可安全地把起始位置改为程序所在目录。",
            new[]
            {
                $"记录的起始位置：{DescribeRawAndResolved(link.WorkingDirectory, paths.WorkingDirectory)}",
                probe.Detail ?? OutcomeDescription(probe.Outcome)
            },
            handling));
    }

    private void AddIconIssues(
        ShortcutLink link,
        ResolvedShortcutPaths paths,
        bool targetAvailable,
        List<ShortcutIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(paths.IconPath) || PathResolver.IsShellNamespace(paths.IconPath))
        {
            return;
        }

        var probe = _probe.ProbePath(paths.IconPath);
        var handling = targetAvailable ? IssueHandling.AutoFix : IssueHandling.Manual;

        if (probe.Outcome == ProbeOutcome.NotFound)
        {
            issues.Add(new ShortcutIssue(
                IssueCodes.IconSourceMissing,
                IssueSeverity.Warning,
                "图标文件不存在",
                targetAvailable
                    ? "快捷方式指定的图标资源已经不存在，因此可能显示白图标或通用图标。可以改为使用目标程序自带的图标。"
                    : "快捷方式指定的图标资源已经不存在，同时目标程序也缺失，白图标通常需要先恢复目标路径。",
                new[]
                {
                    $"记录的图标：{DescribeRawAndResolved(link.IconPath, paths.IconPath)}",
                    $"图标索引：{link.IconIndex}",
                    probe.Detail ?? "找不到图标文件"
                },
                handling));
            return;
        }

        if (probe.Outcome == ProbeOutcome.AccessDenied
            || probe.Outcome is ProbeOutcome.VolumeUnavailable or ProbeOutcome.NetworkUnavailable)
        {
            issues.Add(new ShortcutIssue(
                IssueCodes.IconUnavailable,
                IssueSeverity.Warning,
                "无法确认图标状态",
                "图标资源当前无法读取。LinkMend 不会仅凭这次失败就判断图标已损坏。",
                new[]
                {
                    $"记录的图标：{DescribeRawAndResolved(link.IconPath, paths.IconPath)}",
                    probe.Detail ?? OutcomeDescription(probe.Outcome)
                },
                IssueHandling.Manual));
            return;
        }

        if (probe.Outcome is ProbeOutcome.InvalidPath or ProbeOutcome.Directory)
        {
            issues.Add(new ShortcutIssue(
                IssueCodes.IconUnavailable,
                IssueSeverity.Warning,
                "图标位置不是有效文件",
                "快捷方式记录了一个不能作为图标读取的位置。可以恢复为系统默认图标，或改用目标程序自带的图标。",
                new[]
                {
                    $"记录的图标：{DescribeRawAndResolved(link.IconPath, paths.IconPath)}",
                    probe.Detail ?? OutcomeDescription(probe.Outcome)
                },
                handling));
            return;
        }

        if (probe.Outcome != ProbeOutcome.File)
        {
            return;
        }

        var iconProbe = _probe.ProbeIcon(paths.IconPath, link.IconIndex);
        if (iconProbe == IconProbeOutcome.Available || iconProbe == IconProbeOutcome.Unknown)
        {
            return;
        }

        var title = iconProbe == IconProbeOutcome.Missing
            ? "图标资源无法读取"
            : "图标索引不可用";

        issues.Add(new ShortcutIssue(
            IssueCodes.IconIndexUnavailable,
            IssueSeverity.Warning,
            title,
            "图标文件存在，但其中找不到快捷方式记录的图标索引，通常会回退为白图标或系统默认图标。可以改用目标程序自带的图标。",
            new[]
            {
                $"图标文件：{paths.IconPath}",
                $"记录的索引：{link.IconIndex}",
                "图标提取检查未找到可用图标。"
            },
            handling));
    }

    private static ShortcutIssue CreateTargetMissingIssue(
        ShortcutLink link,
        ResolvedShortcutPaths paths,
        PathProbe probe)
    {
        var evidence = new List<string>
        {
            $"记录的目标：{DescribeRawAndResolved(link.TargetPath, paths.TargetPath)}",
            probe.Detail ?? "系统返回：找不到文件或目录"
        };

        if (string.IsNullOrWhiteSpace(paths.IconPath))
        {
            evidence.Add("该快捷方式没有单独指定图标，目标丢失时通常显示为白图标或系统默认图标。");
        }

        return new ShortcutIssue(
            IssueCodes.TargetMissing,
            IssueSeverity.Error,
            "目标不存在",
            "快捷方式指向的文件或文件夹不在这里。可能是软件被移动、卸载，或快捷方式来自另一台电脑。可以搜索程序的新位置后再修复。",
            evidence,
            IssueHandling.NeedsRelocation);
    }

    private static string DescribeRawAndResolved(string raw, string resolved)
    {
        var expanded = PathResolver.Expand(raw);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return resolved;
        }

        return string.Equals(expanded, resolved, StringComparison.OrdinalIgnoreCase)
            ? resolved
            : $"{expanded}（解析为 {resolved}）";
    }

    private static string OutcomeDescription(ProbeOutcome outcome) => outcome switch
    {
        ProbeOutcome.NotFound => "找不到文件或目录",
        ProbeOutcome.AccessDenied => "访问被拒绝",
        ProbeOutcome.VolumeUnavailable => "磁盘未就绪",
        ProbeOutcome.NetworkUnavailable => "网络位置不可访问",
        ProbeOutcome.InvalidPath => "路径格式无效",
        ProbeOutcome.File => "这是文件，不是文件夹",
        _ => "未知状态"
    };
}
