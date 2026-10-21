using LinkMend.Core.Abstractions;

namespace LinkMend.Tests.Support;

internal sealed class FakeSystemProbe : ISystemProbe
{
    public Dictionary<string, PathProbe> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, FileMetadata> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, IconProbeOutcome> Icons { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> ReadyDrives { get; } = new();

    public void SetFile(string path, FileMetadata? metadata = null)
    {
        Paths[path] = new PathProbe(ProbeOutcome.File);
        if (metadata is not null)
        {
            Metadata[path] = metadata;
        }
    }

    public void SetDirectory(string path) => Paths[path] = new PathProbe(ProbeOutcome.Directory);

    public void SetOutcome(string path, ProbeOutcome outcome, string? detail = null) =>
        Paths[path] = new PathProbe(outcome, detail);

    public void SetIcon(string path, IconProbeOutcome outcome) => Icons[path] = outcome;

    public PathProbe ProbePath(string path) =>
        Paths.TryGetValue(path, out var probe)
            ? probe
            : new PathProbe(ProbeOutcome.NotFound, "测试：路径不存在");

    public FileMetadata? ReadMetadata(string path) =>
        Metadata.TryGetValue(path, out var metadata) ? metadata : null;

    public IconProbeOutcome ProbeIcon(string iconPath, int iconIndex) =>
        Icons.TryGetValue(iconPath, out var outcome) ? outcome : IconProbeOutcome.Unknown;

    public IReadOnlyList<string> GetReadyFixedDriveRoots() => ReadyDrives;
}
