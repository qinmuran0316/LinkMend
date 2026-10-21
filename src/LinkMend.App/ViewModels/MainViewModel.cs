using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using LinkMend.App.Services;
using LinkMend.Core.Backup;
using LinkMend.Core.Models;
using LinkMend.Core.Relocation;
using LinkMend.Core.Repair;
using LinkMend.Core.Scan;
using LinkMend.Windows.Threading;

namespace LinkMend.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string FilterAll = "全部";
    private const string FilterProblems = "需要关注";
    private const string FilterActionable = "可处理";
    private const string FilterHealthy = "正常";

    private readonly ShortcutScanner _scanner;
    private readonly RelocationService _relocationService;
    private readonly RepairPlanBuilder _planBuilder;
    private readonly RepairService _repairService;
    private readonly UndoService _undoService;
    private readonly BackupRepository _backups;
    private readonly AppSettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly List<string> _droppedShortcuts = new();

    private CancellationTokenSource? _scanCts;
    private bool _isBusy;
    private ShortcutRow? _selectedRow;
    private HistoryRow? _selectedHistory;
    private string _selectedFilter = FilterAll;
    private string _statusText = "就绪。选择扫描范围后开始检查。";
    private string _progressText = "";
    private double _progressValue;
    private double _progressMaximum = 1;
    private bool _isProgressIndeterminate = true;
    private int _scannedCount;
    private int _problemCount;
    private int _healthyCount;
    private int _repairableCount;
    private int _checkedCount;

    public MainViewModel(
        ShortcutScanner scanner,
        RelocationService relocationService,
        RepairPlanBuilder planBuilder,
        RepairService repairService,
        UndoService undoService,
        BackupRepository backups,
        AppSettingsStore settingsStore)
    {
        _scanner = scanner;
        _relocationService = relocationService;
        _planBuilder = planBuilder;
        _repairService = repairService;
        _undoService = undoService;
        _backups = backups;
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();

        CustomFolders = new ObservableCollection<string>(_settings.CustomFolders);
        BackUpRoot = backups.RootDirectory;
        BackUpHistoryRoot = backups.HistoryDirectory;
        SettingsFilePath = settingsStore.SettingsFilePath;
        VersionText = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0"}";
    }

    public ObservableCollection<ShortcutRow> AllRows { get; } = new();

    public ObservableCollection<ShortcutRow> Rows { get; } = new();

    public ObservableCollection<string> CustomFolders { get; }

    public ObservableCollection<HistoryRow> HistoryRows { get; } = new();

    public IReadOnlyList<string> FilterOptions { get; } = new[]
    {
        FilterAll,
        FilterProblems,
        FilterActionable,
        FilterHealthy
    };

    public RelocationService RelocationService => _relocationService;

    public IReadOnlyList<string> SearchRoots => _settings.SearchRoots;

    public string BackUpRoot { get; }

    public string BackUpHistoryRoot { get; }

    public string SettingsFilePath { get; }

    public string VersionText { get; }

    public bool ScanDesktop
    {
        get => _settings.ScanDesktop;
        set
        {
            if (_settings.ScanDesktop == value)
            {
                return;
            }

            _settings.ScanDesktop = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool ScanStartMenu
    {
        get => _settings.ScanStartMenu;
        set
        {
            if (_settings.ScanStartMenu == value)
            {
                return;
            }

            _settings.ScanStartMenu = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public double ProgressMaximum
    {
        get => _progressMaximum;
        private set => SetProperty(ref _progressMaximum, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanInteract));
                OnPropertyChanged(nameof(CanRepairSelected));
            }
        }
    }

    public bool CanInteract => !IsBusy;

    public int ScannedCount
    {
        get => _scannedCount;
        private set => SetProperty(ref _scannedCount, value);
    }

    public int ProblemCount
    {
        get => _problemCount;
        private set => SetProperty(ref _problemCount, value);
    }

    public int HealthyCount
    {
        get => _healthyCount;
        private set => SetProperty(ref _healthyCount, value);
    }

    public int RepairableCount
    {
        get => _repairableCount;
        private set => SetProperty(ref _repairableCount, value);
    }

    public int CheckedCount
    {
        get => _checkedCount;
        private set
        {
            if (SetProperty(ref _checkedCount, value))
            {
                OnPropertyChanged(nameof(CanRepairSelected));
            }
        }
    }

    public bool CanRepairSelected => CheckedCount > 0 && !IsBusy;

    public bool HasRows => AllRows.Count > 0;

    public bool HasHistory => HistoryRows.Count > 0;

    public bool HasDroppedFiles => _droppedShortcuts.Count > 0;

    public string DroppedSummary => _droppedShortcuts.Count == 0
        ? ""
        : $"已额外加入 {_droppedShortcuts.Count} 个快捷方式";

    public ShortcutRow? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    public HistoryRow? SelectedHistory
    {
        get => _selectedHistory;
        set => SetProperty(ref _selectedHistory, value);
    }

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public async Task InitializeAsync()
    {
        await RefreshHistoryAsync();
    }

    public async Task<string?> ScanAsync()
    {
        if (IsBusy)
        {
            return "当前正在执行任务，请稍候。";
        }

        var roots = ScanScopeBuilder.Build(ScanDesktop, ScanStartMenu, CustomFolders);
        if (roots.Count == 0 && _droppedShortcuts.Count == 0)
        {
            StatusText = "请至少选择桌面、开始菜单，或添加一个扫描文件夹。";
            return "没有可扫描的范围。请勾选桌面或开始菜单，或添加文件夹。";
        }

        SaveSettings();
        ClearResults();
        StatusText = "正在检查快捷方式…";
        ProgressText = "正在收集快捷方式…";
        ProgressValue = 0;
        ProgressMaximum = 1;
        IsProgressIndeterminate = true;
        IsBusy = true;

        var cts = new CancellationTokenSource();
        _scanCts = cts;
        var progress = new Progress<ScanProgress>(UpdateProgress);

        try
        {
            var request = new ScanRequest(roots, _droppedShortcuts.ToArray());
            var result = await StaExecutor.RunAsync(
                () => _scanner.Scan(request, progress, cts.Token),
                cts.Token);

            ApplyScanResult(result);
            StatusText = BuildScanStatus(result);
            return null;
        }
        catch (OperationCanceledException)
        {
            StatusText = "检查已取消。";
            return null;
        }
        catch (Exception ex)
        {
            StatusText = $"检查失败：{ex.Message}";
            AppLogger.Error(ex, "扫描快捷方式");
            return ex.Message;
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
            if (_scanCts == cts)
            {
                _scanCts = null;
            }

            cts.Dispose();
        }
    }

    public void CancelScan()
    {
        if (!IsBusy || _scanCts is null)
        {
            return;
        }

        StatusText = "正在取消…";
        _scanCts.Cancel();
    }

    public bool AddCustomFolder(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            StatusText = "文件夹路径无效。";
            return false;
        }

        if (!Directory.Exists(fullPath))
        {
            StatusText = "所选文件夹不存在。";
            return false;
        }

        if (CustomFolders.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            StatusText = "这个文件夹已经加入扫描范围。";
            return false;
        }

        CustomFolders.Add(fullPath);
        _settings.CustomFolders = CustomFolders.ToList();
        SaveSettings();
        StatusText = $"已添加扫描文件夹：{fullPath}";
        return true;
    }

    public void RemoveCustomFolder(string path)
    {
        var existing = CustomFolders.FirstOrDefault(
            folder => string.Equals(folder, path, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        CustomFolders.Remove(existing);
        _settings.CustomFolders = CustomFolders.ToList();
        SaveSettings();
        StatusText = $"已移除扫描文件夹：{existing}";
    }

    public bool AddDroppedShortcut(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            StatusText = "拖入的文件不是有效的 .lnk 快捷方式。";
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        if (_droppedShortcuts.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            StatusText = "这个快捷方式已经在待扫描列表中。";
            return false;
        }

        _droppedShortcuts.Add(fullPath);
        OnDroppedChanged();
        StatusText = $"已加入快捷方式：{fullPath}";
        return true;
    }

    public void ClearDroppedShortcuts()
    {
        _droppedShortcuts.Clear();
        OnDroppedChanged();
        StatusText = "已清除额外加入的快捷方式。";
    }

    public void SetAllRepairableChecked(bool isChecked)
    {
        foreach (var row in AllRows.Where(row => row.IsSelectable))
        {
            row.IsChecked = isChecked;
        }
    }

    public IReadOnlyList<RepairPlan> BuildRepairPlans(
        IEnumerable<ShortcutRow> rows,
        out IReadOnlyList<ShortcutRow> missingCandidates)
    {
        var selected = rows.Distinct().ToArray();
        missingCandidates = selected
            .Where(row => row.NeedsRelocation && row.SelectedCandidate is null)
            .ToArray();

        var plans = new List<RepairPlan>();
        foreach (var row in selected)
        {
            if (missingCandidates.Contains(row))
            {
                continue;
            }

            var plan = _planBuilder.Build(row.Item, row.SelectedCandidate);
            if (plan is not null && plan.HasChanges)
            {
                plans.Add(plan);
            }
        }

        return plans;
    }

    public void ApplyCandidate(ShortcutRow row, ResolutionCandidate candidate, IEnumerable<string> additionalRoots)
    {
        row.SetCandidate(candidate);
        row.IsChecked = true;
        MergeSearchRoots(additionalRoots);
        StatusText = $"已为「{row.DisplayName}」选择新位置。勾选并预览后即可修复。";
    }

    public async Task<RepairOutcome> RepairAsync(IReadOnlyList<RepairPlan> plans)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("当前正在执行任务，请稍候。");
        }

        if (plans.Count == 0)
        {
            throw new InvalidOperationException("没有可应用的修改。");
        }

        IsBusy = true;
        StatusText = "正在备份并应用修复…";
        try
        {
            var outcome = await StaExecutor.RunAsync(() => _repairService.Apply(plans));
            StatusText = outcome.RepairedCount > 0
                ? $"修复完成：成功 {outcome.RepairedCount} 项，失败 {outcome.FailedCount} 项，跳过 {outcome.SkippedCount} 项。"
                : $"没有修改成功：失败 {outcome.FailedCount} 项，跳过 {outcome.SkippedCount} 项。";
            await RefreshHistoryAsync();
            return outcome;
        }
        catch (Exception ex)
        {
            StatusText = $"修复失败：{ex.Message}";
            AppLogger.Error(ex, "应用修复");
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshHistoryAsync()
    {
        try
        {
            var allBatches = await Task.Run(_backups.LoadAll);
            var undoneIds = new HashSet<string>(
                allBatches
                    .Where(batch => batch.Operation == BackupOperations.Undo
                                    && !string.IsNullOrWhiteSpace(batch.UndoesBatchId)
                                    && batch.Items.Any(item => item.Status == BackupStatuses.Restored))
                    .Select(batch => batch.UndoesBatchId!),
                StringComparer.Ordinal);

            var selectedId = SelectedHistory?.Batch.Id;
            HistoryRows.Clear();
            foreach (var batch in allBatches)
            {
                HistoryRows.Add(new HistoryRow(batch, undoneIds.Contains(batch.Id)));
            }

            SelectedHistory = selectedId is null
                ? HistoryRows.FirstOrDefault()
                : HistoryRows.FirstOrDefault(row => row.Batch.Id == selectedId) ?? HistoryRows.FirstOrDefault();
            OnPropertyChanged(nameof(HasHistory));
        }
        catch (Exception ex)
        {
            StatusText = $"无法读取修复历史：{ex.Message}";
            AppLogger.Error(ex, "读取修复历史");
        }
    }

    public async Task<UndoPreview> PreviewUndoAsync(HistoryRow row)
    {
        var allBatches = await Task.Run(_backups.LoadAll);
        var batch = allBatches.FirstOrDefault(item => item.Id == row.Batch.Id) ?? row.Batch;
        return await Task.Run(() => _undoService.Preview(batch, allBatches));
    }

    public async Task<UndoOutcome> UndoAsync(HistoryRow row)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("当前正在执行任务，请稍候。");
        }

        IsBusy = true;
        StatusText = "正在从备份恢复…";
        try
        {
            var allBatches = await Task.Run(_backups.LoadAll);
            var batch = allBatches.FirstOrDefault(item => item.Id == row.Batch.Id) ?? row.Batch;
            var outcome = await Task.Run(() => _undoService.Undo(batch, allBatches));
            StatusText = outcome.Message;
            await RefreshHistoryAsync();
            return outcome;
        }
        catch (Exception ex)
        {
            StatusText = $"撤销失败：{ex.Message}";
            AppLogger.Error(ex, "撤销修复");
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void MergeSearchRoots(IEnumerable<string> roots)
    {
        var changed = false;
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(root);
            if (_settings.SearchRoots.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            _settings.SearchRoots.Add(fullPath);
            changed = true;
        }

        if (changed)
        {
            SaveSettings();
        }
    }

    private void ApplyScanResult(ScanResult result)
    {
        foreach (var scanned in result.Shortcuts)
        {
            var row = new ShortcutRow(scanned);
            row.PropertyChanged += Row_PropertyChanged;
            AllRows.Add(row);
        }

        ScannedCount = AllRows.Count;
        ProblemCount = AllRows.Count(row => row.Health != ShortcutHealth.Healthy);
        HealthyCount = AllRows.Count(row => row.Health == ShortcutHealth.Healthy);
        RepairableCount = AllRows.Count(row => row.CanAct);
        CheckedCount = 0;
        OnPropertyChanged(nameof(HasRows));

        ApplyFilter();
        SelectedRow = Rows.FirstOrDefault(row => !row.IsHealthy) ?? Rows.FirstOrDefault();
    }

    private string BuildScanStatus(ScanResult result)
    {
        var text = $"检查完成：共 {result.Total} 个快捷方式";
        if (result.FailedReads > 0)
        {
            text += $"，{result.FailedReads} 个无法读取";
        }

        text += ProblemCount > 0
            ? $"，{ProblemCount} 个需要关注。"
            : "，未发现明显问题。";

        if (result.SkippedItems > 0)
        {
            text += $" 另有 {result.SkippedItems} 个路径被跳过。";
        }

        return text;
    }

    private void UpdateProgress(ScanProgress progress)
    {
        ProgressValue = progress.Processed;
        ProgressMaximum = Math.Max(progress.Total, 1);
        IsProgressIndeterminate = progress.Total <= 0;
        ProgressText = progress.Total > 0
            ? $"{progress.Processed} / {progress.Total}"
            : "准备中…";

        StatusText = progress.Total > 0
            ? $"正在读取快捷方式 {progress.Processed} / {progress.Total}"
            : "正在收集快捷方式…";
    }

    private void ClearResults()
    {
        foreach (var row in AllRows)
        {
            row.PropertyChanged -= Row_PropertyChanged;
        }

        AllRows.Clear();
        Rows.Clear();
        SelectedRow = null;
        ScannedCount = 0;
        ProblemCount = 0;
        HealthyCount = 0;
        RepairableCount = 0;
        CheckedCount = 0;
        OnPropertyChanged(nameof(HasRows));
    }

    private void ApplyFilter()
    {
        var previousSelection = SelectedRow;
        Rows.Clear();

        IEnumerable<ShortcutRow> filtered = SelectedFilter switch
        {
            FilterProblems => AllRows.Where(row => row.Health != ShortcutHealth.Healthy),
            FilterActionable => AllRows.Where(row => row.CanAct),
            FilterHealthy => AllRows.Where(row => row.Health == ShortcutHealth.Healthy),
            _ => AllRows
        };

        foreach (var row in filtered)
        {
            Rows.Add(row);
        }

        if (previousSelection is not null && Rows.Contains(previousSelection))
        {
            SelectedRow = previousSelection;
        }
        else if (Rows.Count > 0)
        {
            SelectedRow = Rows[0];
        }
        else
        {
            SelectedRow = null;
        }
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShortcutRow.IsChecked))
        {
            CheckedCount = AllRows.Count(row => row.IsChecked);
        }
    }

    private void OnDroppedChanged()
    {
        OnPropertyChanged(nameof(HasDroppedFiles));
        OnPropertyChanged(nameof(DroppedSummary));
    }

    private void SaveSettings()
    {
        try
        {
            _settings.CustomFolders = CustomFolders.ToList();
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            StatusText = $"设置无法保存：{ex.Message}";
            AppLogger.Error(ex, "保存设置");
        }
    }
}
