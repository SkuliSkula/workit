namespace Workit.Shared.Payday;

public sealed class PaydayInvoicesResponse
{
    public List<PaydayInvoice> Invoices { get; set; } = [];
    public int PerPage { get; set; }
    public int Total   { get; set; }
    public int Page    { get; set; }
    public int Pages   { get; set; }
    public PaydayInvoicesSummary? Summary { get; set; }
}

public sealed class PaydayInvoicesSummary
{
    public int     TotalCount   { get; set; }
    public decimal PaidAmount   { get; set; }
    public decimal UnpaidAmount { get; set; }
}
