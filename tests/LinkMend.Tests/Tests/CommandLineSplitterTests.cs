using LinkMend.Core.Paths;

namespace LinkMend.Tests.Tests;

public sealed class CommandLineSplitterTests
{
    [Fact]
    public void Split_HandlesQuotedPathsAndFlags()
    {
        var tokens = CommandLineSplitter.Split(
            "--user-data-dir=\"C:\\Users\\Demo User\\Profiles\\A\" --flag \"hello world\" plain");

        Assert.Equal(4, tokens.Count);
        Assert.Equal("--user-data-dir=C:\\Users\\Demo User\\Profiles\\A", tokens[0]);
        Assert.Equal("--flag", tokens[1]);
        Assert.Equal("hello world", tokens[2]);
        Assert.Equal("plain", tokens[3]);
    }

    [Fact]
    public void Split_HandlesEscapedQuotes()
    {
        var tokens = CommandLineSplitter.Split("\"a \\\"quoted\\\" value\" tail");

        Assert.Equal(2, tokens.Count);
        Assert.Equal("a \"quoted\" value", tokens[0]);
        Assert.Equal("tail", tokens[1]);
    }

    [Fact]
    public void Split_ReturnsEmptyForBlankInput()
    {
        Assert.Empty(CommandLineSplitter.Split("   "));
        Assert.Empty(CommandLineSplitter.Split(null));
    }
}
