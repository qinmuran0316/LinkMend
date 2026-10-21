using LinkMend.Core.Abstractions;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;
using LinkMend.Core.Relocation;
using LinkMend.Tests.Support;

namespace LinkMend.Tests.Tests;

public sealed class RelocationServiceTests
{
    private const string OldTarget = @"C:\Old\Demo\demo.exe";
    private const string HintPath = @"C:\Program Files\Vendor\Demo\demo.exe";
    private const string SearchPath = @"C:\Portable\Demo\demo.exe";

    [Fact]
    public void FindCandidates_RanksInstallationHintAbovePlainFilenameMatch()
    {
        var fixture = CreateFixture();
        fixture.Probe.SetFile(HintPath);
        fixture.Probe.SetFile(SearchPath);
        fixture.Hints.Hints["demo.exe"] = new List<InstallationHint>
        {
            new(HintPath, "App Paths 注册表")
        };
        fixture.Search.ResultsByRoot[@"C:\Portable"] = new List<string> { SearchPath };

        var result = fixture.Service.FindCandidates(fixture.Shortcut, Array.Empty<string>());

        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(HintPath, result.Candidates[0].ExecutablePath);
        Assert.Equal(CandidateConfidence.High, result.Candidates[0].Confidence);
        Assert.Contains(result.Candidates[0].Reasons, reason => reason.Contains("App Paths", StringComparison.Ordinal));
        Assert.Equal(SearchPath, result.Candidates[1].ExecutablePath);
        Assert.Equal(CandidateConfidence.Medium, result.Candidates[1].Confidence);
    }

    [Fact]
    public void FindCandidates_UsesProductAndPublisherEvidence()
    {
        var fixture = CreateFixture();
        fixture.Probe.SetFile(SearchPath, new FileMetadata("Demo Pro", "Vendor Ltd", "Demo", "demo.exe", 1024));
        fixture.Probe.Metadata[OldTarget] = new FileMetadata("Demo Pro", "Vendor Ltd", "Demo", "demo.exe", 1000);
        fixture.Search.ResultsByRoot[@"C:\Portable"] = new List<string> { SearchPath };

        var result = fixture.Service.FindCandidates(fixture.Shortcut, Array.Empty<string>());

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(CandidateConfidence.High, candidate.Confidence);
        Assert.Contains(candidate.Reasons, reason => reason.Contains("产品名称一致", StringComparison.Ordinal));
        Assert.Contains(candidate.Reasons, reason => reason.Contains("发布者一致", StringComparison.Ordinal));
    }

    [Fact]
    public void FindCandidates_DeduplicatesPathFoundByRegistryAndSearch()
    {
        var fixture = CreateFixture();
        fixture.Probe.SetFile(HintPath);
        fixture.Hints.Hints["demo.exe"] = new List<InstallationHint>
        {
            new(HintPath, "App Paths 注册表")
        };
        fixture.Search.ResultsByRoot[@"C:\Portable"] = new List<string> { HintPath };

        var result = fixture.Service.FindCandidates(fixture.Shortcut, Array.Empty<string>());

        Assert.Single(result.Candidates);
    }

    [Fact]
    public void FindCandidates_SearchesCustomDefaultAndOriginalRoots()
    {
        var fixture = CreateFixture();
        fixture.SearchRoots.Roots.Add(@"C:\Portable");
        var customRoot = @"D:\MyApps";
        fixture.Probe.SetDirectory(customRoot);

        var result = fixture.Service.FindCandidates(fixture.Shortcut, new[] { customRoot });
        var calledRoots = fixture.Search.Calls.Select(call => call.Root).ToArray();

        Assert.Contains(@"C:\Old\Demo", calledRoots, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(@"C:\Links", calledRoots, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(@"C:\Portable", calledRoots, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(customRoot, calledRoots, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(result.Candidates);
        Assert.NotEmpty(result.SearchedRoots);
        Assert.NotNull(result.Notice);
    }

    [Fact]
    public void FindCandidates_IgnoresExecutablesThatNoLongerExist()
    {
        var fixture = CreateFixture();
        fixture.Search.ResultsByRoot[@"C:\Portable"] = new List<string> { @"C:\Portable\Demo\demo.exe" };

        var result = fixture.Service.FindCandidates(fixture.Shortcut, Array.Empty<string>());

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void FindCandidates_HonorsCancellation()
    {
        var fixture = CreateFixture();
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => fixture.Service.FindCandidates(fixture.Shortcut, Array.Empty<string>(), source.Token));
    }

    private static Fixture CreateFixture()
    {
        var probe = new FakeSystemProbe();
        probe.SetDirectory(@"C:\Old\Demo");
        probe.SetDirectory(@"C:\Links");
        probe.SetDirectory(@"C:\Portable");

        var hints = new FakeInstallationIndex();
        var search = new FakeExecutableSearch();
        var roots = new FakeSearchRootProvider();
        roots.Roots.Add(@"C:\Portable");
        var service = new RelocationService(probe, hints, search, roots, TimeSpan.FromSeconds(2));

        var link = new ShortcutLink
        {
            FilePath = @"C:\Links\Demo.lnk",
            DisplayName = "Demo",
            TargetPath = OldTarget
        };
        var paths = PathResolver.Resolve(link);
        var diagnosis = DiagnosisResult.FromIssues(new[]
        {
            new ShortcutIssue(
                IssueCodes.TargetMissing,
                IssueSeverity.Error,
                "目标不存在",
                "测试",
                Array.Empty<string>(),
                IssueHandling.NeedsRelocation)
        });
        var shortcut = new ScannedShortcut(link, paths, "hash", "测试", diagnosis);

        return new Fixture(probe, hints, search, roots, service, shortcut);
    }

    private sealed record Fixture(
        FakeSystemProbe Probe,
        FakeInstallationIndex Hints,
        FakeExecutableSearch Search,
        FakeSearchRootProvider SearchRoots,
        RelocationService Service,
        ScannedShortcut Shortcut);
}
