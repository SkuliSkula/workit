using System.Text.Json.Serialization;

namespace Workit.Shared.Payday;

// Update these property names to match the actual API response once known.
internal sealed class PaydayTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    public string? GetToken() => AccessToken ?? Token;
}
