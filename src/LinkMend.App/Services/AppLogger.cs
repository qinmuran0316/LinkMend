namespace LinkMend.App.Services;

public static class AppLogger
{
    private static readonly object Gate = new();
    private static string? _logPath;

    public static void Initialize(string rootDirectory)
    {
        _logPath = Path.Combine(rootDirectory, "linkmend.log");
    }

    public static void Error(Exception exception, string context)
    {
        if (string.IsNullOrWhiteSpace(_logPath))
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    _logPath,
                    $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never replace the original error.
        }
    }
}
