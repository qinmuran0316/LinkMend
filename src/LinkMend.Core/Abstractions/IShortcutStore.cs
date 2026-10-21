using LinkMend.Core.Models;

namespace LinkMend.Core.Abstractions;

public enum ShortcutReadFailureKind
{
    AccessDenied,
    NotFound,
    Corrupt,
    Unsupported,
    Unknown
}

public sealed record ShortcutReadFailure(ShortcutReadFailureKind Kind, string Message);

public sealed record ShortcutReadResult(ShortcutLink? Link, ShortcutReadFailure? Failure)
{
    public bool Success => Link is not null;

    public static ShortcutReadResult Ok(ShortcutLink link) => new(link, null);

    public static ShortcutReadResult Fail(ShortcutReadFailureKind kind, string message) =>
        new(null, new ShortcutReadFailure(kind, message));
}

public sealed record IconUpdate(string Path, int Index, bool Clear = false);

public sealed record ShortcutPatch(
    string? TargetPath = null,
    string? WorkingDirectory = null,
    IconUpdate? Icon = null)
{
    public bool HasChanges =>
        TargetPath is not null || WorkingDirectory is not null || Icon is not null;
}

public sealed class ShortcutWriteException : Exception
{
    public ShortcutWriteException(string message)
        : base(message)
    {
    }

    public ShortcutWriteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public interface IShortcutStore
{
    ShortcutReadResult Read(string path);

    /// <summary>
    /// Writes requested fields and leaves all other shortcut properties untouched.
    /// Implementations should avoid replacing the original file until the new data
    /// has been written and read back successfully.
    /// </summary>
    void Apply(string path, ShortcutPatch patch);
}
