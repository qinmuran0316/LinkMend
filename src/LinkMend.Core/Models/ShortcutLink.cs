namespace LinkMend.Core.Models;

/// <summary>
/// A shortcut exactly as it was read from the .lnk file. Paths are stored raw:
/// they may contain environment variables, relative segments, or surrounding quotes.
/// </summary>
public sealed record ShortcutLink
{
    public required string FilePath { get; init; }

    public string DisplayName { get; init; } = "";

    public string Description { get; init; } = "";

    public string TargetPath { get; init; } = "";

    public string Arguments { get; init; } = "";

    public string WorkingDirectory { get; init; } = "";

    public string IconPath { get; init; } = "";

    public int IconIndex { get; init; }

    public string FileName => Path.GetFileName(FilePath);
}
