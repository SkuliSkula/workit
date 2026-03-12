namespace Workit.Api.Auth;

public sealed class AdminSeedOptions
{
    public const string SectionName = "AdminSeed";

    public string Email { get; set; } = "admin@workit.local";
    public string Password { get; set; } = "Admin123!";
}
