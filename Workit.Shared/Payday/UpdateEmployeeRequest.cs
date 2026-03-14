namespace Workit.Shared.Payday;

public sealed class UpdateEmployeeRequest
{
    /// <summary>Optional.</summary>
    public string? Name { get; set; }

    /// <summary>Optional.</summary>
    public string? Address { get; set; }

    /// <summary>Optional.</summary>
    public string? Zip { get; set; }

    /// <summary>Optional.</summary>
    public string? City { get; set; }

    /// <summary>Optional.</summary>
    public string? Mobile { get; set; }

    /// <summary>Optional.</summary>
    public string? Email { get; set; }

    /// <summary>Optional.</summary>
    public bool? Active { get; set; }
}
