using LinkMend.Core.Models;
using LinkMend.Core.Paths;

namespace LinkMend.App.ViewModels;

public sealed class ShortcutRow : ObservableObject
{
    private bool _isChecked;
    private ResolutionCandidate? _selectedCandidate;

    public ShortcutRow(ScannedShortcut item)
    {
        Item = item;
    }

    public ScannedShortcut Item { get; }

    public ShortcutHealth Health => Item.Diagnosis.Health;

    public IReadOnlyList<ShortcutIssue> Issues => Item.Diagnosis.Issues;

    public bool HasIssues => Issues.Count > 0;

    public bool IsHealthy => Health == ShortcutHealth.Healthy;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Item.Link.DisplayName)
            ? Item.Link.FileName
            : Item.Link.DisplayName;

    public string FileName => Item.Link.FileName;

    public string FilePath => Item.Link.FilePath;

    public string ScopeLabel => Item.ScopeLabel;

    public string StatusLabel => Item.Diagnosis.PrimaryIssue?.Code switch
    {
        IssueCodes.TargetUnavailable or IssueCodes.TargetNetworkUnavailable => "暂不可用",
        IssueCodes.TargetAccessDenied => "需检查权限",
        _ => Health switch
        {
            ShortcutHealth.Healthy => "正常",
            ShortcutHealth.NeedsAttention => "需注意",
            ShortcutHealth.Broken => "已失效",
            ShortcutHealth.Unsupported => "特殊项",
            ShortcutHealth.Unreadable => "无法读取",
            _ => "未知"
        }
    };

    public string PrimaryTitle =>
        Item.Diagnosis.PrimaryIssue?.Title ?? "未发现问题";

    public string PrimarySummary =>
        Item.Diagnosis.PrimaryIssue?.Summary
        ?? "LinkMend 没有发现可确定的目标、起始位置或图标问题。";

    public bool NeedsRelocation => Item.Diagnosis.NeedsRelocation;

    public bool HasAutoFix => Item.Diagnosis.HasAutoFix;

    public bool CanAct => Item.CanBuildAutomaticPlan;

    public bool IsSelectable => CanAct;

    public bool CanApplyFix =>
        CanAct && (HasAutoFix || (NeedsRelocation && SelectedCandidate is not null));

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    public ResolutionCandidate? SelectedCandidate
    {
        get => _selectedCandidate;
        private set => SetProperty(ref _selectedCandidate, value);
    }

    public bool HasCandidate => SelectedCandidate is not null;

    public string CandidateDisplay =>
        SelectedCandidate is null
            ? ""
            : $"{SelectedCandidate.ExecutablePath}（{ConfidenceLabel(SelectedCandidate.Confidence)}可信度）";

    public string CandidateReasons =>
        SelectedCandidate is null
            ? ""
            : string.Join(Environment.NewLine, SelectedCandidate.Reasons.Select(reason => $"• {reason}"));

    public string TargetDisplay
    {
        get
        {
            var raw = Item.Link.TargetPath;
            var resolved = Item.Paths.TargetPath;
            if (string.IsNullOrWhiteSpace(raw) && string.IsNullOrWhiteSpace(resolved))
            {
                return "（未记录目标）";
            }

            if (string.IsNullOrWhiteSpace(raw)
                || string.Equals(PathResolver.Expand(raw), resolved, StringComparison.OrdinalIgnoreCase))
            {
                return resolved;
            }

            return $"{resolved}{Environment.NewLine}原始记录：{raw}";
        }
    }

    public string WorkingDirectoryDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Item.Link.WorkingDirectory))
            {
                return "（未设置，使用目标所在目录）";
            }

            return Item.Paths.WorkingDirectory;
        }
    }

    public string IconDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Item.Link.IconPath))
            {
                return "使用目标程序默认图标";
            }

            return $"{Item.Paths.IconPath}, {Item.Link.IconIndex}";
        }
    }

    public string Suggestion
    {
        get
        {
            if (NeedsRelocation)
            {
                return SelectedCandidate is null
                    ? "点击“查找新位置”，从候选程序中确认正确位置后再修复。"
                    : $"目标将改为：{SelectedCandidate.ExecutablePath}。请预览后再应用修复。";
            }

            if (HasAutoFix)
            {
                var fixes = new List<string>();
                if (Item.Diagnosis.Issues.Any(issue =>
                        issue.Code == IssueCodes.WorkingDirectoryMissing
                        && issue.Handling == IssueHandling.AutoFix))
                {
                    fixes.Add("将起始位置改为目标所在目录");
                }

                if (Item.Diagnosis.Issues.Any(issue =>
                        (issue.Code == IssueCodes.IconSourceMissing
                         || issue.Code == IssueCodes.IconIndexUnavailable
                         || issue.Code == IssueCodes.IconUnavailable)
                        && issue.Handling == IssueHandling.AutoFix))
                {
                    fixes.Add("修复或清除失效的自定义图标");
                }

                return fixes.Count > 0
                    ? $"可安全自动修复：{string.Join("；", fixes)}。修复前会先备份。"
                    : "有可自动修复的问题，请预览修改后再确认。";
            }

            return Health switch
            {
                ShortcutHealth.Unreadable => "建议删除后重新创建快捷方式；LinkMend 不会改写无法读取的文件。",
                ShortcutHealth.Unsupported => "这类快捷方式由系统或应用托管，本版不做修改。",
                ShortcutHealth.NeedsAttention => "请根据诊断依据手动处理；LinkMend 会避免自动修改数据目录和启动参数。",
                ShortcutHealth.Broken => "检查文件权限或恢复磁盘后重新扫描；当前信息不足以安全修复。",
                _ => "未发现可确定的问题。如果图标仍显示为空，可能是 Windows 图标缓存问题，本版不会强制清理。"
            };
        }
    }

    public string CheckBoxToolTip => CanAct
        ? "勾选后可在预览中修复"
        : "当前问题不支持自动修复";

    public void SetCandidate(ResolutionCandidate? candidate)
    {
        SelectedCandidate = candidate;
        OnPropertyChanged(nameof(HasCandidate));
        OnPropertyChanged(nameof(CanApplyFix));
        OnPropertyChanged(nameof(CandidateDisplay));
        OnPropertyChanged(nameof(CandidateReasons));
        OnPropertyChanged(nameof(Suggestion));
    }

    private static string ConfidenceLabel(CandidateConfidence confidence) => confidence switch
    {
        CandidateConfidence.High => "高",
        CandidateConfidence.Medium => "中",
        _ => "低"
    };
}
