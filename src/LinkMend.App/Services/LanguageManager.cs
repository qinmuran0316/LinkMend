using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Text.RegularExpressions;

namespace LinkMend.App.Services;

/// <summary>
/// Lightweight runtime localization for the desktop UI. The original XAML remains
/// the source of truth in Chinese; static controls keep that source text and are
/// translated when the user changes the language. View-model messages call T().
/// </summary>
public static class LanguageManager
{
    public const string Chinese = "zh-CN";
    public const string English = "en-US";

    private static readonly Dictionary<string, string> EnglishTranslations =
        new(StringComparer.Ordinal)
        {
            ["LinkMend · 快捷方式诊断与修复"] = "LinkMend · Shortcut Diagnostics & Repair",
            ["快捷方式诊断与修复"] = "Shortcut Diagnostics & Repair",
            ["工作台"] = "Workspace",
            ["诊断与修复"] = "Diagnostics & Repair",
            ["修复历史"] = "Repair History",
            ["关于与安全"] = "About & Safety",
            ["安全优先"] = "Safety first",
            ["扫描不启动程序。修改前备份，写入后重新校验。"] = "Scanning never starts programs. Files are backed up before changes and verified afterwards.",
            ["快捷方式健康检查"] = "Shortcut health check",
            ["找出白图标、失效目标和数据目录冲突；修复前先备份，可随时撤销。"] = "Find blank icons, missing targets, and data-folder conflicts. Back up before repairing and undo later.",
            ["自动搜索并修复"] = "Auto search and repair",
            ["开始检查"] = "Start check",
            ["取消检查"] = "Cancel check",
            ["扫描范围"] = "Scan scope",
            ["只读取快捷方式，不会启动目标程序"] = "Reads shortcuts only; target programs are never started",
            ["桌面"] = "Desktop",
            ["开始菜单"] = "Start Menu",
            ["+ 添加文件夹"] = "+ Add folder",
            ["清除"] = "Clear",
            ["也可以把 .lnk 文件或文件夹拖入窗口。"] = "You can also drop .lnk files or folders onto the window.",
            ["从扫描范围移除"] = "Remove from scan scope",
            ["扫描发现问题时"] = "When a problem is found",
            ["先看左侧判断依据，再定位新路径或预览修复。"] = "Review the evidence on the left, then locate the new path or preview a repair.",
            ["已扫描"] = "Scanned",
            ["需要关注"] = "Needs attention",
            ["可修复或定位"] = "Repairable or locatable",
            ["正常"] = "Healthy",
            ["检查结果"] = "Check results",
            ["按严重程度排序"] = "Sorted by severity",
            ["全部"] = "All",
            ["可处理"] = "Actionable",
            ["全选可处理"] = "Select all actionable",
            ["清空"] = "Clear",
            ["还没有检查结果"] = "No check results yet",
            ["选择扫描范围，点击右上角“开始检查”。"] = "Select a scan scope and click “Start check” above.",
            ["选择一个快捷方式"] = "Select a shortcut",
            ["这里会显示诊断依据、路径对照和建议操作。"] = "Evidence, path comparisons, and suggested actions will appear here.",
            ["问题与判断依据"] = "Problems and evidence",
            ["建议"] = "Suggestion",
            ["目标"] = "Target",
            ["起始位置"] = "Working directory",
            ["图标"] = "Icon",
            ["已选择的新位置"] = "Selected new location",
            ["查找新位置"] = "Find new location",
            ["修复此项"] = "Repair this item",
            ["打开所在文件夹"] = "Open containing folder",
            ["预览并修复已选"] = "Preview and repair selected",
            ["清空选择"] = "Clear selection",
            ["打开备份目录"] = "Open backup folder",
            ["刷新"] = "Refresh",
            ["还没有修复记录"] = "No repair history",
            ["完成一次修复后，备份与撤销记录会出现在这里。"] = "Backups and undo records appear here after a repair.",
            ["每次修改都有本地备份。撤销前会检查文件是否在修复后被改动。"] = "Every change has a local backup. Files are checked for later changes before undo.",
            ["备份目录中的快照是原始 .lnk 文件；撤销前 LinkMend 会再次校验当前内容。"] = "Snapshots in the backup folder are the original .lnk files; LinkMend verifies current content again before undo.",
            ["关于 LinkMend"] = "About LinkMend",
            ["一个中文优先、只做可解释修复的 Windows 快捷方式诊断工具。"] = "A Windows shortcut diagnostic tool with explainable repairs and Chinese and English UI.",
            ["设计原则"] = "Design principles",
            ["1. 先判断原因，再提出修改：区分目标丢失、磁盘离线、权限不足、图标索引失效和特殊快捷方式。"] = "1. Diagnose the cause before suggesting a change: distinguish missing targets, offline disks, permissions, icon-index failures, and special shortcuts.",
            ["2. 不猜测：同名程序、多个数据目录和不确定的图标缓存问题会要求你确认，不自动选择或覆盖。"] = "2. Do not guess: similarly named programs, multiple data folders, and uncertain icon-cache issues require confirmation.",
            ["3. 写入前备份、写入后校验：原始 .lnk 快照保存在本地；撤销前会检查文件是否又被其他程序修改。"] = "3. Back up before writing and verify afterwards: the original .lnk snapshot stays local; undo checks for changes by other programs.",
            ["4. 最小权限：以普通用户身份运行，不主动提权；扫描不会启动目标程序。"] = "4. Least privilege: run as a standard user without elevation; scanning never starts target programs.",
            ["支持与限制"] = "Support and limits",
            ["支持：目标/起始位置/图标路径与索引诊断；同名程序搜索（安装记录 + 有限目录扫描）；显式 --user-data-dir/--data-dir 等参数冲突提示；备份、撤销和冲突保护。"] = "Supports target, working-directory, and icon path/index diagnostics; same-name program search using installation records and bounded directory scans; data-folder conflict hints; backups, undo, and conflict protection.",
            ["限制：不清理 Windows 图标缓存；不解析特殊 Shell 命名空间快捷方式；不会自动删除启动参数；目录搜索设有上限，便携软件可手动添加搜索目录。"] = "Limits: Windows icon cache is not cleared; special Shell namespace shortcuts are not parsed; launch arguments are never deleted; directory searches are bounded.",
            ["数据目录检测仅分析明确的启动参数，不代表应用的完整配置来源；多个数据目录可能是有意隔离。"] = "Data-folder checks analyze explicit launch arguments only and may not represent an application's complete configuration.",
            ["本地数据"] = "Local data",
            ["所有备份和设置只保存在本机；LinkMend 不上传快捷方式或程序信息。"] = "Backups and settings stay on this computer; LinkMend does not upload shortcut or program information.",
            ["项目信息"] = "Project information",
            ["源代码：github.com/qinmuran0316/LinkMend"] = "Source: github.com/qinmuran0316/LinkMend",
            ["设计参考：ShellLink (.NET COM 互操作)、LnkParse3 (异常快捷方式解析思路)、WPF UI (Fluent 视觉方向)。本版使用原生 WPF 样式，以减少发布依赖。"] = "References: ShellLink (.NET COM interop), LnkParse3 (shortcut parsing ideas), and WPF UI (Fluent direction). This build uses native WPF styles to reduce dependencies.",
            ["语言 / Language:"] = "Language:",
            ["查找软件新位置"] = "Find new program location",
            ["同时检查安装记录和常见安装目录"] = "Checks installation records and common install folders",
            ["搜索目录"] = "Search folders",
            ["准备搜索…"] = "Preparing search…",
            ["搜索中"] = "Searching",
            ["候选程序"] = "Candidate programs",
            ["没有找到同名程序"] = "No matching program found",
            ["LinkMend 不会自动选择。请核对路径、发布者和目录后再使用。"] = "LinkMend does not guess. Check the path, publisher, and folder before using a candidate.",
            ["添加目录"] = "Add folder",
            ["重新搜索"] = "Search again",
            ["取消"] = "Cancel",
            ["使用选中位置"] = "Use selected location",
            ["预览修复"] = "Preview repair",
            ["写入前会备份原始快捷方式，写入后会重新读取验证。"] = "The original shortcut is backed up before writing and read again for verification afterwards.",
            ["修复完成后可在“修复历史”中撤销；如果文件在修复后又被其他程序修改，撤销会停止并保留新变化。"] = "After repair, use Repair History to undo. Undo stops if another program changes the file afterwards.",
            ["只修改上方列出的字段，其他快捷方式属性保持不变。"] = "Only the fields listed above are changed; other shortcut properties stay untouched.",
            ["应用修复"] = "Apply repair",
            ["当前版本："] = "Version: ",
            ["数据目录："] = "Data folder: ",
            ["历史记录："] = "History: ",
            ["设置文件："] = "Settings file: ",
            ["高可信度"] = "High confidence",
            ["中可信度"] = "Medium confidence",
            ["低可信度，请核对"] = "Low confidence; check carefully",
            ["没有可用的版本信息"] = "No version information available",
            ["搜索失败"] = "Search failed",
            ["搜索已取消。"] = "Search canceled.",
            ["可撤销"] = "Undo available",
            ["已修复"] = "Repaired",
            ["已恢复"] = "Restored",
            ["已撤销"] = "Undone",
            ["未完成"] = "Incomplete",
            ["失败"] = "Failed",
            ["部分成功"] = "Partially successful",
            ["部分撤销"] = "Partially undone",
            ["撤销完成"] = "Undo complete",
            ["部分恢复"] = "Partially restored",
            ["撤销失败"] = "Undo failed",
            ["无备注"] = "No note",
            ["无详细信息"] = "No details",
            ["默认"] = "Default",
            ["目标不存在"] = "Target missing",
            ["目标所在磁盘当前不可用"] = "Target disk unavailable",
            ["目标网络位置当前不可用"] = "Target network location unavailable",
            ["无法读取目标位置"] = "Cannot read target location",
            ["目标路径无效"] = "Invalid target path",
            ["无法确认目标状态"] = "Cannot confirm target state",
            ["无法读取快捷方式"] = "Cannot read shortcut",
            ["特殊或系统托管快捷方式"] = "Special or system-managed shortcut",
            ["快捷方式没有记录目标"] = "Shortcut has no target",
            ["起始位置不可用"] = "Working directory unavailable",
            ["起始位置不是文件夹"] = "Working directory is not a folder",
            ["图标文件不存在"] = "Icon file missing",
            ["无法确认图标状态"] = "Cannot confirm icon state",
            ["图标位置不是有效文件"] = "Icon location is not a file",
            ["图标资源无法读取"] = "Icon resource cannot be read",
            ["图标索引不可用"] = "Icon index unavailable",
            ["数据目录当前不可用"] = "Data folder unavailable",
            ["数据目录没有访问权限"] = "No access to data folder",
            ["数据目录参数不是有效路径"] = "Data folder argument is invalid",
            ["多个数据目录配置"] = "Multiple data folder configurations",
            ["未发现问题"] = "No issue found",
            ["系统命名空间快捷方式"] = "System namespace shortcut",
            ["目标丢失"] = "Target missing",
            ["目标路径"] = "Target path",
            ["系统默认图标"] = "System default icon",
            ["（未记录目标）"] = "(No target recorded)",
            ["（未设置，使用目标所在目录）"] = "(Not set; uses the target folder)",
            ["使用目标程序默认图标"] = "Use the target program's default icon",
            ["勾选后可在预览中修复"] = "Check to repair in the preview",
            ["当前问题不支持自动修复"] = "This issue does not support automatic repair",
            ["添加要搜索的文件夹"] = "Add a folder to search",
            ["原目标："] = "Original target: ",
            ["正在搜索安装记录和常见安装目录…"] = "Searching installation records and common install folders…",
            ["安装记录中没有匹配项。"] = "No match in installation records.",
            ["可以添加软件所在目录后重新搜索。"] = "You can add the program folder and search again.",
            ["所选搜索目录无效。"] = "The selected search folder is invalid.",
            ["这个搜索目录已经在列表中。"] = "This search folder is already listed.",
            ["就绪。选择扫描范围后开始检查。"] = "Ready. Select a scan scope to start checking.",
            ["正在检查快捷方式…"] = "Checking shortcuts…",
            ["正在收集快捷方式…"] = "Collecting shortcuts…",
            ["检查已取消。"] = "Check canceled.",
            ["正在自动搜索程序位置…"] = "Searching for program locations automatically…",
            ["正在取消…"] = "Canceling…",
            ["正在完成文件写入，请完成后再关闭窗口。"] = "File writing is still in progress. Finish it before closing.",
            ["正在备份并应用修复…"] = "Backing up and applying repairs…",
            ["正在从备份恢复…"] = "Restoring from backup…",
            ["检查完成："] = "Check complete: ",
            ["未发现明显问题。"] = "No obvious problems found.",
            ["准备中…"] = "Preparing…",
            ["自动搜索完成："] = "Automatic search complete: ",
            ["自动搜索完成"] = "Automatic search complete",
            ["自动搜索未找到可修复项目"] = "Automatic search found no directly repairable items",
            ["检查未完成"] = "Check incomplete",
            ["找到多个同名程序时不会自动选择，请核对路径、发布者和目录后确认。"] = "When several programs share a name, no candidate is chosen automatically. Check the path, publisher, and folder.",
            ["文件名与快捷方式记录一致"] = "File name matches the shortcut record",
            ["文件大小与原程序接近"] = "File size is close to the original",
            ["只确认了文件名；请核对发布者和所在目录后再使用"] = "Only the file name matches; check the publisher and folder before using it",
            ["产品名称一致："] = "Product name matches: ",
            ["发布者一致："] = "Publisher matches: ",
            ["文件说明一致："] = "File description matches: ",
            ["所在文件夹与原位置同名："] = "Folder name matches the original: ",
            ["系统登记来源："] = "Registered source: ",
            ["将起始位置改为目标所在目录"] = "Change the working directory to the target folder",
            ["修复或清除失效的自定义图标"] = "Repair or clear the invalid custom icon",
            ["点击“查找新位置”，从候选程序中确认正确位置后再修复。"] = "Click “Find new location”, confirm the correct candidate, then repair.",
            ["可安全自动修复："] = "Safe automatic repairs: ",
            ["修复前会先备份。"] = "The file will be backed up first.",
            ["有可自动修复的问题，请预览修改后再确认。"] = "Automatic repairs are available; review the changes before confirming.",
            ["目标将改为："] = "Target will become: ",
            ["。请预览后再应用修复。"] = ". Review the preview before applying.",
            ["未发现可确定的目标、起始位置或图标问题。"] = "No definite target, working-directory, or icon issue was found.",
            ["目标："] = "Target: ",
            ["起始位置："] = "Working directory: ",
            ["图标："] = "Icon: ",
            ["工作目录"] = "Working directory"
        };

