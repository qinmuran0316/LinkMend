using LinkMend.Core.Paths;
using LinkMend.Tests.Support;

namespace LinkMend.Tests.Tests;

public sealed class AtomicFileTests
{
    [Fact]
    public void WriteAllBytes_WhenExpectedHashChanged_PreservesNewContent()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateFile("Demo.lnk", "original");
        var expectedHash = FileHash.Compute(path);
        File.WriteAllText(path, "new-user-content");

        Assert.Throws<FileChangedException>(() => AtomicFile.WriteAllBytes(path, new byte[] { 1 }, expectedHash));
        Assert.Equal("new-user-content", File.ReadAllText(path));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.tmp"));
    }

    [Fact]
    public void WriteAllBytes_WhenExpectedFileDeleted_DoesNotRecreateIt()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateFile("Demo.lnk", "original");
        var expectedHash = FileHash.Compute(path);
        File.Delete(path);

        Assert.Throws<FileChangedException>(() => AtomicFile.WriteAllBytes(path, new byte[] { 1 }, expectedHash));
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.tmp"));
    }

    [Fact]
    public void WriteAllBytes_ExistingReadOnlyFile_IsNotReplaced()
    {
        using var temp = new TempDirectory();
        var path = temp.CreateFile("protected.lnk", "original");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllBytes(path, new byte[] { 1, 2, 3 }));
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.tmp"));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }
}
