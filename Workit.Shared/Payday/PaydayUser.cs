using System.Text.Json.Serialization;

namespace Workit.Shared.Payday;

public sealed class PaydayUser
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public string? Language { get; set; }

    [JsonPropertyName("isowner")]
    public bool IsOwner { get; set; }

    [JsonPropertyName("logincount")]
    public int LoginCount { get; set; }

    [JsonPropertyName("lastlogindate")]
    public DateTime? LastLoginDate { get; set; }

    public DateTime Created { get; set; }
    public DateTime Edited { get; set; }
}
