using LinkMend.Core.Abstractions;
using LinkMend.Core.Backup;
using LinkMend.Core.Models;

namespace LinkMend.Core.Repair;

public sealed record RepairPlan(
    ScannedShortcut Item,
    string ExpectedHash,
    string? TargetPath,
    string? WorkingDirectory,
    IconUpdate? Icon)
{
    public bool HasChanges =>
        TargetPath is not null || WorkingDirectory is not null || Icon is not null;

    public ShortcutPatch ToPatch() => new(TargetPath, WorkingDirectory, Icon);

    public string FileName => Item.Link.FileName;
}

public enum OperationItemOutcome
{
    Repaired,
    Skipped,
    Failed,
    Restored
}

public sealed record RepairItemResult(
    string ShortcutPath,
    string FileName,
    OperationItemOutcome Outcome,
    string Message);

public sealed record RepairOutcome(
    string? BatchId,
    IReadOnlyList<RepairItemResult> Items,
    IReadOnlyList<UndoConflict> Conflicts)
{
    public int RepairedCount => Items.Count(item => item.Outcome == OperationItemOutcome.Repaired);

    public int FailedCount => Items.Count(item => item.Outcome == OperationItemOutcome.Failed);

    public int SkippedCount => Items.Count(item => item.Outcome == OperationItemOutcome.Skipped);
}

public sealed record UndoOutcome(
    string? BatchId,
    bool Applied,
    IReadOnlyList<RepairItemResult> Items,
    IReadOnlyList<UndoConflict> Conflicts,
    string Message);
