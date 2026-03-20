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
}