    private static readonly Dictionary<DependencyObject, OriginalValues> OriginalText = new();

    public static event EventHandler? LanguageChanged;

    public static string CurrentLanguage { get; private set; } = Chinese;

    public static bool IsEnglish => string.Equals(CurrentLanguage, English, StringComparison.OrdinalIgnoreCase);

    public static string T(string? text)
    {
        if (string.IsNullOrEmpty(text) || !IsEnglish)
        {
            return text ?? string.Empty;
        }

        if (EnglishTranslations.TryGetValue(text, out var exact))
        {
            return exact;
        }

        var relocationMatch = Regex.Match(text, @"^为「(?<name>.+)」查找新位置$");
        if (relocationMatch.Success)
        {
            return $"Find a new location for “{relocationMatch.Groups["name"].Value}”";
        }

        var translated = text;
        foreach (var pair in EnglishTranslations.OrderByDescending(pair => pair.Key.Length))
        {
            if (translated.Contains(pair.Key, StringComparison.Ordinal))
            {
                translated = translated.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
            }
        }

        translated = Regex.Replace(
            translated,
            @"自动搜索完成：找到 (\d+) 个程序，(\d+) 个结果需要确认，(\d+) 个未找到。",
            "Automatic search complete: found $1 program(s), $2 result(s) need confirmation, $3 not found.");
        translated = Regex.Replace(
            translated,
            @"已找到并准备修复：(\d+) 项\s*自动定位程序：(\d+) 项\s*需要人工确认：(\d+) 项\s*未找到：(\d+) 项",
            "Ready to repair: $1 item(s)\nAutomatically located: $2\nNeed confirmation: $3\nNot found: $4");
        translated = Regex.Replace(
            translated,
            @"检查完成：共 (\d+) 个快捷方式，(\d+) 个无法读取",
            "Check complete: $1 shortcut(s), $2 could not be read");
        translated = Regex.Replace(
            translated,
            @"检查完成：共 (\d+) 个快捷方式",
            "Check complete: $1 shortcut(s)");
        translated = Regex.Replace(
            translated,
            @"正在读取快捷方式 (\d+) / (\d+)",
            "Reading shortcuts $1 / $2");
        translated = Regex.Replace(
            translated,
            @"已搜索 (\d+) 个目录，并按证据排序。",
            "Searched $1 folder(s), sorted by evidence.");
        translated = Regex.Replace(
            translated,
            @"(\d+) 个候选",
            "$1 candidate(s)");
        translated = Regex.Replace(
            translated,
            @"正在修改 (\d+) 个快捷方式，共 (\d+) 处字段。请确认修改前后的值。",
            "This will modify $1 shortcut(s) across $2 fields. Review the before and after values.");
        translated = Regex.Replace(
            translated,
            @"将修改 (\d+) 个快捷方式，共 (\d+) 处字段。请确认修改前后的值。",
            "This will modify $1 shortcut(s) across $2 fields. Review the before and after values.");

        return translated;
    }

