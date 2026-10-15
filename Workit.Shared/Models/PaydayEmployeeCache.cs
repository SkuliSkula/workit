namespace Workit.Shared.Models;

/// <summary>
/// Workit's copy of one employee in the company's Payday payroll. Payday owns
/// these; Workit stores them so nothing about payroll staff has to be fetched
/// live, and links its own <see cref="Employee"/> to one by SSN
/// (<see cref="Employee.PaydayId"/>). The Workit employee is never rewritten
/// from here — the owner's "Sync employees" on the Payday page does that, on purpose.
/// </summary>
public sealed class PaydayEmployeeCache
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid PaydayId  { get; set; }

    public string? Ssn     { get; set; }
    public string? Name    { get; set; }
    public string? Address { get; set; }
    public string? Zip     { get; set; }
    public string? City    { get; set; }
    public string? Mobile  { get; set; }
    public string? Email   { get; set; }

    /// <summary>Payday's own active flag.</summary>
    public bool Active { get; set; }

    /// <summary>No longer in Payday's list. Kept, not deleted: payroll history may name them.</summary>
    public bool Archived { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}

/// <summary>Outcome of one Payday → Workit employee sync for a company.</summary>
/// <param name="Linked">Workit employees matched to a Payday employee by SSN this run.</param>
public sealed record PaydayEmployeeSyncResult(int Fetched, int Added, int Updated, int Archived, int Linked, DateTimeOffset SyncedAt);
