using System.Windows;
using System.Windows.Threading;
using LinkMend.App.Services;
using LinkMend.App.ViewModels;
using LinkMend.Core.Backup;
using LinkMend.Core.Diagnosis;
using LinkMend.Core.Relocation;
using LinkMend.Core.Repair;
using LinkMend.Core.Scan;
using LinkMend.Windows;
using LinkMend.Windows.Search;

namespace LinkMend.App;

public partial class App : Application
{
    private Mutex? _runningMarker;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Path.Combine(AppContext.BaseDirectory, "LinkMendData")
            : Path.Combine(localAppData, "LinkMend");

        Directory.CreateDirectory(root);
        AppLogger.Initialize(root);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppLogger.Error(exception, "未处理的进程异常");
            }
        };

        try
        {
            // The installer checks for this object before upgrading or uninstalling.
            // No ownership is taken, so multiple application windows remain supported.
            _runningMarker = new Mutex(false, @"Local\LinkMend.Running");
            var probe = new WindowsSystemProbe();
            var store = new WindowsShortcutStore();
            var diagnoser = new ShortcutDiagnoser(probe);
            var scanner = new ShortcutScanner(store, diagnoser);
            var backups = new BackupRepository(root);
            var settingsStore = new AppSettingsStore(root);
            var planBuilder = new RepairPlanBuilder(probe);
            var repairService = new RepairService(store, backups, probe);
            var undoService = new UndoService(backups);
            var relocationService = new RelocationService(
                probe,
                new WindowsInstallationIndex(),
                new WindowsExecutableSearch(),
                new WindowsSearchRootProvider(probe));

            var viewModel = new MainViewModel(
                scanner,
                relocationService,
                planBuilder,
                repairService,
                undoService,
                backups,
                settingsStore);

            var window = new MainWindow(viewModel);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "启动 LinkMend");
            MessageBox.Show(
                $"LinkMend 启动失败：{ex.Message}\n\n详细信息已写入本地日志。",
                "LinkMend",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _runningMarker?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error(e.Exception, "界面线程未处理异常");
        MessageBox.Show(
            $"发生未处理的错误：{e.Exception.Message}\n\n详细信息已写入本地日志。",
            "LinkMend",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
