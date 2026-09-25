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

    /// <summary>
    /// Hours absent on each day of the range. A full day is the company's
    /// standard day; 4 records half a day, for someone who came in, worked the
    /// morning and went home ill. Never more than a standard day — you cannot
    /// be absent from more duty than the day holds.
    /// </summary>
    public decimal HoursPerDay { get; set; } = 8m;
    public string Notes { get; set; } = string.Empty;
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string ReviewNotes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Where this record came from. Defaults to Workit for manually created records.</summary>
    public DataSource Source { get; set; } = DataSource.Workit;
}

public sealed class AbsenceReviewPayload
{
    public AbsenceStatus Status { get; set; }
    public string? ReviewNotes { get; set; }
}
