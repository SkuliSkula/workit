namespace Workit.Shared.Api;

public interface IAccessTokenAccessor
{
    ValueTask<string?> GetAccessTokenAsync();
}

internal sealed class NoOpAccessTokenAccessor : IAccessTokenAccessor
{
    public ValueTask<string?> GetAccessTokenAsync() => ValueTask.FromResult<string?>(null);
}
