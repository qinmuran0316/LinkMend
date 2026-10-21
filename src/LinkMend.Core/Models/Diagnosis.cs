namespace LinkMend.Core.Models;

public enum ShortcutHealth
{
    Healthy,
    NeedsAttention,
    Broken,
    Unsupported,
    Unreadable
}

public enum IssueSeverity
{
    Info,
    Warning,
    Error
}

public enum IssueHandling
{
    None,
    AutoFix,
    NeedsRelocation,
    Manual
}

public static class IssueCodes
{
    public const string TargetMissing = "target_missing";
    public const string TargetUnavailable = "target_unavailable";
    public const string TargetNetworkUnavailable = "target_network_unavailable";
    public const string TargetAccessDenied = "target_access_denied";
    public const string TargetInvalid = "target_invalid";
    public const string TargetUnknown = "target_unknown";
    public const string TargetShellNamespace = "target_shell_namespace";
    public const string SpecialShortcut = "special_shortcut";
    public const string WorkingDirectoryMissing = "working_directory_missing";
    public const string WorkingDirectoryUnavailable = "working_directory_unavailable";
    public const string IconSourceMissing = "icon_source_missing";
    public const string IconIndexUnavailable = "icon_index_unavailable";
    public const string IconUnavailable = "icon_unavailable";
    public const string DataPathUnavailable = "data_path_unavailable";
    public const string DataPathConflict = "data_path_conflict";
    public const string UnreadableShortcut = "unreadable_shortcut";
    public const string HashUnavailable = "hash_unavailable";
}

public sealed record ShortcutIssue(
    string Code,
    IssueSeverity Severity,
    string Title,
    string Summary,
    IReadOnlyList<string> Evidence,
    IssueHandling Handling);

public sealed record DiagnosisResult
{
    public required IReadOnlyList<ShortcutIssue> Issues { get; init; }

    public required ShortcutHealth Health { get; init; }

    public bool HasProblems => Health is not ShortcutHealth.Healthy;

    public bool NeedsRelocation =>
        Issues.Any(issue => issue.Handling == IssueHandling.NeedsRelocation);

    public bool HasAutoFix =>
        Issues.Any(issue => issue.Handling == IssueHandling.AutoFix);

    public ShortcutIssue? PrimaryIssue =>
        Issues.OrderByDescending(issue => issue.Severity).FirstOrDefault();

    public static DiagnosisResult Healthy { get; } = new()
    {
        Issues = Array.Empty<ShortcutIssue>(),
        Health = ShortcutHealth.Healthy
    };

    public static DiagnosisResult FromIssues(IEnumerable<ShortcutIssue> issues, ShortcutHealth? overrideHealth = null)
    {
        var list = issues.ToArray();
        var health = overrideHealth ?? CalculateHealth(list);
        return new DiagnosisResult { Issues = list, Health = health };
    }

    public DiagnosisResult AddIssue(ShortcutIssue issue)
    {
        var issues = Issues.Append(issue).ToArray();
        var health = Health == ShortcutHealth.Unreadable
            ? Health
            : CalculateHealth(issues);
        return this with { Issues = issues, Health = health };
    }

    public static ShortcutHealth CalculateHealth(IReadOnlyList<ShortcutIssue> issues)
    {
        if (issues.Any(issue => issue.Severity == IssueSeverity.Error))
        {
            return ShortcutHealth.Broken;
        }

        if (issues.Any(issue => issue.Severity == IssueSeverity.Warning))
        {
            return ShortcutHealth.NeedsAttention;
        }

        return ShortcutHealth.Healthy;
    }
}
