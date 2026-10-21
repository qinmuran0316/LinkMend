namespace LinkMend.Tests.Support;

internal sealed class CallbackTimeProvider : TimeProvider
{
    public Action? OnGetUtcNow { get; set; }

    public override DateTimeOffset GetUtcNow()
    {
        OnGetUtcNow?.Invoke();
        return DateTimeOffset.UtcNow;
    }
}
