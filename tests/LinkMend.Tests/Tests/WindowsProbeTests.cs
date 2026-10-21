using LinkMend.Core.Abstractions;
using LinkMend.Tests.Support;
using LinkMend.Windows;
using LinkMend.Windows.Search;

namespace LinkMend.Tests.Tests;

public sealed class WindowsProbeTests
{
    [Fact]
    public void ProbePath_DistinguishesFilesDirectoriesAndMissingPaths()
    {
        using var temp = new TempDirectory();
        var file = temp.CreateFile("a.txt");
        var directory = temp.CreateDirectory("folder");
        var missing = temp.Combine("missing.txt");
        var probe = new WindowsSystemProbe();

        Assert.Equal(ProbeOutcome.File, probe.ProbePath(file).Outcome);
        Assert.Equal(ProbeOutcome.Directory, probe.ProbePath(directory).Outcome);
        Assert.Equal(ProbeOutcome.NotFound, probe.ProbePath(missing).Outcome);
    }

    [Fact]
    public void ProbeIcon_ReturnsMissingForNonexistentIconPath()
    {
        using var temp = new TempDirectory();
        var probe = new WindowsSystemProbe();

        Assert.Equal(IconProbeOutcome.Missing, probe.ProbeIcon(temp.Combine("no.ico"), 0));
    }

    [Fact]
    public void ExecutableSearch_FindsNestedExecutableWithoutFollowingSymlinkDirectories()
    {
        using var temp = new TempDirectory();
        var nested = temp.CreateFile(@"Vendor\Demo\bin\demo.exe");
        var search = new WindowsExecutableSearch();

        var results = search.FindExactFileName(temp.Path, "demo.exe", 5, CancellationToken.None);

        Assert.Contains(nested, results, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecutableSearch_HonorsCancellation()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(@"Vendor\Demo\demo.exe");
        var search = new WindowsExecutableSearch();
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => search.FindExactFileName(temp.Path, "demo.exe", 5, source.Token));
    }
}
