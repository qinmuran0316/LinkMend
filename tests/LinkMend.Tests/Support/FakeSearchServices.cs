using LinkMend.Core.Abstractions;

namespace LinkMend.Tests.Support;

internal sealed class FakeInstallationIndex : IInstallationIndex
{
    public Dictionary<string, List<InstallationHint>> Hints { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<InstallationHint> FindByExecutableName(string fileName) =>
        Hints.TryGetValue(fileName, out var hints)
            ? hints
            : Array.Empty<InstallationHint>();
}

internal sealed class FakeExecutableSearch : IExecutableSearch
{
    public Dictionary<string, List<string>> ResultsByRoot { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<(string Root, string FileName)> Calls { get; } = new();

    public IReadOnlyList<string> FindExactFileName(
        string root,
        string fileName,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((root, fileName));
        return ResultsByRoot.TryGetValue(root, out var results)
            ? results
            : Array.Empty<string>();
    }
}

internal sealed class FakeSearchRootProvider : ISearchRootProvider
{
    public List<string> Roots { get; } = new();

    public IReadOnlyList<string> GetDefaultRoots() => Roots;
}
