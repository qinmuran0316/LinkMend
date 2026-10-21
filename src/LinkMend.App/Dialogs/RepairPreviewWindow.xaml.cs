using System.Windows;
using LinkMend.App.Services;
using LinkMend.Core.Repair;

namespace LinkMend.App.Dialogs;

public partial class RepairPreviewWindow : Window
{
    private readonly IReadOnlyList<RepairPlan> _plans;

    public RepairPreviewWindow(IReadOnlyList<RepairPlan> plans)
    {
        InitializeComponent();
        _plans = plans;
        LanguageManager.LanguageChanged += LanguageManager_LanguageChanged;
        Closed += (_, _) => LanguageManager.LanguageChanged -= LanguageManager_LanguageChanged;
        LanguageManager.Apply(this);
        RefreshLocalizedContent();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void LanguageManager_LanguageChanged(object? sender, EventArgs e)
    {
        LanguageManager.Apply(this);
        RefreshLocalizedContent();
    }

    private void RefreshLocalizedContent()
    {
        var items = _plans.Select(plan => new PreviewItem(plan)).ToArray();
        PlansList.ItemsSource = items;
        SummaryText.Text = LanguageManager.T($"将修改 {items.Length} 个快捷方式，共 {items.Sum(item => item.Changes.Count)} 处字段。请确认修改前后的值。");
    }
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
            changes.Add(new PreviewChange(LanguageManager.T("目标路径"), Display(plan.Item.Link.TargetPath), plan.TargetPath));
        }

        if (plan.WorkingDirectory is not null)
        {
            changes.Add(new PreviewChange(
                LanguageManager.T("起始位置"),
                Display(plan.Item.Link.WorkingDirectory),
                plan.WorkingDirectory));
        }

        if (plan.Icon is { } icon)
        {
            var before = string.IsNullOrWhiteSpace(plan.Item.Link.IconPath)
                ? LanguageManager.T("系统默认图标")
                : $"{plan.Item.Link.IconPath}, {plan.Item.Link.IconIndex}";
            var after = icon.Clear
                ? LanguageManager.T("系统默认图标")
                : $"{icon.Path}, {icon.Index}";
            changes.Add(new PreviewChange(LanguageManager.T("图标"), before, after));
        }

        Changes = changes;
    }

    public string DisplayName { get; }

    public string FilePath { get; }

    public IReadOnlyList<PreviewChange> Changes { get; }

    private static string Display(string value) =>
        string.IsNullOrWhiteSpace(value) ? LanguageManager.T("（未设置）") : value;
}

public sealed record PreviewChange(string Label, string Before, string After);
