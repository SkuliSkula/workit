using System.Text.Json.Serialization;

namespace Workit.Shared.Payday;

internal sealed class PaydayTokenResponse
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    public string? GetToken() => AccessToken;
}
