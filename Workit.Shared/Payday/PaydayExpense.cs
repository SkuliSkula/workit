namespace Workit.Shared.Payday;

public sealed class PaydayExpense
{
    public Guid      Id                  { get; set; }

    /// <summary>DRAFT | UNPAID | PAID</summary>
    public string    Status              { get; set; } = string.Empty;

    /// <summary>Object containing the expense creditor (id, ssn, name).</summary>
    public PaydayExpenseCreditor? Creditor { get; set; }

    public DateTime? PaidDate            { get; set; }
    public PaydayPaymentTypeRef? PaymentType { get; set; }

    public DateTime? Date                { get; set; }
    public DateTime? DueDate             { get; set; }
    public DateTime? FinalDueDate        { get; set; }

    public string?   Reference           { get; set; }
    public bool      Deductible          { get; set; }
    public string?   Comments            { get; set; }

    public decimal   AmountExcludingVat  { get; set; }
    public decimal   AmountIncludingVat  { get; set; }
    public decimal   AmountVat           { get; set; }

    public string?   Voucher             { get; set; }
    public DateTime? Created             { get; set; }

    public List<PaydayExpenseLine>? Lines { get; set; }
}

public sealed class PaydayExpenseCreditor
{
    public Guid?   Id   { get; set; }
    public string? Ssn  { get; set; }
    public string? Name { get; set; }
}

public sealed class PaydayPaymentTypeRef
{
    public Guid?   Id   { get; set; }
    public string? Name { get; set; }
}
