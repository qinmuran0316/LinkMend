namespace LinkMend.Core.Abstractions;

public sealed record InstallationHint(string ExecutablePath, string Source);

public interface IInstallationIndex
{
    IReadOnlyList<InstallationHint> FindByExecutableName(string fileName);
}

public interface IExecutableSearch
{
    IReadOnlyList<string> FindExactFileName(string root, string fileName, int maxDepth, CancellationToken cancellationToken);
}

public interface ISearchRootProvider
{
    IReadOnlyList<string> GetDefaultRoots();
}
