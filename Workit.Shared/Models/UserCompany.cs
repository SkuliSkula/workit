namespace Workit.Shared.Models;

public sealed class UserCompany
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
