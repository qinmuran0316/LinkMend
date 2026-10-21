using LinkMend.Core.Abstractions;

namespace LinkMend.Windows.Search;

/// <summary>
/// A deliberately bounded directory search. It never follows reparse points and
/// stops after a fixed number of directories so relocation cannot stall the app.
/// </summary>
public sealed class WindowsExecutableSearch : IExecutableSearch
{
    private const int MaxVisitedDirectories = 8000;
    private const int MaxResults = 50;

    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows",
        "winsxs",
        "servicing",
        "$recycle.bin",
        "system volume information",
        "windowsapps",
        "installer",
        "node_modules",
        ".git",
        ".vs"
    };

    public IReadOnlyList<string> FindExactFileName(
        string root,
        string fileName,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(root)
            || string.IsNullOrWhiteSpace(fileName)
            || maxDepth < 0
            || !Directory.Exists(root))
        {
            return results;
        }

        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root, 0));
        var visited = 0;

        while (pending.Count > 0 && results.Count < MaxResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            if (++visited > MaxVisitedDirectories)
            {
                break;
            }

            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    MatchCasing = MatchCasing.CaseInsensitive,
                    ReturnSpecialDirectories = false
                };

                foreach (var file in Directory.EnumerateFiles(directory, fileName, options))
                {
                    if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(file);
                        if (results.Count >= MaxResults)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A protected directory is not a fatal search error.
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(child);
                    if (SkippedDirectoryNames.Contains(name))
                    {
                        continue;
                    }

                    try
                    {
                        var attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    pending.Push((child, depth + 1));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Skip directories that disappear or deny enumeration.
            }
        }

        return results;
    }
}
