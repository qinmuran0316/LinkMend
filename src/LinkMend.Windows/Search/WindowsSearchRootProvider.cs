using LinkMend.Core.Abstractions;

namespace LinkMend.Windows.Search;

public sealed class WindowsSearchRootProvider : ISearchRootProvider
{
    private readonly ISystemProbe _probe;

    public WindowsSearchRootProvider(ISystemProbe probe)
    {
        _probe = probe;
    }

    public IReadOnlyList<string> GetDefaultRoots()
    {
        var roots = new List<string>();
        var candidateNames = new[]
        {
            "Program Files",
            "Program Files (x86)",
            "Programs",
            "Apps",
            "Software",
            "Tools",
            "PortableApps"
        };

        foreach (var driveRoot in _probe.GetReadyFixedDriveRoots())
        {
            foreach (var name in candidateNames)
            {
                Add(Path.Combine(driveRoot, name));
            }
        }

        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");
        Add(localPrograms);

        return roots;

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (_probe.ProbePath(path).Outcome != ProbeOutcome.Directory)
                {
                    return;
                }

                var fullPath = Path.GetFullPath(path);
                if (!roots.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                {
                    roots.Add(fullPath);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Ignore malformed environment paths.
            }
        }
    }
}
