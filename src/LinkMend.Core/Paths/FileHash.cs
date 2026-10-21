using System.Security.Cryptography;

namespace LinkMend.Core.Paths;

public static class FileHash
{
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
