using LinkMend.Core.Abstractions;
using LinkMend.Core.Diagnosis;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;
using LinkMend.Tests.Support;

namespace LinkMend.Tests.Tests;

public sealed class ShortcutDiagnoserTests
{
    private const string Target = @"C:\Apps\Demo\demo.exe";
    private const string LinkPath = @"C:\Links\Demo.lnk";

    [Fact]
    public void Diagnose_ExistingTargetWithoutOtherProblems_IsHealthy()
    {
        var probe = new FakeSystemProbe();
        probe.SetFile(Target);
        var (link, paths) = CreateLink(Target);

        var diagnosis = new ShortcutDiagnoser(probe).Diagnose(link, paths);

        Assert.Equal(ShortcutHealth.Healthy, diagnosis.Health);
        Assert.Empty(diagnosis.Issues);
    }

    [Fact]
    public void Diagnose_FolderTarget_IsHealthy()
    {
        var probe = new FakeSystemProbe();
        probe.SetDirectory(@"C:\Apps\Demo");
        var (link, paths) = CreateLink(@"C:\Apps\Demo");

        var diagnosis = new ShortcutDiagnoser(probe).Diagnose(link, paths);

        Assert.Equal(ShortcutHealth.Healthy, diagnosis.Health);
    }

    [Fact]
    public void Diagnose_MissingTarget_IsRelocatableAndExplainsWhiteIcon()
    {
        var probe = new FakeSystemProbe();
        var (link, paths) = CreateLink(Target);

        var diagnosis = new ShortcutDiagnoser(probe).Diagnose(link, paths);

        Assert.Equal(ShortcutHealth.Broken, diagnosis.Health);
        Assert.True(diagnosis.NeedsRelocation);
        var issue = Assert.Single(diagnosis.Issues);
        Assert.Equal(IssueCodes.TargetMissing, issue.Code);
        Assert.Contains(issue.Evidence, item => item.Contains("白图标", StringComparison.Ordinal));
    }

    [Fact]
    public void Diagnose_UnavailableVolume_IsNotClassifiedAsPermanentlyMissing()
    {
        var probe = new FakeSystemProbe();
        probe.SetOutcome(Target, ProbeOutcome.VolumeUnavailable, "磁盘未就绪");
        var (link, paths) = CreateLink(Target);

        var diagnosis = new ShortcutDiagnoser(probe).Diagnose(link, paths);

        Assert.Equal(ShortcutHealth.NeedsAttention, diagnosis.Health);
        Assert.False(diagnosis.NeedsRelocation);
        var issue = Assert.Single(diagnosis.Issues);
        Assert.Equal(IssueCodes.TargetUnavailable, issue.Code);
        Assert.Equal(IssueHandling.Manual, issue.Handling);
    }

