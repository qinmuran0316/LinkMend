using LinkMend.Core.Models;

namespace LinkMend.App.Services;

public static class ScanScopeBuilder
{
    public static IReadOnlyList<ScanRoot> Build(
        bool includeDesktop,
        bool includeStartMenu,
        IEnumerable<string> customFolders)
    {
        var roots = new List<ScanRoot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (includeDesktop)
        {
            Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "用户桌面");
            Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "公共桌面");
        }

        if (includeStartMenu)
        {
            Add(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "用户开始菜单");
            Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "所有用户开始菜单");
        }

        foreach (var folder in customFolders)
        {
            Add(folder, GetFolderLabel(folder));
        }

        return roots;

        void Add(string? path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return;
            }

            if (!Directory.Exists(fullPath) || !seen.Add(fullPath))
            {
                return;
            }

            roots.Add(new ScanRoot(fullPath, label));
        }
    }

    private static string GetFolderLabel(string path)
    {
        try
        {
            var trimmed = path.TrimEnd('\\', '/');
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? trimmed : name;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return path;
        }
    }
}
