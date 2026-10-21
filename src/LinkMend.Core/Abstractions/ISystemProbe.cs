namespace LinkMend.Core.Abstractions;

public enum ProbeOutcome
{
    File,
    Directory,
    NotFound,
    AccessDenied,
    VolumeUnavailable,
    NetworkUnavailable,
    InvalidPath,
    ShellNamespace,
    Unknown
}

public sealed record PathProbe(ProbeOutcome Outcome, string? Detail = null)
{
    public bool Exists => Outcome is ProbeOutcome.File or ProbeOutcome.Directory;
}

public enum IconProbeOutcome
{
    Available,
    Missing,
    IndexUnavailable,
    Unknown
}

public sealed record FileMetadata(
    string ProductName,
    string CompanyName,
    string FileDescription,
    string OriginalFilename,
    long Length)
{
    public static FileMetadata Empty { get; } = new("", "", "", "", 0);
}

/// <summary>
/// Read-only access to the parts of Windows that the diagnosis engine needs.
/// Implementations must never start the target program.
/// </summary>
public interface ISystemProbe
{
    PathProbe ProbePath(string path);

    FileMetadata? ReadMetadata(string path);

    IconProbeOutcome ProbeIcon(string iconPath, int iconIndex);

    IReadOnlyList<string> GetReadyFixedDriveRoots();
}
