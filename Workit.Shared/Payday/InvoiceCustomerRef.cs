namespace Workit.Shared.Payday;

/// <summary>Nested customer/payor reference used in invoice requests: { "id": "..." }</summary>
public sealed class InvoiceCustomerRef
{
    public required Guid Id { get; set; }
}
