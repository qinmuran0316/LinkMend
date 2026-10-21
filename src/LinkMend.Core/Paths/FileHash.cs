using System.Security.Cryptography;

namespace LinkMend.Core.Paths;

public static class FileHash
{
    public static bool IsValid(string? hash) =>
        hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);

    public static void EnsureMatches(string path, string expectedHash)
    {
        if (!IsValid(expectedHash))
        {
            throw new ArgumentException("无效的文件校验值。", nameof(expectedHash));
        }

        try
        {
            if (string.Equals(Compute(path), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileChangedException("快捷方式已被删除或移动，未执行覆盖。");
        }

        throw new FileChangedException("快捷方式内容发生变化，未执行覆盖。请重新扫描。");
    }

    public static string Compute(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string Compute(string path)
    {
        using var stream = OpenShared(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static bool TryCompute(string path, out string hash, out string? error)
    {
        try
        {
            hash = Compute(path);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            hash = "";
            error = ex.Message;
            return false;
        }
    }

    public static byte[] ReadAllBytesShared(string path)
    {
        using var stream = OpenShared(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
