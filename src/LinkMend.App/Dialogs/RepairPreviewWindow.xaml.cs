using System.Windows;
using LinkMend.Core.Repair;

namespace LinkMend.App.Dialogs;

public partial class RepairPreviewWindow : Window
{
    public RepairPreviewWindow(IReadOnlyList<RepairPlan> plans)
    {
        InitializeComponent();
        var items = plans.Select(plan => new PreviewItem(plan)).ToArray();
        PlansList.ItemsSource = items;
        SummaryText.Text = $"将修改 {items.Length} 个快捷方式，共 {items.Sum(item => item.Changes.Count)} 处字段。请确认修改前后的值。";
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

public sealed class PreviewItem
{
    public PreviewItem(RepairPlan plan)
    {
        DisplayName = plan.Item.Link.DisplayName;
        FilePath = plan.Item.Link.FilePath;
        var changes = new List<PreviewChange>();

        if (plan.TargetPath is not null)
        {
            changes.Add(new PreviewChange("目标路径", Display(plan.Item.Link.TargetPath), plan.TargetPath));
        }

        if (plan.WorkingDirectory is not null)
        {
            changes.Add(new PreviewChange(
                "起始位置",
                Display(plan.Item.Link.WorkingDirectory),
                plan.WorkingDirectory));
        }

        if (plan.Icon is { } icon)
        {
            var before = string.IsNullOrWhiteSpace(plan.Item.Link.IconPath)
                ? "系统默认图标"
                : $"{plan.Item.Link.IconPath}, {plan.Item.Link.IconIndex}";
            var after = icon.Clear
                ? "系统默认图标"
                : $"{icon.Path}, {icon.Index}";
            changes.Add(new PreviewChange("图标", before, after));
        }

        Changes = changes;
    }

    public string DisplayName { get; }

    public string FilePath { get; }

    public IReadOnlyList<PreviewChange> Changes { get; }

    private static string Display(string value) =>
        string.IsNullOrWhiteSpace(value) ? "（未设置）" : value;
}

public sealed record PreviewChange(string Label, string Before, string After);
