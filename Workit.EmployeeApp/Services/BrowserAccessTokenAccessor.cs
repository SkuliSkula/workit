using Microsoft.JSInterop;
using Workit.Shared.Api;

namespace Workit.EmployeeApp.Services;

public sealed class BrowserAccessTokenAccessor(IJSRuntime jsRuntime) : IAccessTokenAccessor
{
    public async ValueTask<string?> GetAccessTokenAsync()
    {
        try
        {
            return await jsRuntime.InvokeAsync<string?>("localStorage.getItem", AuthSessionService.StorageKey);
        }
        catch (JSException)
        {
            return null;
        }
    }
}
