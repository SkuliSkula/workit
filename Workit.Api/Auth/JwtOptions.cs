namespace Workit.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "Workit.Api";
    public string Audience { get; set; } = "Workit.Apps";
    public string SigningKey { get; set; } = "workit-dev-signing-key-change-me-2026";
    public int ExpirationMinutes { get; set; } = 480;
}
