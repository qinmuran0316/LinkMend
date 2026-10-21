using LinkMend.Core.Abstractions;
using LinkMend.Core.Models;

namespace LinkMend.Tests.Support;

internal sealed class FakeShortcutStore : IShortcutStore
{
    public Dictionary<string, ShortcutLink> Links { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int ApplyCount { get; private set; }

    public bool CorruptFileAfterApply { get; set; }

    public string? ApplyFailure { get; set; }

    public ShortcutReadResult Read(string path)
    {
        return Links.TryGetValue(path, out var link)
            ? ShortcutReadResult.Ok(link with { FilePath = path })
            : ShortcutReadResult.Fail(ShortcutReadFailureKind.NotFound, "测试：没有登记这个快捷方式");
    }

    public void Apply(string path, ShortcutPatch patch)
    {
        if (ApplyFailure is not null)
        {
            throw new ShortcutWriteException(ApplyFailure);
        }

        if (!Links.TryGetValue(path, out var link))
        {
            throw new ShortcutWriteException("测试：没有登记这个快捷方式");
        }

        ApplyCount++;
        if (CorruptFileAfterApply)
        {
            File.WriteAllText(path, $"corrupted-{Guid.NewGuid():N}");
            return;
        }

        var updated = link;
        if (patch.TargetPath is not null)
        {
            updated = updated with { TargetPath = patch.TargetPath };
        }

        if (patch.WorkingDirectory is not null)
        {
            updated = updated with { WorkingDirectory = patch.WorkingDirectory };
        }

        if (patch.Icon is { } icon)
        {
            updated = icon.Clear
                ? updated with { IconPath = "", IconIndex = 0 }
                : updated with { IconPath = icon.Path, IconIndex = icon.Index };
        }

        Links[path] = updated;
        File.WriteAllText(path, $"updated-{Guid.NewGuid():N}");
    }
}
