namespace Workit.Shared.Api;

/// <summary>What one automatic linking pass over the company's Payday expenses did.</summary>
public sealed class ExpenseAutoLinkResult
{
    /// <summary>Payday expenses looked at.</summary>
    public int Scanned { get; set; }
    /// <summary>Expenses newly linked to a job in this pass.</summary>
    public List<ExpenseAutoLinkHit> Linked { get; set; } = [];
    /// <summary>Expenses that mention more than one job — left for the owner to decide.</summary>
    public List<ExpenseAutoLinkAmbiguity> Ambiguous { get; set; } = [];
    /// <summary>Expenses already linked, or unlinked by an owner, that were left alone.</summary>
    public int Skipped { get; set; }
}

public sealed class ExpenseAutoLinkHit
{
    public Guid PaydayExpenseId { get; set; }
    public string? CreditorName { get; set; }
    public decimal AmountIncludingVat { get; set; }
    public Guid JobId { get; set; }
    public string JobCode { get; set; } = string.Empty;
    public string JobName { get; set; } = string.Empty;
}

public sealed class ExpenseAutoLinkAmbiguity
{
    public Guid PaydayExpenseId { get; set; }
    public string? CreditorName { get; set; }
    public List<string> JobCodes { get; set; } = [];
}