    public static void SetLanguage(string? language)
    {
        var normalized = string.Equals(language, English, StringComparison.OrdinalIgnoreCase)
            ? English
            : Chinese;
        if (string.Equals(CurrentLanguage, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CurrentLanguage = normalized;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void Apply(Window window)
    {
        var visited = new HashSet<DependencyObject>();
        ApplyNode(window);

        void ApplyNode(DependencyObject node)
        {
            if (!visited.Add(node))
            {
                return;
            }

            var values = OriginalText.TryGetValue(node, out var existing)
                ? existing
                : Capture(node);
            OriginalText[node] = values;

            if (node is Window currentWindow && values.Title is not null)
            {
                currentWindow.Title = T(values.Title);
            }

            if (node is TextBlock textBlock && values.Text is not null)
            {
                textBlock.Text = T(values.Text);
            }

            if (node is ContentControl contentControl && values.Content is not null)
            {
                contentControl.Content = T(values.Content);
            }

            if (node is FrameworkElement element && values.ToolTip is not null)
            {
                element.ToolTip = T(values.ToolTip);
            }

            if (node is Visual or Visual3D)
            {
                var visualCount = VisualTreeHelper.GetChildrenCount(node);
                for (var index = 0; index < visualCount; index++)
                {
                    ApplyNode(VisualTreeHelper.GetChild(node, index));
                }
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                ApplyNode(child);
            }
        }
    }

    private static OriginalValues Capture(DependencyObject node)
    {
        var title = node is Window window && !BindingOperations.IsDataBound(window, Window.TitleProperty)
            ? window.Title
            : null;
        var text = node is TextBlock textBlock && !BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty)
            ? textBlock.Text
            : null;
        var content = node is ContentControl contentControl
                      && !BindingOperations.IsDataBound(contentControl, ContentControl.ContentProperty)
                      && contentControl.Content is string contentText
            ? contentText
            : null;
        var toolTip = node is FrameworkElement element
                      && !BindingOperations.IsDataBound(element, FrameworkElement.ToolTipProperty)
                      && element.ToolTip is string toolTipText
            ? toolTipText
            : null;

        return new OriginalValues(title, text, content, toolTip);
    }

    private sealed record OriginalValues(string? Title, string? Text, string? Content, string? ToolTip);
}
