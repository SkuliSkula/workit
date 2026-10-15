namespace Workit.Shared.Models;

/// <summary>
/// Workit's stored copy of one small piece of Payday reference data for a
/// company — its company profile, its Payday users, and lists such as expense
/// accounts — kept as Payday returned it (JSON) and refreshed by the sync.
/// Records with a life of their own (invoices, expenses, customers, employees)
/// have proper tables instead.
/// </summary>
public sealed class PaydaySnapshot
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }

    /// <summary>What this is, e.g. <c>company</c> or <c>users</c>; one per company.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Json { get; set; } = string.Empty;

    public DateTimeOffset SyncedAt { get; set; }
}
