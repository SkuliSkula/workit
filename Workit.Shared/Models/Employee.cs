namespace Workit.Shared.Models;

public sealed class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Trade { get; set; } = string.Empty;
    public string Ssn { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public EmploymentType EmploymentType { get; set; } = EmploymentType.Employed;
    public decimal HourlySalary { get; set; }
    public decimal HourlyBillableRate { get; set; }

    public string Address { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;

    /// <summary>Whether the employee is active in the source system (Payday).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Where this record came from. Defaults to Workit for manually created records.</summary>
    public DataSource Source { get; set; } = DataSource.Workit;

    /// <summary>The corresponding Payday employee id, when synced from Payday.</summary>
    public Guid? PaydayId { get; set; }
}
