using LinkMend.Core.Abstractions;
using LinkMend.Core.Models;
using LinkMend.Core.Paths;

namespace LinkMend.Core.Repair;

public sealed class RepairPlanBuilder
{
    private static readonly HashSet<string> AutoIconIssueCodes = new(StringComparer.Ordinal)
    {
        IssueCodes.IconSourceMissing,
        IssueCodes.IconIndexUnavailable,
        IssueCodes.IconUnavailable
    };

    private readonly ISystemProbe _probe;

    public RepairPlanBuilder(ISystemProbe probe)
    {
        _probe = probe;
    }

    public RepairPlan? Build(
        ScannedShortcut item,
        ResolutionCandidate? candidate,
        bool fixWorkingDirectory = true,
        bool fixIcon = true)
    {
        if (!item.CanBuildAutomaticPlan)
        {
            return null;
        }

        var needsRelocation = item.Diagnosis.NeedsRelocation;
        if (candidate is not null && !needsRelocation)
        {
            return null;
        }

        if (needsRelocation && candidate is null)
        {
            return null;
        }

        var oldTarget = item.Paths.TargetPath;
        var targetChange = "";
        if (candidate is not null
            && !PathResolver.AreSame(candidate.ExecutablePath, oldTarget))
        {
            targetChange = candidate.ExecutablePath;
        }

        if (!string.IsNullOrWhiteSpace(targetChange))
        {
            var candidateProbe = _probe.ProbePath(targetChange);
            if (!candidateProbe.Exists)
            {
                return null;
            }
        }

        var effectiveTarget = string.IsNullOrWhiteSpace(targetChange)
            ? oldTarget
            : targetChange;
        var targetProbe = string.IsNullOrWhiteSpace(effectiveTarget)
            ? new PathProbe(ProbeOutcome.NotFound)
            : _probe.ProbePath(effectiveTarget);

        string? newWorkingDirectory = null;
        if (fixWorkingDirectory && targetProbe.Exists && !string.IsNullOrWhiteSpace(effectiveTarget))
        {
            var hasWorkingDirectoryIssue = item.Diagnosis.Issues.Any(issue =>
                issue.Code == IssueCodes.WorkingDirectoryMissing
                && issue.Handling == IssueHandling.AutoFix);

            var oldWorking = item.Paths.WorkingDirectory;
            var workingPointsAtOldLocation =
                !string.IsNullOrWhiteSpace(oldTarget)
                && (PathResolver.AreSame(oldWorking, oldTarget)
                    || PathResolver.AreSame(oldWorking, GetParentDirectory(oldTarget)));

            if (hasWorkingDirectoryIssue
                || (!string.IsNullOrWhiteSpace(targetChange) && workingPointsAtOldLocation))
            {
                var proposed = PathResolver.GetContainingDirectory(
                    effectiveTarget,
                    targetProbe.Outcome == ProbeOutcome.Directory);

                if (!string.IsNullOrWhiteSpace(proposed)
                    && !PathResolver.AreSame(proposed, oldWorking)
                    && _probe.ProbePath(proposed).Outcome == ProbeOutcome.Directory)
                {
                    newWorkingDirectory = proposed;
                }
            }
        }

        IconUpdate? iconUpdate = null;
        if (fixIcon && targetProbe.Exists)
        {
            var hasIconIssue = item.Diagnosis.Issues.Any(issue =>
                AutoIconIssueCodes.Contains(issue.Code)
                && issue.Handling == IssueHandling.AutoFix);

            var iconWasOldTarget =
                !string.IsNullOrWhiteSpace(oldTarget)
                && PathResolver.AreSame(item.Paths.IconPath, oldTarget);

            if (hasIconIssue || (!string.IsNullOrWhiteSpace(targetChange) && iconWasOldTarget))
            {
                iconUpdate = ChooseIcon(effectiveTarget, targetProbe.Outcome);
            }
        }

        if (string.IsNullOrWhiteSpace(targetChange)
            && newWorkingDirectory is null
            && iconUpdate is null)
        {
            return null;
        }

        return new RepairPlan(
            item,
            item.ContentHash,
            string.IsNullOrWhiteSpace(targetChange) ? null : targetChange,
            newWorkingDirectory,
            iconUpdate);
    }

    private IconUpdate ChooseIcon(string target, ProbeOutcome targetOutcome)
    {
        if (targetOutcome == ProbeOutcome.File
            && PathResolver.IsIconCapable(target)
            && _probe.ProbeIcon(target, 0) == IconProbeOutcome.Available)
        {
            return new IconUpdate(target, 0);
        }

        return new IconUpdate("", 0, Clear: true);
    }

    private static string GetParentDirectory(string path)
    {
        try
        {
            return Path.GetDirectoryName(path) ?? "";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }
}
