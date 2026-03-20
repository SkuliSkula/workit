namespace Workit.Shared.Payday;

public sealed class CreateExpenseRequest
{
    /// <summary>Required. The expense creditor (id, ssn, name).</summary>
    public required PaydayExpenseCreditor Creditor { get; set; }

    /// <summary>Required. Format: YYYY-MM-DD</summary>
    public required string Date { get; set; }

    /// <summary>Required. DRAFT | UNPAID | PAID</summary>
    public required string Status { get; set; }

    /// <summary>Optional. Format: YYYY-MM-DD</summary>
    public string? PaidDate { get; set; }

    /// <summary>Optional. Set this property to mark the expense as paid. Reference: Expenses → Payment Types.</summary>
    public PaydayPaymentTypeRef? PaymentType { get; set; }

    /// <summary>Optional. Format: YYYY-MM-DD</summary>
    public string? DueDate { get; set; }

    /// <summary>Optional. Format: YYYY-MM-DD</summary>
    public string? FinalDueDate { get; set; }

    /// <summary>Optional.</summary>
    public string? Reference { get; set; }

    /// <summary>Required. Is the VAT deductible.</summary>
    public required bool Deductible { get; set; }

    /// <summary>Optional.</summary>
    public string? Comments { get; set; }

    /// <summary>Required. At least one line.</summary>
    public required List<CreateExpenseLineRequest> Lines { get; set; }
}

public sealed class CreateExpenseLineRequest
{
    /// <summary>Required.</summary>
    public required string Description { get; set; }

    /// <summary>Required.</summary>
    public required int Quantity { get; set; }

    /// <summary>One of unitPriceExcludingVat or unitPriceIncludingVat is required.</summary>
    public decimal? UnitPriceExcludingVat { get; set; }

    /// <summary>One of unitPriceExcludingVat or unitPriceIncludingVat is required.</summary>
    public decimal? UnitPriceIncludingVat { get; set; }

    /// <summary>Required.</summary>
    public required decimal VatPercentage { get; set; }

    /// <summary>Optional.</summary>
    public decimal? DiscountPercentage { get; set; }

    /// <summary>Required. Reference: Expenses → Accounts.</summary>
    public required Guid AccountId { get; set; }
}
