using LinkMend.Core.Paths;

namespace LinkMend.Tests.Tests;

public sealed class PathResolverTests
{
    [Fact]
    public void ResolveOne_ResolvesRelativePathAgainstShortcutDirectory()
    {
        var resolved = PathResolver.ResolveOne(@"..\bin\app.exe", @"C:\Links\Demo");

        Assert.Equal(@"C:\Links\bin\app.exe", resolved, ignoreCase: true);
    }

    [Fact]
    public void ResolveOne_ExpandsEnvironmentVariables()
    {
        var variable = "LINKMEND_TEST_ROOT";
        var root = @"C:\FakeRoot\Apps";
        Environment.SetEnvironmentVariable(variable, root);
        try
        {
            var resolved = PathResolver.ResolveOne($@"%{variable}%\Demo\app.exe", @"C:\Links");
            Assert.Equal(@"C:\FakeRoot\Apps\Demo\app.exe", resolved, ignoreCase: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void ResolveOne_RemovesSurroundingQuotes()
    {
        var resolved = PathResolver.ResolveOne("\"C:\\Apps\\Demo App\\demo.exe\"", @"C:\Links");
        Assert.Equal(@"C:\Apps\Demo App\demo.exe", resolved, ignoreCase: true);
    }

    [Fact]
    public void NormalizeKey_IgnoresCaseAndTrailingSeparator()
    {
        Assert.Equal(
            PathResolver.NormalizeKey(@"C:\Apps\Demo\"),
            PathResolver.NormalizeKey(@"c:\apps\demo"));
    }

    [Fact]
    public void IsShellNamespace_RecognizesShellPaths()
    {
        Assert.True(PathResolver.IsShellNamespace(@"shell:AppsFolder\Something"));
        Assert.True(PathResolver.IsShellNamespace(@"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"));
        Assert.False(PathResolver.IsShellNamespace(@"C:\Windows\explorer.exe"));
    }

    [Theory]
    [InlineData(@"C:\Program Files\App\app.exe", true)]
    [InlineData(@"C:\Icons\app.ico", true)]
    [InlineData(@"C:\Docs\readme.txt", false)]
    public void IsIconCapable_UsesSupportedExtensions(string path, bool expected)
    {
        Assert.Equal(expected, PathResolver.IsIconCapable(path));
    }
}
