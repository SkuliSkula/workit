namespace Workit.Shared.Models;

public sealed class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
