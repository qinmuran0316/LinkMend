namespace LinkMend.Core.Models;

public sealed record ShortcutPeer(ShortcutLink Link, ResolvedShortcutPaths Paths);

public sealed record ScannedShortcut(
    ShortcutLink Link,
    ResolvedShortcutPaths Paths,
    string ContentHash,
    string ScopeLabel,
    DiagnosisResult Diagnosis)
{
    public bool CanBuildAutomaticPlan =>
        !string.IsNullOrWhiteSpace(ContentHash)
        && (Diagnosis.HasAutoFix || Diagnosis.NeedsRelocation);
}

public sealed record ScanRoot(string DirectoryPath, string Label);

public sealed record ScanRequest(
    IReadOnlyList<ScanRoot> Roots,
    IReadOnlyList<string> ExplicitShortcutFiles);

public sealed record ScanProgress(int Processed, int Total, string CurrentFile);

public sealed record ScanResult(
    IReadOnlyList<ScannedShortcut> Shortcuts,
    int FailedReads,
    int SkippedItems)
{
    public int Total => Shortcuts.Count;
}
