namespace Workit.Shared.Payday;

/// <summary>GET /customers/{id}/accountStatement — the customer's ledger (receivables) with a running balance.</summary>
public sealed class PaydayAccountStatement
{
    public List<PaydayAccountStatementLine> Lines { get; set; } = [];
    public decimal StartBalancePage { get; set; }
    public decimal StartBalance     { get; set; }
    /// <summary>What the customer owes (positive) at the end of the range.</summary>
    public decimal EndBalance       { get; set; }
    public int PerPage { get; set; }
    public int Total   { get; set; }
    public int Page    { get; set; }
    public int Pages   { get; set; }
}

public sealed class PaydayAccountStatementLine
{
    public Guid     Id          { get; set; }
    public Guid?    EntryId     { get; set; }
    public DateTime Date        { get; set; }
    /// <summary>Positive = invoiced, negative = paid/credited.</summary>
    public decimal  Amount      { get; set; }
    public decimal  Balance     { get; set; }
    public string?  Description { get; set; }
    public string?  Currency    { get; set; }
    public string?  AccountCode { get; set; }
    public string?  AccountName { get; set; }
    public string?  Voucher     { get; set; }
    public string?  Reference   { get; set; }
    public DateTimeOffset? Created { get; set; }
}
