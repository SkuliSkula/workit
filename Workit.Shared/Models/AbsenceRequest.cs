namespace Workit.Shared.Models;

public sealed class AbsenceRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public AbsenceType Type { get; set; }
    public AbsenceStatus Status { get; set; } = AbsenceStatus.Pending;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string ReviewNotes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AbsenceReviewPayload
{
    public AbsenceStatus Status { get; set; }
    public string? ReviewNotes { get; set; }
}
