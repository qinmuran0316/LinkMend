namespace LinkMend.Core.Paths;

public static class AtomicFile
{
    public static void WriteAllBytes(string destinationPath, ReadOnlySpan<byte> data, string? expectedHash = null)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"目录不存在：{directory}");
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.linkmend-{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 81920,
                       FileOptions.WriteThrough))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }

            if (expectedHash is not null)
            {
                ReplaceExisting(temporaryPath, destinationPath, expectedHash);
            }
            else if (File.Exists(destinationPath))
            {
                File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A stale temp file is harmless; the operation result is already known.
            }
        }
    }

    public static void ReplaceExisting(string temporaryPath, string destinationPath, string expectedHash)
    {
        FileHash.EnsureMatches(destinationPath, expectedHash);
        if ((File.GetAttributes(destinationPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new FileChangedException("快捷方式是重解析点，未执行覆盖。");
        }

        // Fail closed if replacement or metadata preservation is not supported.
        File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null);
    }
}
