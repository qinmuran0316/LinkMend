using LinkMend.Core.Diagnosis;
using LinkMend.Core.Models;
using LinkMend.Core.Scan;
using LinkMend.Tests.Support;

namespace LinkMend.Tests.Tests;

public sealed class ShortcutScannerTests
{
    [Fact]
    public void Scan_ReadsLinksDeduplicatesExplicitFilesAndCountsFailures()
    {
        using var temp = new TempDirectory();
        var target = temp.CreateFile(@"Apps\Demo\demo.exe");
        var desktop = temp.CreateDirectory("Desktop");
        var healthyPath = temp.CreateFile(@"Desktop\Healthy.lnk", "fake-bytes");
        var brokenPath = temp.CreateFile(@"Desktop\Broken.lnk", "fake-bytes");
        var explicitPath = temp.CreateFile(@"Elsewhere\Explicit.lnk", "fake-bytes");

        var probe = new FakeSystemProbe();
        probe.SetFile(target);
        var store = new FakeShortcutStore();
        store.Links[healthyPath] = new ShortcutLink
        {
            FilePath = healthyPath,
            DisplayName = "Healthy",
            TargetPath = target
        };
        store.Links[explicitPath] = new ShortcutLink
        {
            FilePath = explicitPath,
            DisplayName = "Explicit",
            TargetPath = target
        };
        // Broken.lnk is deliberately absent from the store to exercise read failure.

        var diagnoser = new ShortcutDiagnoser(probe);
        var scanner = new ShortcutScanner(store, diagnoser);
        var progress = new List<ScanProgress>();
        var result = scanner.Scan(
            new ScanRequest(
                new[] { new ScanRoot(desktop, "桌面") },
                new[] { explicitPath, healthyPath }),
            new SynchronousProgress<ScanProgress>(progress.Add),
            CancellationToken.None);

        Assert.Equal(3, result.Total);
        Assert.Equal(1, result.FailedReads);
        Assert.Single(result.Shortcuts, item => item.Diagnosis.Health == ShortcutHealth.Unreadable);
        Assert.Equal(3, result.Shortcuts.Count(item => item.ScopeLabel is "单独添加" or "桌面"));
        Assert.Contains(result.Shortcuts, item => item.Link.FilePath == healthyPath);
        Assert.Contains(result.Shortcuts, item => item.Link.FilePath == brokenPath);
        Assert.Contains(result.Shortcuts, item => item.Link.FilePath == explicitPath);
        Assert.NotEmpty(progress);
        Assert.Equal(3, progress[^1].Total);
    }

    [Fact]
    public void Scan_WhenCanceledWithEmptyScope_ThrowsCancellation()
    {
        var scanner = new ShortcutScanner(new FakeShortcutStore(), new ShortcutDiagnoser(new FakeSystemProbe()));
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => scanner.Scan(
            new ScanRequest(Array.Empty<ScanRoot>(), Array.Empty<string>()), null, source.Token));
    }

    [Fact]
    public void Scan_WhenFileChangesDuringRead_DisablesAutomaticRepair()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateFile("Demo.lnk", "original-bytes");
        var store = new FakeShortcutStore();
        store.Links[path] = new ShortcutLink { FilePath = path, TargetPath = @"C:\Missing\demo.exe" };
        store.OnRead = file => File.WriteAllText(file, "changed-during-read");
        var scanner = new ShortcutScanner(store, new ShortcutDiagnoser(new FakeSystemProbe()));

        var result = scanner.Scan(new ScanRequest(Array.Empty<ScanRoot>(), new[] { path }), null, CancellationToken.None);

        var shortcut = Assert.Single(result.Shortcuts);
        Assert.Empty(shortcut.ContentHash);
        Assert.Contains(shortcut.Diagnosis.Issues, issue => issue.Code == IssueCodes.HashUnavailable);
    }

    [Fact]
    public void Scan_EmptyScope_ReturnsEmptyResult()
    {
        using var temp = new TempDirectory();
        var store = new FakeShortcutStore();
        var scanner = new ShortcutScanner(
            store,
            new ShortcutDiagnoser(new FakeSystemProbe()));

        var result = scanner.Scan(
            new ScanRequest(Array.Empty<ScanRoot>(), Array.Empty<string>()),
            null,
            CancellationToken.None);

        Assert.Empty(result.Shortcuts);
        Assert.Equal(0, result.FailedReads);
    }

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public SynchronousProgress(Action<T> report)
        {
            _report = report;
        }

        public void Report(T value) => _report(value);
    }
}
