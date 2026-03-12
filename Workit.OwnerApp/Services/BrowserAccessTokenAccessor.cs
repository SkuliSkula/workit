using Microsoft.JSInterop;
using Workit.Shared.Api;

namespace Workit.OwnerApp.Services;

public sealed class BrowserAccessTokenAccessor(IJSRuntime jsRuntime) : IAccessTokenAccessor
{
    public async ValueTask<string?> GetAccessTokenAsync()
    {
        try
        {
            return await jsRuntime.InvokeAsync<string?>("localStorage.getItem", AuthSessionService.StorageKey);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (JSException)
        {
            return null;
        }
    }
}
