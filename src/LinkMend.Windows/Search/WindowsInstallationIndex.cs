using LinkMend.Core.Abstractions;
using LinkMend.Core.Paths;
using Microsoft.Win32;

namespace LinkMend.Windows.Search;

/// <summary>
/// Uses Windows App Paths and uninstall records as high-confidence hints.
/// A hint is still only a candidate; the user makes the final choice.
/// </summary>
public sealed class WindowsInstallationIndex : IInstallationIndex
{
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly Dictionary<string, IReadOnlyList<InstallationHint>> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public IReadOnlyList<InstallationHint> FindByExecutableName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Array.Empty<InstallationHint>();
        }

        lock (_gate)
        {
            if (_cache.TryGetValue(fileName, out var cached))
            {
                return cached;
            }

            var hints = FindCore(fileName);
            _cache[fileName] = hints;
            return hints;
        }
    }

    private static IReadOnlyList<InstallationHint> FindCore(string fileName)
    {
        var hints = new Dictionary<string, InstallationHint>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hive, view) in EnumerateRegistryLocations())
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                ReadAppPaths(baseKey, fileName, hints);
                ReadUninstallEntries(baseKey, fileName, hints);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                // Registry access is optional.
            }
        }

        return hints.Values
            .OrderBy(hint => hint.Source, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(hint => hint.ExecutablePath, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<(RegistryHive Hive, RegistryView View)> EnumerateRegistryLocations()
    {
        yield return (RegistryHive.CurrentUser, RegistryView.Registry64);
        yield return (RegistryHive.CurrentUser, RegistryView.Registry32);
        yield return (RegistryHive.LocalMachine, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, RegistryView.Registry32);
    }

    private static void ReadAppPaths(
        RegistryKey baseKey,
        string fileName,
        Dictionary<string, InstallationHint> hints)
    {
        try
        {
            using var key = baseKey.OpenSubKey($@"{AppPathsKey}\{fileName}");
            if (key is null)
            {
                return;
            }

            var pathValue = key.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(pathValue))
            {
                AddHint(pathValue, "App Paths 注册表", fileName, hints);
            }

            var directoryValue = key.GetValue("Path") as string;
            if (!string.IsNullOrWhiteSpace(directoryValue))
            {
                var normalizedDirectory = NormalizePath(directoryValue);
                if (!string.IsNullOrWhiteSpace(normalizedDirectory))
                {
                    AddHint(Path.Combine(normalizedDirectory, fileName), "App Paths 注册表", fileName, hints);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Optional hint source.
        }
    }

    private static void ReadUninstallEntries(
        RegistryKey baseKey,
        string fileName,
        Dictionary<string, InstallationHint> hints)
    {
        try
        {
            using var uninstall = baseKey.OpenSubKey(UninstallKey);
            if (uninstall is null)
            {
                return;
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(subKeyName);
                if (entry is null)
                {
                    continue;
                }

                var displayName = Clean(entry.GetValue("DisplayName") as string);
                var source = string.IsNullOrWhiteSpace(displayName)
                    ? "已安装程序记录"
                    : $"已安装程序：{displayName}";

                var displayIcon = entry.GetValue("DisplayIcon") as string;
                if (!string.IsNullOrWhiteSpace(displayIcon))
                {
                    var iconExecutable = ExtractExecutablePath(displayIcon);
                    if (!string.IsNullOrWhiteSpace(iconExecutable)
                        && string.Equals(Path.GetFileName(iconExecutable), fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        AddHint(iconExecutable, source, fileName, hints);
                    }
                }

                var installLocation = NormalizePath(entry.GetValue("InstallLocation") as string);
                if (string.IsNullOrWhiteSpace(installLocation))
                {
                    continue;
                }

                AddHint(Path.Combine(installLocation, fileName), source, fileName, hints);
                AddHint(Path.Combine(installLocation, "bin", fileName), source, fileName, hints);
                AddHint(Path.Combine(installLocation, "app", fileName), source, fileName, hints);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Optional hint source.
        }
    }

    private static void AddHint(
        string candidate,
        string source,
        string expectedFileName,
        Dictionary<string, InstallationHint> hints)
    {
        var normalized = NormalizePath(candidate);
        if (string.IsNullOrWhiteSpace(normalized)
            || !string.Equals(Path.GetFileName(normalized), expectedFileName, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(normalized))
        {
            return;
        }

        hints.TryAdd(normalized, new InstallationHint(normalized, source));
    }

    private static string ExtractExecutablePath(string value)
    {
        var text = value.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 0 ? NormalizePath(text[1..end]) : "";
        }

        var comma = text.LastIndexOf(',');
        if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out _))
        {
            text = text[..comma];
        }

        return NormalizePath(text);
    }

    private static string NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(PathResolver.Expand(value.Trim().Trim('"')));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }

    private static string Clean(string? value) => value?.Trim() ?? "";
}
