namespace Workit.Shared.Payday;

public sealed class UpdateCustomerRequest
{
    /// <summary>Optional.</summary>
    public string? Language { get; set; }

    /// <summary>Optional.</summary>
    public string? Name { get; set; }

    /// <summary>Optional.</summary>
    public string? Address { get; set; }

    /// <summary>Optional.</summary>
    public string? ZipCode { get; set; }

    /// <summary>Optional.</summary>
    public string? City { get; set; }

    /// <summary>Optional.</summary>
    public string? Country { get; set; }

    /// <summary>Optional.</summary>
    public string? Email { get; set; }

    /// <summary>Optional.</summary>
    public string? Contact { get; set; }

    /// <summary>Optional.</summary>
    public string? Comment { get; set; }

    /// <summary>Optional.</summary>
    public bool? SendElectronicInvoices { get; set; }

    /// <summary>Optional.</summary>
    public int? FinalDueDateDefaultDaysAfter { get; set; }

    /// <summary>Optional. Set to refetch data from Registers Iceland.</summary>
    public bool? Refetch { get; set; }
}