    [Fact]
    public void Diagnose_AccessDenied_ExplainsThatExistenceIsUnknown()
    {
        var probe = new FakeSystemProbe();
        probe.SetOutcome(Target, ProbeOutcome.AccessDenied, "访问被拒绝");
        var (link, paths) = CreateLink(Target);

        var diagnosis = new ShortcutDiagnoser(probe).Diagnose(link, paths);

        var issue = Assert.Single(diagnosis.Issues);
        Assert.Equal(IssueCodes.TargetAccessDenied, issue.Code);
        Assert.Contains("不能判断", issue.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnose_MissingWorkingDirectory_IsAutoFixable()
    {
        var probe = new FakeSystemProbe();
        probe.SetFile(Target);
        var (link, paths) = CreateLink(Target, workingDirectory: @"C:\Apps\Demo\old-working");
        probe.SetDirectory(@"C:\Apps\Demo");

        var diagnosis = new ShortcutDiagnoser(probe).Diagnose(link, paths);

        Assert.Equal(ShortcutHealth.NeedsAttention, diagnosis.Health);
        var issue = Assert.Single(diagnosis.Issues);
        Assert.Equal(IssueCodes.WorkingDirectoryMissing, issue.Code);
        Assert.Equal(IssueHandling.AutoFix, issue.Handling);
    }

    [Fact]
    public void Diagnose_MissingIconFile_IsAutoFixableWhenTargetExists()
    {
        var probe = new FakeSystemProbe();
        probe.SetFile(Target);
        var (link, paths) = CreateLink(
            Target,
            iconPath: @"C:\Icons\missing.ico",
            iconIndex: 2);

        var diagnosis = new ShortcutDiagnoser(probe).Diagnose(link, paths);

        var issue = Assert.Single(diagnosis.Issues);
        Assert.Equal(IssueCodes.IconSourceMissing, issue.Code);
        Assert.Equal(IssueHandling.AutoFix, issue.Handling);
        Assert.Contains(issue.Evidence, item => item.Contains("图标索引：2", StringComparison.Ordinal));
    }

    [Fact]
    public void Diagnose_InvalidIconIndex_IsReportedSeparately()
    {
        var probe = new FakeSystemProbe();
        probe.SetFile(Target);
        const string icon = @"C:\Icons\pack.ico";
        probe.SetFile(icon);
        probe.SetIcon(icon, IconProbeOutcome.IndexUnavailable);
        var (link, paths) = CreateLink(Target, iconPath: icon, iconIndex: 9);

        var diagnosis = new ShortcutDiagnoser(probe).Diagnose(link, paths);

        var issue = Assert.Single(diagnosis.Issues);
        Assert.Equal(IssueCodes.IconIndexUnavailable, issue.Code);
        Assert.Equal(IssueHandling.AutoFix, issue.Handling);
    }

    [Fact]
    public void Diagnose_EmptyTargetWithExistingIcon_IsTreatedAsUnsupportedSpecialShortcut()
    {
        var probe = new FakeSystemProbe();
        const string icon = @"C:\Windows\System32\imageres.dll";
        probe.SetFile(icon);
        var (link, paths) = CreateLink("", iconPath: icon);

        var diagnosis = new ShortcutDiagnoser(probe).Diagnose(link, paths);

        Assert.Equal(ShortcutHealth.Unsupported, diagnosis.Health);
        Assert.Contains(diagnosis.Issues, issue => issue.Code == IssueCodes.SpecialShortcut);
    }

    [Fact]
    public void Diagnose_EmptyTargetWithoutIcon_IsBrokenAndNotAutoFixed()
    {
        var probe = new FakeSystemProbe();
        var (link, paths) = CreateLink("");

        var diagnosis = new ShortcutDiagnoser(probe).Diagnose(link, paths);

        Assert.Equal(ShortcutHealth.Broken, diagnosis.Health);
        Assert.False(diagnosis.NeedsRelocation);
        var issue = Assert.Single(diagnosis.Issues);
        Assert.Equal(IssueCodes.TargetMissing, issue.Code);
        Assert.Equal(IssueHandling.Manual, issue.Handling);
    }

    [Fact]
    public void Diagnose_ConflictingDataDirectories_AddsWarningWithoutAutoFix()
    {
        var probe = new FakeSystemProbe();
        probe.SetFile(Target);
        probe.SetDirectory(@"D:\Profiles\A");
        probe.SetDirectory(@"D:\Profiles\B");

        var first = CreateLink(Target, arguments: @"--user-data-dir=""D:\Profiles\A""", linkPath: @"C:\Links\One.lnk");
        var second = CreateLink(Target, arguments: @"--user-data-dir=""D:\Profiles\B""", linkPath: @"C:\Links\Two.lnk");
        var peers = new[]
        {
            new ShortcutPeer(first.Link, first.Paths),
            new ShortcutPeer(second.Link, second.Paths)
        };

        var diagnosis = new ShortcutDiagnoser(probe).Diagnose(first.Link, first.Paths, peers);

        var issue = Assert.Single(diagnosis.Issues, item => item.Code == IssueCodes.DataPathConflict);
        Assert.Equal(IssueHandling.Manual, issue.Handling);
        Assert.Contains(issue.Evidence, item => item.Contains("One", StringComparison.Ordinal));
        Assert.Contains(issue.Evidence, item => item.Contains("Two", StringComparison.Ordinal));

        var withoutDataFlag = CreateLink(Target, linkPath: @"C:\Links\Three.lnk");
        var withoutFlagDiagnosis = new ShortcutDiagnoser(probe).Diagnose(
            withoutDataFlag.Link,
            withoutDataFlag.Paths,
            new[]
            {
                new ShortcutPeer(first.Link, first.Paths),
                new ShortcutPeer(second.Link, second.Paths),
                new ShortcutPeer(withoutDataFlag.Link, withoutDataFlag.Paths)
            });
        Assert.DoesNotContain(withoutFlagDiagnosis.Issues, item => item.Code == IssueCodes.DataPathConflict);
    }

    [Fact]
    public void Diagnose_DataDirectoryOnUnavailableVolume_IsWarningOnly()
    {
        var probe = new FakeSystemProbe();
        probe.SetFile(Target);
        probe.SetOutcome(@"D:\Offline\Profile", ProbeOutcome.VolumeUnavailable, "磁盘未就绪");
        var (link, paths) = CreateLink(
            Target,
            arguments: @"--user-data-dir=""D:\Offline\Profile""");

        var diagnosis = new ShortcutDiagnoser(probe).Diagnose(link, paths);

        var issue = Assert.Single(diagnosis.Issues);
        Assert.Equal(IssueCodes.DataPathUnavailable, issue.Code);
        Assert.Equal(IssueHandling.Manual, issue.Handling);
    }

    private static (ShortcutLink Link, ResolvedShortcutPaths Paths) CreateLink(
        string target,
        string workingDirectory = "",
        string iconPath = "",
        int iconIndex = 0,
        string arguments = "",
        string linkPath = LinkPath)
    {
        var link = new ShortcutLink
        {
            FilePath = linkPath,
            DisplayName = Path.GetFileNameWithoutExtension(linkPath),
            TargetPath = target,
            WorkingDirectory = workingDirectory,
            IconPath = iconPath,
            IconIndex = iconIndex,
            Arguments = arguments
        };

        return (link, PathResolver.Resolve(link));
    }
}
