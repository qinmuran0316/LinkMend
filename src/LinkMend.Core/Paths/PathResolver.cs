using LinkMend.Core.Models;

namespace LinkMend.Core.Paths;

public static class PathResolver
{
    private static readonly HashSet<string> IconCapableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".ico", ".cpl", ".scr", ".ocx"
    };

    public static ResolvedShortcutPaths Resolve(ShortcutLink link)
    {
        var baseDirectory = Path.GetDirectoryName(link.FilePath) ?? "";
        return new ResolvedShortcutPaths(
            ResolveOne(link.TargetPath, baseDirectory),
            ResolveOne(link.WorkingDirectory, baseDirectory),
            ResolveOne(link.IconPath, baseDirectory));
    }

    public static string ResolveOne(string? value, string baseDirectory)
    {
        var expanded = Expand(value);
        if (expanded.Length == 0 || IsShellNamespace(expanded))
        {
            return expanded;
        }

        if (Uri.TryCreate(expanded, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return expanded;
        }

        try
        {
            if (uri is { IsFile: true })
            {
                expanded = uri.LocalPath;
            }

            if (Path.IsPathFullyQualified(expanded))
            {
                return Path.GetFullPath(expanded);
            }

            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                return Path.GetFullPath(Path.Combine(baseDirectory, expanded));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return expanded;
        }

        return expanded;
    }

    public static string Expand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim();
        try
        {
            trimmed = Environment.ExpandEnvironmentVariables(trimmed);
        }
        catch (ArgumentException)
        {
            // Leave malformed values intact so they can be shown as evidence.
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    public static bool IsShellNamespace(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("::", StringComparison.Ordinal);
    }

    public static bool IsIconCapable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) && IconCapableExtensions.Contains(extension);
    }

    public static string NormalizeKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var value = path.Trim();
        if (IsShellNamespace(value))
        {
            return value.ToUpperInvariant();
        }

        try
        {
            value = Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return value.TrimEnd('\\', '/').ToUpperInvariant();
        }

        var root = Path.GetPathRoot(value) ?? "";
        value = value.TrimEnd('\\', '/');
        if (value.Length == 0)
        {
            value = root;
        }

        return value.ToUpperInvariant();
    }

    public static bool AreSame(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(NormalizeKey(left), NormalizeKey(right), StringComparison.Ordinal);

    public static string GetContainingDirectory(string path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        if (isDirectory)
        {
            return path;
        }

        try
        {
            return Path.GetDirectoryName(path) ?? "";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }
}
