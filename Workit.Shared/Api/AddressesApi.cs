namespace Workit.Shared.Api;

using Workit.Shared.Models;

/// <summary>Address typeahead (Staðfangaskrá) and contact suggestions for the job form's "For the crew" section.</summary>
public interface IAddressesApi
{
    Task<ApiResult<AddressSearchResult>> SearchAsync(string query);
    Task<ApiResult<List<ContactSuggestion>>> ContactSuggestionsAsync(int? landNr, string? location, Guid? customerId, Guid? excludeJobId);
}

internal sealed class AddressesApi(HttpClient httpClient, IAccessTokenAccessor accessTokenAccessor)
    : ApiClientBase(httpClient, accessTokenAccessor), IAddressesApi
{
    public Task<ApiResult<AddressSearchResult>> SearchAsync(string query) =>
        GetAsync<AddressSearchResult>($"api/addresses/search?q={Uri.EscapeDataString(query)}", "Address lookup is not available right now.");

    public async Task<ApiResult<List<ContactSuggestion>>> ContactSuggestionsAsync(int? landNr, string? location, Guid? customerId, Guid? excludeJobId)
    {
        var qs = new List<string>();
        if (landNr is not null) qs.Add($"landNr={landNr}");
        if (!string.IsNullOrWhiteSpace(location)) qs.Add($"location={Uri.EscapeDataString(location)}");
        if (customerId is not null) qs.Add($"customerId={customerId}");
        if (excludeJobId is not null) qs.Add($"excludeJobId={excludeJobId}");
        var result = await GetAsync<List<ContactSuggestion>>("api/jobs/contact-suggestions?" + string.Join('&', qs), "Contact suggestions could not be loaded.");
        return result.IsSuccess
            ? ApiResult<List<ContactSuggestion>>.Success(result.Value ?? [])
            : ApiResult<List<ContactSuggestion>>.Failure(result.ErrorMessage ?? "Contact suggestions could not be loaded.");
    }
}
