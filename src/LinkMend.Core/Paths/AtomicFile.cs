namespace LinkMend.Core.Paths;

public static class AtomicFile
{
    public static void WriteAllBytes(string destinationPath, ReadOnlySpan<byte> data)
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

            if (File.Exists(destinationPath))
            {
                try
                {
                    File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, destinationPath, overwrite: true);
                }
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
}
