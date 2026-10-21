namespace LinkMend.Core.Paths;

public sealed class FileChangedException : IOException
{
    public FileChangedException(string message) : base(message)
    {
    }
}
