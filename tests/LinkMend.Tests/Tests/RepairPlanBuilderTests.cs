using LinkMend.Core.Abstractions;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;
using LinkMend.Core.Repair;
using LinkMend.Tests.Support;

namespace LinkMend.Tests.Tests;

public sealed class RepairPlanBuilderTests
{
    private const string OldTarget = @"C:\Old\Demo\demo.exe";
    private const string NewTarget = @"C:\New\Demo\demo.exe";

    [Fact]
    public void Build_WithRelocationCandidate_UpdatesTargetWorkingDirectoryAndOldTargetIcon()
    {
        var probe = new FakeSystemProbe();
        probe.SetFile(NewTarget);
        probe.SetDirectory(@"C:\New\Demo");
        probe.SetIcon(NewTarget, IconProbeOutcome.Available);
        var item = CreateItem(
            target: OldTarget,
            workingDirectory: @"C:\Old\Demo",
            iconPath: OldTarget,
            diagnosis: RelocationDiagnosis());
        var candidate = new ResolutionCandidate(
            NewTarget,
            90,
            CandidateConfidence.High,
            new[] { "测试" },
            null);

        var plan = new RepairPlanBuilder(probe).Build(item, candidate);

        Assert.NotNull(plan);
        Assert.Equal(NewTarget, plan!.TargetPath);
        Assert.Equal(@"C:\New\Demo", plan.WorkingDirectory);
        Assert.NotNull(plan.Icon);
        Assert.Equal(NewTarget, plan.Icon!.Path);
        Assert.False(plan.Icon.Clear);
    }

    [Fact]
    public void Build_WithoutCandidateForMissingTarget_ReturnsNull()
    {
        var probe = new FakeSystemProbe();
        var item = CreateItem(OldTarget, diagnosis: RelocationDiagnosis());

        var plan = new RepairPlanBuilder(probe).Build(item, null);

        Assert.Null(plan);
    }

    [Fact]
    public void Build_MissingCustomIcon_ReplacesIconWithTargetResource()
    {
        var probe = new FakeSystemProbe();
        probe.SetFile(NewTarget);
        probe.SetIcon(NewTarget, IconProbeOutcome.Available);
        var diagnosis = DiagnosisResult.FromIssues(new[]
        {
            new ShortcutIssue(
                IssueCodes.IconSourceMissing,
                IssueSeverity.Warning,
                "图标文件不存在",
                "测试",
                Array.Empty<string>(),
                IssueHandling.AutoFix)
        });
        var item = CreateItem(NewTarget, iconPath: @"C:\Icons\missing.ico", diagnosis: diagnosis);

        var plan = new RepairPlanBuilder(probe).Build(item, null);

        Assert.NotNull(plan);
        Assert.Null(plan!.TargetPath);
        Assert.NotNull(plan.Icon);
        Assert.Equal(NewTarget, plan.Icon!.Path);
        Assert.Equal(0, plan.Icon.Index);
    }

    [Fact]
    public void Build_IconNotUsableAtTarget_ClearsCustomIcon()
    {
        var probe = new FakeSystemProbe();
        probe.SetFile(@"C:\Docs\guide.pdf");
        probe.SetIcon(@"C:\Docs\guide.pdf", IconProbeOutcome.Unknown);
        var diagnosis = DiagnosisResult.FromIssues(new[]
        {
            new ShortcutIssue(
                IssueCodes.IconSourceMissing,
                IssueSeverity.Warning,
                "图标文件不存在",
                "测试",
                Array.Empty<string>(),
                IssueHandling.AutoFix)
        });
        var item = CreateItem(@"C:\Docs\guide.pdf", iconPath: @"C:\Icons\missing.ico", diagnosis: diagnosis);

        var plan = new RepairPlanBuilder(probe).Build(item, null);

        Assert.NotNull(plan);
        Assert.NotNull(plan!.Icon);
        Assert.True(plan.Icon!.Clear);
    }

    [Fact]
    public void Build_WithoutContentHash_RefusesAutomaticPlan()
    {
        var probe = new FakeSystemProbe();
        probe.SetFile(NewTarget);
        var item = CreateItem(OldTarget, diagnosis: RelocationDiagnosis()) with { ContentHash = "" };
        var candidate = new ResolutionCandidate(NewTarget, 90, CandidateConfidence.High, Array.Empty<string>(), null);

        var plan = new RepairPlanBuilder(probe).Build(item, candidate);

        Assert.Null(plan);
    }

    [Fact]
    public void Build_ManualOnlyProblemWithCandidate_DoesNotRetarget()
    {
        var probe = new FakeSystemProbe();
        probe.SetFile(NewTarget);
        var diagnosis = DiagnosisResult.FromIssues(new[]
        {
            new ShortcutIssue(IssueCodes.TargetUnavailable, IssueSeverity.Warning,
                "磁盘离线", "测试", Array.Empty<string>(), IssueHandling.Manual)
        });
        var item = CreateItem(OldTarget, diagnosis: diagnosis);
        var candidate = new ResolutionCandidate(NewTarget, 90, CandidateConfidence.High, Array.Empty<string>(), null);

        Assert.Null(new RepairPlanBuilder(probe).Build(item, candidate));
    }

    [Fact]
    public void Build_AutoFixWithoutHash_IsNotActionable()
    {
        var diagnosis = DiagnosisResult.FromIssues(new[]
        {
            new ShortcutIssue(IssueCodes.IconSourceMissing, IssueSeverity.Warning,
                "图标丢失", "测试", Array.Empty<string>(), IssueHandling.AutoFix)
        });
        var item = CreateItem(NewTarget, diagnosis: diagnosis) with { ContentHash = "" };

        Assert.False(item.CanBuildAutomaticPlan);
        Assert.Null(new RepairPlanBuilder(new FakeSystemProbe()).Build(item, null));
    }

    [Fact]
    public void Build_ManualOnlyProblem_ReturnsNull()
    {
        var probe = new FakeSystemProbe();
        probe.SetFile(NewTarget);
        var diagnosis = DiagnosisResult.FromIssues(new[]
        {
            new ShortcutIssue(
                IssueCodes.DataPathConflict,
                IssueSeverity.Warning,
                "多个数据目录配置",
                "测试",
                Array.Empty<string>(),
                IssueHandling.Manual)
        });
        var item = CreateItem(NewTarget, diagnosis: diagnosis);

        var plan = new RepairPlanBuilder(probe).Build(item, null);

        Assert.Null(plan);
    }

    private static DiagnosisResult RelocationDiagnosis() =>
        DiagnosisResult.FromIssues(new[]
        {
            new ShortcutIssue(
                IssueCodes.TargetMissing,
                IssueSeverity.Error,
                "目标不存在",
                "测试",
                Array.Empty<string>(),
                IssueHandling.NeedsRelocation)
        });

    private static ScannedShortcut CreateItem(
        string target,
        string workingDirectory = "",
        string iconPath = "",
        DiagnosisResult? diagnosis = null)
    {
        var link = new ShortcutLink
        {
            FilePath = @"C:\Links\Demo.lnk",
            DisplayName = "Demo",
            TargetPath = target,
            WorkingDirectory = workingDirectory,
            IconPath = iconPath
        };

        return new ScannedShortcut(
            link,
            PathResolver.Resolve(link),
            "0123456789abcdef",
            "测试",
            diagnosis ?? DiagnosisResult.Healthy);
    }
}
