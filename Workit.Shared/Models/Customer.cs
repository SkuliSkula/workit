namespace Workit.Shared.Models;

public sealed class Customer : ICreatedAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Ssn { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;

    /// <summary>
    /// Whether new jobs for this customer open with a Tasks section. A default
    /// only — it never restricts: any job can get tasks, and a job with none
    /// behaves as before. Not touched by the Payday customer sync.
    /// </summary>
    public bool PlanJobsInTasks { get; set; }

    /// <summary>Where this record came from. Defaults to Workit for manually created records.</summary>
    public DataSource Source { get; set; } = DataSource.Workit;

    /// <summary>The corresponding Payday customer id, when linked to Payday (synced from it or pushed to it).</summary>
    public Guid? PaydayId { get; set; }

    /// <summary>
    /// A Workit edit that has not reached Payday yet — set when the write-through
    /// failed (Payday down, validation) and cleared when the background sync
    /// pushes it. Saving in Workit never waits on Payday.
    /// </summary>
    public bool PaydayPushPending { get; set; }

    /// <summary>
    /// Payday's own message from the last failed push, for the owner to act on —
    /// or, on a linked customer, a warning from the last accepted push (Payday
    /// dropped an SSN it did not recognise).
    /// </summary>
    public string? PaydayPushError { get; set; }

    /// <summary>Last time this row and Payday agreed (push or pull).</summary>
    public DateTimeOffset? PaydaySyncedAt { get; set; }

    /// <inheritdoc/>
    public DateTimeOffset? CreatedAt { get; set; }
    /// <inheritdoc/>
    public Guid? CreatedByUserId { get; set; }
    /// <inheritdoc/>
    public string? CreatedByName { get; set; }
}
