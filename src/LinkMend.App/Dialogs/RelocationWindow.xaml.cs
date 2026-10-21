using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using LinkMend.App.Services;
using LinkMend.App.ViewModels;
using LinkMend.Core.Abstractions;
using LinkMend.Core.Models;
using LinkMend.Core.Relocation;
using Microsoft.Win32;

namespace LinkMend.App.Dialogs;

public partial class RelocationWindow : Window
{
    private readonly RelocationService _service;
    private readonly ScannedShortcut _shortcut;
    private readonly ObservableCollection<string> _roots = new();
    private readonly ObservableCollection<CandidateDisplay> _candidates = new();
    private CancellationTokenSource? _searchCts;
    private bool _isSearching;

    public RelocationWindow(
        RelocationService service,
        ScannedShortcut shortcut,
        IEnumerable<string> initialRoots)
    {
        InitializeComponent();
        LanguageManager.LanguageChanged += LanguageManager_LanguageChanged;
        Closed += (_, _) => LanguageManager.LanguageChanged -= LanguageManager_LanguageChanged;
        _service = service;
        _shortcut = shortcut;

        foreach (var root in initialRoots
                     .Where(Directory.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _roots.Add(root);
        }

        RootsList.ItemsSource = _roots;
        CandidatesList.ItemsSource = _candidates;
        HeaderTitle.Text = LanguageManager.T($"为「{shortcut.Link.DisplayName}」查找新位置");
        OriginalTargetText.Text = LanguageManager.T($"原目标：{shortcut.Paths.TargetPath}");
        SearchProgress.Visibility = Visibility.Collapsed;
        LanguageManager.Apply(this);
        Loaded += async (_, _) => await SearchAsync();
        Closing += (_, _) => _searchCts?.Cancel();
    }

    public ResolutionCandidate? SelectedCandidate { get; private set; }

    public IReadOnlyList<string> SearchRoots => _roots.ToArray();

    private async void SearchAgain_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private void LanguageManager_LanguageChanged(object? sender, EventArgs e)
    {
        LanguageManager.Apply(this);
        foreach (var candidate in _candidates)
        {
            candidate.RefreshLanguage();
        }

        HeaderTitle.Text = LanguageManager.T($"为「{_shortcut.Link.DisplayName}」查找新位置");
        OriginalTargetText.Text = LanguageManager.T($"原目标：{_shortcut.Paths.TargetPath}");
    }

    private async void AddRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LanguageManager.T("添加要搜索的文件夹"),
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string path;
        try
        {
            path = Path.GetFullPath(dialog.FolderName);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            SearchStatus.Text = LanguageManager.T("所选搜索目录无效。");
            return;
        }

        if (_roots.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            SearchStatus.Text = LanguageManager.T("这个搜索目录已经在列表中。");
            return;
        }

        _roots.Add(path);
        await SearchAsync();
    }

    private void CandidatesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UseCandidateButton.IsEnabled = CandidatesList.SelectedItem is CandidateDisplay;

    private void CandidatesList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CandidatesList.SelectedItem is CandidateDisplay)
        {
            UseCandidate_Click(sender, e);
        }
    }

    private void UseCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (CandidatesList.SelectedItem is not CandidateDisplay display)
        {
            return;
        }

        SelectedCandidate = display.Candidate;
        DialogResult = true;
    }

    private async Task SearchAsync()
    {
        if (_isSearching)
        {
            return;
        }

        _isSearching = true;
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        SearchButton.IsEnabled = false;
        AddRootButton.IsEnabled = false;
        UseCandidateButton.IsEnabled = false;
        SearchProgress.Visibility = Visibility.Visible;
        SearchStatus.Text = LanguageManager.T("正在搜索安装记录和常见安装目录…");
        CandidateCountText.Text = LanguageManager.T("搜索中");
        EmptyPanel.Visibility = Visibility.Collapsed;
        CandidatesList.Visibility = Visibility.Collapsed;
        _candidates.Clear();
        CandidatesList.SelectedItem = null;

        try
        {
            var roots = _roots.ToArray();
            var result = await Task.Run(
                () => _service.FindCandidates(_shortcut, roots, _searchCts.Token),
                _searchCts.Token);

            foreach (var candidate in result.Candidates)
            {
                _candidates.Add(new CandidateDisplay(candidate));
            }

            CandidateCountText.Text = LanguageManager.T($"{_candidates.Count} 个候选");
            SearchStatus.Text = LanguageManager.T(result.SearchedRoots.Count > 0
                ? $"已搜索 {result.SearchedRoots.Count} 个目录，并按证据排序。"
                : "安装记录中没有匹配项。");

            if (_candidates.Count == 0)
            {
                EmptyPanel.Visibility = Visibility.Visible;
                CandidatesList.Visibility = Visibility.Collapsed;
                EmptyTitle.Text = LanguageManager.T("没有找到同名程序");
                EmptyMessage.Text = LanguageManager.T(result.Notice
                                    ?? "可以添加软件所在目录后重新搜索。");
            }
            else
            {
                EmptyPanel.Visibility = Visibility.Collapsed;
                CandidatesList.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
            SearchStatus.Text = LanguageManager.T("搜索已取消。");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "搜索软件新位置");
            SearchStatus.Text = LanguageManager.T($"搜索失败：{ex.Message}");
            EmptyPanel.Visibility = Visibility.Visible;
            CandidatesList.Visibility = Visibility.Collapsed;
            EmptyTitle.Text = LanguageManager.T("搜索失败");
            EmptyMessage.Text = ex.Message;
        }
        finally
        {
            SearchProgress.Visibility = Visibility.Collapsed;
            SearchButton.IsEnabled = true;
            AddRootButton.IsEnabled = true;
            UseCandidateButton.IsEnabled = CandidatesList.SelectedItem is CandidateDisplay;
            _isSearching = false;
        }
    }
}

public sealed class CandidateDisplay : ObservableObject
{
    public CandidateDisplay(ResolutionCandidate candidate)
    {
        Candidate = candidate;
    }

    public ResolutionCandidate Candidate { get; }

    public string Path => Candidate.ExecutablePath;

    public string ConfidenceLabel => LanguageManager.T(Candidate.Confidence switch
    {
        CandidateConfidence.High => "高可信度",
        CandidateConfidence.Medium => "中可信度",
        _ => "低可信度，请核对"
    });

    public string MetadataLine
    {
        get
        {
            var metadata = Candidate.Metadata;
            if (metadata is null)
            {
                return LanguageManager.T("没有可用的版本信息");
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(metadata.ProductName))
            {
                parts.Add(metadata.ProductName);
            }

            if (!string.IsNullOrWhiteSpace(metadata.CompanyName))
            {
                parts.Add(metadata.CompanyName);
            }

            if (!string.IsNullOrWhiteSpace(metadata.FileDescription))
            {
                parts.Add(metadata.FileDescription);
            }

            if (metadata.Length > 0)
            {
                parts.Add(FormatSize(metadata.Length));
            }

            return parts.Count == 0 ? LanguageManager.T("没有可用的版本信息") : string.Join(" · ", parts);
        }
    }

    public IReadOnlyList<string> Reasons => Candidate.Reasons.Select(LanguageManager.T).ToArray();

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(ConfidenceLabel));
        OnPropertyChanged(nameof(MetadataLine));
        OnPropertyChanged(nameof(Reasons));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d:0.0} MB";
        }

        return $"{bytes / 1024d:0} KB";
    }
}
