namespace Workit.Shared.Models;

public sealed class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Ssn { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public decimal DrivingUnitPrice { get; set; }
    public decimal StandardHoursPerDay { get; set; } = 8m;

    /// <summary>
    /// Names of the company's Payday payroll items that Workit's hour kinds are
    /// uploaded as. Must match the item names in Payday exactly; an empty
    /// driving name means driving units are not sent to payroll.
    /// </summary>
    public string PayrollRegularItemName  { get; set; } = "Dagvinna";
    public string PayrollOvertimeItemName { get; set; } = "Yfirvinna";
    public string PayrollDrivingItemName  { get; set; } = string.Empty;

    public string ZipCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string VatNumber { get; set; } = string.Empty;

    // Payday integration credentials (per-company).
    // Write-only: clients send these when creating a company or via the
    // payday-credentials endpoint; the API stores them encrypted and never
    // returns them. Responses carry HasPaydayCredentials instead.
    public string? PaydayClientId { get; set; }
    public string? PaydayClientSecret { get; set; }

    /// <summary>
    /// True when Payday credentials are configured for this company.
    /// Set by the API on responses only; not stored in the database.
    /// </summary>
    public bool HasPaydayCredentials { get; set; }

    /// <summary>Where this record came from. Defaults to Workit for manually created records.</summary>
    public DataSource Source { get; set; } = DataSource.Workit;

    /// <summary>The corresponding Payday company id, when created from Payday.</summary>
    public Guid? PaydayId { get; set; }
}
