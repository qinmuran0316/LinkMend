using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LinkMend.App.Dialogs;
using LinkMend.App.Services;
using LinkMend.App.ViewModels;
using LinkMend.Core.Repair;
using Microsoft.Win32;

namespace LinkMend.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _uiReady;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        LanguageManager.LanguageChanged += LanguageManager_LanguageChanged;
        Closed += (_, _) => LanguageManager.LanguageChanged -= LanguageManager_LanguageChanged;
        Loaded += OnLoaded;
        Closing += (_, args) =>
        {
            args.Cancel = !_viewModel.TryPrepareToClose();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        FitToMonitor();
    }

    private void FitToMonitor()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return;
            }

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return;
            }

            var nativeDpi = GetDpiForWindow(handle);
            var scale = nativeDpi > 0
                ? nativeDpi / 96d
                : VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (scale <= 0)
            {
                scale = 1;
            }

            var workWidth = (info.Work.Right - info.Work.Left) / scale;
            var workHeight = (info.Work.Bottom - info.Work.Top) / scale;

            Width = Math.Min(Width, Math.Max(MinWidth, workWidth - 32));
            Height = Math.Min(Height, Math.Max(MinHeight, workHeight - 32));
            Left = info.Work.Left / scale + (workWidth - Width) / 2;
            Top = info.Work.Top / scale + (workHeight - Height) / 2;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            AppLogger.Error(ex, "调整主窗口尺寸");
        }
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            FitToMonitor();
            LanguageManager.Apply(this);
            _uiReady = true;
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex, "初始化界面");
        }
    }

    private void LanguageManager_LanguageChanged(object? sender, EventArgs e)
    {
        LanguageManager.Apply(this);
        _viewModel.RefreshLanguage();
    }

    private void NavDiagnosis_Checked(object sender, RoutedEventArgs e)
    {
        if (_uiReady)
        {
            ShowPage(DiagnosisPage);
        }
    }

    private async void NavHistory_Checked(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
        {
            return;
        }

        ShowPage(HistoryPage);
        try
        {
            await _viewModel.RefreshHistoryAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex, "加载修复历史");
        }
    }

    private void NavAbout_Checked(object sender, RoutedEventArgs e)
    {
        if (_uiReady)
        {
            ShowPage(AboutPage);
        }
    }

    private void ShowPage(UIElement page)
    {
        DiagnosisPage.Visibility = page == DiagnosisPage ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = page == HistoryPage ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == AboutPage ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var error = await _viewModel.ScanAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                ShowWarning(LanguageManager.T(error), LanguageManager.T("检查未完成"));
            }
        }
        catch (Exception ex)
        {
            HandleException(ex, "扫描快捷方式");
        }
    }

    private async void AutoRepair_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        try
        {
            var result = await _viewModel.AutoLocateAsync();
            if (result.ReadyRows.Count == 0)
            {
                ShowWarning(
                    LanguageManager.T($"自动搜索没有找到可直接修复的项目。\n\n需要确认：{result.AmbiguousCount} 项\n未找到：{result.NotFoundCount} 项\n\n请选中目标丢失的项目，点击“查找新位置”确认候选。"),
                    LanguageManager.T("自动搜索未找到可修复项目"));
                return;
            }

            var summary = LanguageManager.T(
                $"已找到并准备修复：{result.ReadyRows.Count} 项\n" +
                $"自动定位程序：{result.LocatedCount} 项\n" +
                $"需要人工确认：{result.AmbiguousCount} 项\n" +
                $"未找到：{result.NotFoundCount} 项\n\n" +
                "高置信度结果会进入修复预览，其他项目不会被修改。");
            MessageBox.Show(this, summary, LanguageManager.T("自动搜索完成"), MessageBoxButton.OK, MessageBoxImage.Information);
            await RepairRowsAsync(result.ReadyRows);
        }
        catch (Exception ex)
        {
            HandleException(ex, "自动搜索并修复");
        }
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e) => _viewModel.CancelScan();

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择要扫描的文件夹",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.AddCustomFolder(dialog.FolderName);
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            _viewModel.RemoveCustomFolder(path);
        }
    }

    private void ClearDropped_Click(object sender, RoutedEventArgs e) =>
        _viewModel.ClearDroppedShortcuts();

    private void SelectAllRepairable_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetAllRepairableChecked(true);

    private void ClearSelection_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetAllRepairableChecked(false);

    private async void RepairSelected_Click(object sender, RoutedEventArgs e)
    {
        var rows = _viewModel.AllRows.Where(row => row.IsChecked).ToArray();
        await RepairRowsAsync(rows);
    }

    private async void FixSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRow is { } row)
        {
            await RepairRowsAsync(new[] { row });
        }
    }

    private async Task RepairRowsAsync(IReadOnlyList<ShortcutRow> rows)
    {
        if (_viewModel.IsBusy)
        {
            ShowWarning("当前正在执行任务，请稍候。", "请稍候");
            return;
        }

        if (rows.Count == 0)
        {
            ShowWarning("请先勾选要修复的快捷方式。", "没有选择项目");
            return;
        }

        var plans = _viewModel.BuildRepairPlans(rows, out var missingCandidates);
        if (missingCandidates.Count > 0)
        {
            var names = string.Join("、", missingCandidates.Select(row => $"「{row.DisplayName}」").Take(5));
            ShowWarning(
                $"{names} 的目标丢失，还没有选择新位置。\n\n请先点击“查找新位置”，确认候选程序后再修复。",
                "需要先定位程序");
            return;
        }

        if (plans.Count == 0)
        {
            ShowWarning("勾选的项目没有可自动应用的修改。请查看诊断依据后手动处理。", "没有可应用的修改");
            return;
        }

        var preview = new RepairPreviewWindow(plans)
        {
            Owner = this
        };

        if (preview.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var outcome = await _viewModel.RepairAsync(plans);
            ShowRepairOutcome(outcome);

            if (outcome.RepairedCount > 0)
            {
                var error = await _viewModel.ScanAsync();
                if (!string.IsNullOrWhiteSpace(error))
                {
                    ShowWarning($"修复已完成，但重新检查失败：{error}", "重新检查失败");
                }
            }
        }
        catch (Exception ex)
        {
            HandleException(ex, "应用修复");
        }
    }

    private async void FindRelocation_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRow is not { } row || !row.NeedsRelocation)
        {
            return;
        }

        try
        {
            var dialog = new RelocationWindow(
                _viewModel.RelocationService,
                row.Item,
                _viewModel.SearchRoots)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && dialog.SelectedCandidate is { } candidate)
            {
                _viewModel.ApplyCandidate(row, candidate, dialog.SearchRoots);
            }
        }
        catch (Exception ex)
        {
            HandleException(ex, "查找新位置");
        }
    }

    private void OpenShortcutFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRow is not { } row)
        {
            return;
        }

        try
        {
            ShellActions.OpenContainingFolder(row.FilePath);
        }
        catch (Exception ex)
        {
            HandleException(ex, "打开快捷方式所在文件夹");
        }
    }

    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellActions.OpenFolder(_viewModel.BackUpRoot);
        }
        catch (Exception ex)
        {
            HandleException(ex, "打开备份目录");
        }
    }

    private async void RefreshHistory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshHistoryAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex, "刷新修复历史");
        }
    }

    private async void UndoBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedHistory is not { CanUndo: true } row)
        {
            return;
        }

        try
        {
            var preview = await _viewModel.PreviewUndoAsync(row);
            if (!preview.CanUndo)
            {
                ShowUndoConflicts(preview);
                return;
            }

            var confirmation = MessageBox.Show(
                this,
                $"将撤销批次 {row.BatchId}。\n{preview.Summary}\n\n撤销前会再次校验当前文件；若发现修复后又被修改，将停止并保留新变化。",
                "确认撤销",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var outcome = await _viewModel.UndoAsync(row);
            if (!outcome.Applied)
            {
                if (outcome.Conflicts.Count > 0)
                {
                    ShowConflicts(outcome.Conflicts);
                }
                else
                {
                    ShowWarning(outcome.Message, "撤销未执行");
                }

                return;
            }

            var failures = outcome.Items
                .Where(item => item.Outcome == OperationItemOutcome.Failed)
                .ToArray();
            var message = outcome.Message;
            if (failures.Length > 0)
            {
                message += "\n\n失败项：\n" + string.Join(
                    "\n",
                    failures.Take(6).Select(item => $"• {item.FileName}：{item.Message}"));
            }

            MessageBox.Show(
                this,
                message,
                "撤销完成",
                MessageBoxButton.OK,
                failures.Length > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            if (_viewModel.HasRows)
            {
                await _viewModel.ScanAsync();
            }
        }
        catch (Exception ex)
        {
            HandleException(ex, "撤销修复");
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        var accepted = 0;
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                if (_viewModel.AddCustomFolder(path))
                {
                    accepted++;
                }
            }
            else if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                if (_viewModel.AddDroppedShortcut(path))
                {
                    accepted++;
                }
            }
        }

        if (accepted > 0)
        {
            ShowPage(DiagnosisPage);
            NavDiagnosis.IsChecked = true;
        }

        e.Handled = true;
    }

    private void ShowRepairOutcome(RepairOutcome outcome)
    {
        var text = $"成功修复：{outcome.RepairedCount} 项\n失败：{outcome.FailedCount} 项\n跳过：{outcome.SkippedCount} 项";
        if (!string.IsNullOrWhiteSpace(outcome.BatchId))
        {
            text += $"\n\n备份批次：{outcome.BatchId}\n可在“修复历史”中查看并撤销。";
        }

        var failed = outcome.Items
            .Where(item => item.Outcome is OperationItemOutcome.Failed or OperationItemOutcome.Skipped)
            .Take(6)
            .ToArray();

        if (failed.Length > 0)
        {
            text += "\n\n未完成项：\n" + string.Join(
                "\n",
                failed.Select(item => $"• {item.FileName}：{item.Message}"));
        }

        MessageBox.Show(
            this,
            text,
            outcome.RepairedCount > 0 ? "修复完成" : "修复未完成",
            MessageBoxButton.OK,
            outcome.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void ShowUndoConflicts(UndoPreview preview)
    {
        if (preview.Conflicts.Count == 0)
        {
            ShowWarning(preview.Summary, "无法撤销");
            return;
        }

        ShowConflicts(preview.Conflicts);
    }

    private void ShowConflicts(IEnumerable<LinkMend.Core.Backup.UndoConflict> conflicts)
    {
        var text = "检测到文件变化，未撤销任何内容：\n\n" + string.Join(
            "\n",
            conflicts.Take(8).Select(conflict =>
                $"• {(string.IsNullOrWhiteSpace(conflict.FileName) ? "批次" : conflict.FileName)}：{conflict.Reason}"));

        text += "\n\n如果这些变化是你刚刚做的，保留它们更安全；需要恢复时请先核对备份。";
        ShowWarning(text, "撤销已停止");
    }

    private void ShowWarning(string message, string title) =>
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    private void HandleException(Exception exception, string context)
    {
        AppLogger.Error(exception, context);
        MessageBox.Show(
            this,
            $"{context}时发生错误：{exception.Message}\n\n详细信息已写入本地日志。",
            "LinkMend",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
