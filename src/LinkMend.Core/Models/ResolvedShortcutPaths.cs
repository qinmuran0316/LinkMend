namespace LinkMend.Core.Models;

/// <summary>
/// Paths after expanding environment variables and resolving relative segments.
/// A path is not guaranteed to exist.
/// </summary>
public sealed record ResolvedShortcutPaths(
    string TargetPath,
    string WorkingDirectory,
    string IconPath)
{
    public static ResolvedShortcutPaths Empty { get; } = new("", "", "");
}
